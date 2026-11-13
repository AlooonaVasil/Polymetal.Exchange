using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Polymetal.Exchange.Utilities;

namespace Polymetal.Exchange.Service
{
    class RunService
    {
        private static string pathPolymetalPdmsStart;
        private static string pathPolymetalExchangeTeklav20;
        private static string pathPolymetalExchangeTeklav21;
        private static string pathPolymetalExchangeTeklav211;
        private static string pathPolymetalExchangeTeklav2017i;
        private static string pathPolymetalExchangeTeklav2018;
        private static string pathPolymetalExchangeTeklav2021;
        private static string pathPolymetalExchangeE3D;
        private static bool _checkMagicTriggers = true;
        private static bool _checkTeklaTriggers = true;
        private static bool _checkRevitTriggers = true;
        private static bool _checkAvevaTriggers = true;

        static void Main(string[] args)
        {
            if (args.Length != 4 && args.Length != 5)
            {
                Console.WriteLine("Недостаточно аргументов");
                Console.WriteLine("1: [обрабатывать Tekla]");
                Console.WriteLine("2: [обрабатывать MagiCAD]");
                Console.WriteLine("3: [обрабатывать Revit]");
                Console.WriteLine("4: [обрабатывать Aveva]");
                Console.WriteLine("5: [кол-во параллельных Aveva задач] (опционально, в Aveva-only по умолчанию 2, макс. 2)");
                return;
            }
            var queue = new List<TriggerInfo>();
            Unpacking();

            _checkTeklaTriggers = args[0].Equals("true");
            _checkMagicTriggers = args[1].Equals("true");
            _checkRevitTriggers = args[2].Equals("true");
            _checkAvevaTriggers = args[3].Equals("true");

            Console.WriteLine($"Waiting Tekla triggers  \t{(_checkTeklaTriggers ? "enabled" : "disabled")}");
            Console.WriteLine($"Waiting MagiCAD triggers\t{(_checkMagicTriggers ? "enabled" : "disabled")}");
            Console.WriteLine($"Waiting Revit triggers  \t{(_checkRevitTriggers ? "enabled" : "disabled")}");
            Console.WriteLine($"Waiting Aveva triggers  \t{(_checkAvevaTriggers ? "enabled" : "disabled")}");

            var avevaOnlyMode = _checkAvevaTriggers && !_checkTeklaTriggers && !_checkMagicTriggers && !_checkRevitTriggers;
            var avevaParallelism = avevaOnlyMode ? 2 : 1;
            if (args.Length == 5)
            {
                int parsed;
                if (int.TryParse(args[4], out parsed))
                {
                    avevaParallelism = parsed;
                }
            }
            if (avevaParallelism < 1) avevaParallelism = 1;
            if (!avevaOnlyMode) avevaParallelism = 1; // безопасность: параллелим только в Aveva-only режиме
            if (avevaParallelism > 2) avevaParallelism = 2; // жесткий лимит: максимум 2 Aveva процесса

            Console.WriteLine($"Aveva parallelism       \t{avevaParallelism}");

            var running = new Dictionary<int, Task<TriggerInfo>>();

            while (true)
            {
                var sqlQueue = GetTriggerQueue();
                foreach (var triggerInfo in sqlQueue)
                {
                    // В режиме "только Aveva" обрабатываем только экспорт из Aveva (Pdms, Status=0).
                    // Статус=1 для Pdms означает стадию "импорт Aveva в Tekla", которая выполняется Tekla-процессом.
                    if (avevaOnlyMode && triggerInfo.Source == TriggerInfo.TriggerSource.Pdms && triggerInfo.Status == 1)
                        continue;

                    if (!triggerInfo.NeedExchange)
                    {
                        //отсекаем триггеры, которые не надо обрабатывать (замороженный объект или объект не имеющий связи с PDMS)
                        triggerInfo.Status = 3;
                        SendTriggerState(triggerInfo.Id, triggerInfo.Status);
                        continue;
                    }

                    // уже в очереди или уже выполняется (параллельный режим): не дублировать — иначе счётчик queue
                    // растёт «фантомами», которые никогда не возьмутся в работу (running.ContainsKey).
                    if (queue.Any(x => x.Id == triggerInfo.Id) || running.ContainsKey(triggerInfo.Id))
                        continue;

                    //убираем предыдущие аналогичные записи из очереди (выгрузка из той же программы и по тому же объекту)
                    var sameTrigger = queue.Where(x => x.Source == triggerInfo.Source && x.ProjectId == triggerInfo.ProjectId).ToList();
                    foreach (var sameInfo in sameTrigger)
                    {
                        sameInfo.Status = 3;
                        SendTriggerState(sameInfo.Id, sameInfo.Status);
                        queue.Remove(sameInfo);
                    }

                    //добавление новой задачи в очередь
                    queue.Add(triggerInfo);
                }

                if (avevaParallelism > 1)
                {
                    // 1) собрать завершившиеся
                    var completed = running.Where(kvp => kvp.Value.IsCompleted).Select(kvp => kvp.Key).ToList();
                    foreach (var triggerId in completed)
                    {
                        var t = running[triggerId];
                        running.Remove(triggerId);

                        TriggerInfo finished;
                        try
                        {
                            finished = t.GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            // Обычно не срабатывает: Task.Run внутри глотает исключения и возвращает TriggerInfo.
                            Console.WriteLine(
                                $"[{DateTime.Now.ToLocalTime()}] Неожиданная ошибка при завершении задачи trigger {triggerId}: {ex.GetBaseException().Message}");
                            continue;
                        }

                        if (finished.Status < 2)
                        {
                            // если задача не завершилась (например, нет лицензии), возвращаем в очередь
                            if (!queue.Any(x => x.Id == finished.Id))
                                queue.Add(finished);
                        }
                        else
                        {
                            // убрать возможные дубликаты по Id (до фикса они могли накопиться)
                            queue.RemoveAll(x => x.Id == finished.Id);
                        }
                    }

                    // 2) запустить новые задачи, пока есть слоты
                    while (running.Count < avevaParallelism)
                    {
                        var next = queue.FirstOrDefault(x => x.Source == TriggerInfo.TriggerSource.Pdms && x.Status == 0 && !running.ContainsKey(x.Id));
                        if (next == null) break;

                        queue.Remove(next);
                        var captured = next;
                        running[captured.Id] = Task.Run(() =>
                        {
                            try
                            {
                                DoTask(ref captured);
                                return captured;
                            }
                            catch
                            {
                                return captured;
                            }
                        });
                    }
                }
                else
                {
                    var task = queue.FirstOrDefault();
                    if (task != null)
                    {
                        DoTask(ref task);
                        if (task.Status < 2)
                        {
                            //перемещение в конец очереди
                            queue.Remove(task);
                            queue.Add(task);
                        }
                        else
                        {
                            //удаление отработанных
                            queue.Remove(task);
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] количество задач в очереди:\t{queue.Count}");
                if (avevaParallelism > 1)
                {
                    Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] Aveva задач выполняется:\t{running.Count}");
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(10000);
            }
        }

        private static void DoTask(ref TriggerInfo task)
        {
            //не нуждается в обработке
            if (task.Status >= 2) return;

            Console.WriteLine("Проект для обработки " + task.ProjectDesc + " инициировал " + task.UserName);
            var mess = GetCurrentStatus(task);
            bool done;

            #region экспорт из Aveva

            if (task.Source == TriggerInfo.TriggerSource.Pdms)
            {
                if (task.Status == 0)
                {
                    //выгрузка из Aveva
                    if (!IsLicenseEnable(task.Source))
                    {
                        Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> отсутствует лицензия Aveva");
                        return;
                    }
                    done = StartProcess(task);
                    if (done)
                    {
                        task.Status = 2;
                        SendTriggerState(task.Id, task.Status);
                    }
                    Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> {(done ? "выполнен" : "не выполнен")}");
                }
                return;
            }

            #endregion

            if (task.Status == 0 && task.Source == TriggerInfo.TriggerSource.Tekla)
            {
                #region экспорт из Tekla               

                if (!IsLicenseEnable(task.Source))
                {
                    Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> отсутствует лицензия Tekla");
                    return;
                }

                done = StartProcess(task);
                if (done)
                {
                    task.Status = 1;
                    SendTriggerState(task.Id, task.Status);
                }
                Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> {(done ? "выполнен" : "не выполнен")}");

                #endregion
            }
            else if (task.Status == 0 && task.Source == TriggerInfo.TriggerSource.MagiCAD)
            {
                #region экспорт MagiCAD

                var modelName = new ProjectInformation(task.ProjectId).Program.Path;
                done = false;
                if (modelName.EndsWith(".dwg"))
                {
                    done = MagiCAD.Export.Run(task.Id);
                }
                else if (modelName.EndsWith(".rvt"))
                {
                    if (!IsLicenseEnable(TriggerInfo.TriggerSource.Revit))
                    {
                        Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> отсутствует лицензия Revit");
                        return;
                    }

                    done = RevitExport.Run(task.Id);
                }

                if (done)
                {
                    task.Status = 1;
                    SendTriggerState(task.Id, task.Status);
                }
                Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> {(done ? "выполнен" : "не выполнен")}");

                #endregion
            }
            else if (task.Status == 0 && task.Source == TriggerInfo.TriggerSource.Revit)
            {
                #region экспорт Revit

                if (!IsLicenseEnable(TriggerInfo.TriggerSource.Revit))
                {
                    Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> отсутствует лицензия Revit");
                    return;
                }

                done = RevitExport.Run(task.Id);
                if (done)
                {
                    task.Status = 1;
                    SendTriggerState(task.Id, task.Status);
                }
                Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> {(done ? "выполнен" : "не выполнен")}");

                #endregion
            }

            #region импорт в Aveva

            if (task.Status != 1) return;
            //импорт в Aveva
            mess = GetCurrentStatus(task);
            if (!IsLicenseEnable(TriggerInfo.TriggerSource.Pdms))
            {
                Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> отсутствует лицензия Aveva");
                return;
            }

            done = StartProcess(task);

            if (done)
            {
                task.Status = 2;
                SendTriggerState(task.Id, task.Status);
            }
            Console.WriteLine($"[{DateTime.Now.ToLocalTime()}] {task.ProjectId} -> {mess} -> {(done ? "выполнен" : "не выполнен")}");

            #endregion
        }

        /// <summary>
        /// Описание текущей стадии обмена по триггеру
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private static string GetCurrentStatus(TriggerInfo info)
        {
            var mess = "";
            if (info.Source == TriggerInfo.TriggerSource.Tekla && info.Status == 0)
            {
                mess = "экспорт модели из Tekla";
            }
            else if (info.Source == TriggerInfo.TriggerSource.Tekla && info.Status == 1)
            {
                mess = "импорт модели Tekla в Aveva";
            }
            else if (info.Source == TriggerInfo.TriggerSource.Pdms && info.Status == 0)
            {
                mess = "экспорт модели из Aveva";
            }
            else if (info.Source == TriggerInfo.TriggerSource.Pdms && info.Status == 1)
            {
                mess = "импорт модели Aveva в Tekla";
            }
            else if (info.Source == TriggerInfo.TriggerSource.MagiCAD && info.Status == 0)
            {
                mess = "экспорт модели из MagiCAD";
            }
            else if (info.Source == TriggerInfo.TriggerSource.MagiCAD && info.Status == 1)
            {
                mess = "импорт модели MagiCAD в Aveva";
            }
            else if (info.Source == TriggerInfo.TriggerSource.Revit && info.Status == 0)
            {
                mess = "экспорт модели из Revit";
            }
            else if (info.Source == TriggerInfo.TriggerSource.Revit && info.Status == 1)
            {
                mess = "импорт модели Revit в Aveva";
            }
            return mess;
        }

        /// <summary>
        /// Проверка наличия свободной лицензии программы
        /// </summary>
        /// <param name="target">программа</param>
        /// <returns>true - если есть свободная лицензия</returns>
        private static bool IsLicenseEnable(TriggerInfo.TriggerSource target)
        {
            var procInfo = new ProcessStartInfo
            {
                FileName = pathPolymetalPdmsStart,
                Arguments = target.ToString(),
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = Process.Start(procInfo);
            if (proc != null)
            {
                proc.WaitForExit();
                var result = proc.ExitCode;
                proc.Close();
                Console.WriteLine("License result {0}", result-100);
                return result > 100;
            }
            Console.WriteLine("License result proc == null");
            return false;
        }

        /// <summary>
        /// Распаковка внедренных ресурсов
        /// </summary>
        private static void Unpacking()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            pathPolymetalExchangeE3D = Path.Combine(path, "Polymetal.Exchange.E3D.exe");
            pathPolymetalExchangeTeklav20 = Path.Combine(path, "Polymetal.Exchange.Tekla.v20.exe");
            pathPolymetalExchangeTeklav2017i = Path.Combine(path, "Polymetal.Exchange.Tekla.v2017i.exe");
            pathPolymetalExchangeTeklav2018 = Path.Combine(path, "Polymetal.Exchange.Tekla.v2018.exe");
            pathPolymetalExchangeTeklav2021 = Path.Combine(path, "Polymetal.Exchange.Tekla.v2021.exe");
            pathPolymetalExchangeTeklav21 = Path.Combine(path, "Polymetal.Exchange.Tekla.v21.exe");
            pathPolymetalExchangeTeklav211 = Path.Combine(path, "Polymetal.Exchange.Tekla.v211.exe");            
            pathPolymetalPdmsStart = Path.Combine(path, "Polymetal.Pdms.Start.exe");

            //GetEmbeddedResource("Xbim.Common.dll");
            //GetEmbeddedResource("Xbim.Ifc.Extensions.dll");
            //GetEmbeddedResource("Xbim.Ifc2x3.dll");
            //GetEmbeddedResource("Xbim.IO.dll");
            //GetEmbeddedResource("Xbim.Geometry.Engine.Interop.dll");
            //GetEmbeddedResource("Xbim.Geometry.Engine64.dll");
            //GetEmbeddedResource("Esent.Interop.dll");
            //GetEmbeddedResource("log4net.dll");
        }

        /// <summary>
        /// Получение внедренного ресурса
        /// </summary>
        /// <param name="resourceName">имя внедренного ресурса</param>
        /// <returns>путь к распакованному ресурсу</returns>
        private static string GetEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var path = Path.GetDirectoryName(assembly.Location);
            path = Path.Combine(path, resourceName);
            if (File.Exists(path)) return path;
            using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.Service.ExePack." + resourceName))
            {
                var bytes = new byte[(int)stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                File.WriteAllBytes(path, bytes);
            }
            return path;
        }

        ///// <summary>
        ///// Получение текущего состояния триггеров
        ///// </summary>
        ///// <returns>список триггеров</returns>
        private static List<TriggerInfo> GetTriggerQueue()
        {
            var result = new List<TriggerInfo>();
            var checkList = new List<string>();

            if (_checkTeklaTriggers)
            {
                checkList.Add("'Tekla'");
            }

            if (_checkMagicTriggers)
            {
                checkList.Add("'MagiCAD'");
            }

            if (_checkRevitTriggers)
            {
                checkList.Add("'Revit'");
            }

            if (_checkAvevaTriggers)
            {
                checkList.Add("'Pdms'");
            }

            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE " +
                                 "Aveva_Poly_Db " +
                                 "SELECT " +
                                 "IdExchangeTrigger, " +
                                 "ExchangeTrigger.ProjectId, " +
                                 "SourceTrigger, " +
                                 "StatusTrigger, " +
                                 "NeedExchange," +
                                 "[Version], " +
                                 "ProjectDesc = RTRIM(Project) +' '+ RTRIM(MDB), " +
                                 "ShortName " +
                                 "FROM " +
                                 "ExchangeTrigger " +
                                 "JOIN ExchangeProject ON ExchangeProject.ProjectID = ExchangeTrigger.ProjectId " +
                                 "JOIN vPdmsProjects ON vPdmsProjects.IdSubObject = ExchangeProject.SubObjectID " +
                                 "JOIN [User] ON [User].UserID = ExchangeTrigger.UserId " +
                                 "WHERE " +
                                 "StatusTrigger < 2  AND SourceTrigger IN (" + string.Join(",", checkList) + ") ORDER BY IdExchangeTrigger";

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var info = new TriggerInfo();
                        info.Id = int.Parse(reader["IdExchangeTrigger"].ToString());
                        info.ProjectId = int.Parse(reader["ProjectId"].ToString());
                        Enum.TryParse(reader["SourceTrigger"].ToString(), true, out info.Source);
                        info.Status = short.Parse(reader["StatusTrigger"].ToString());
                        info.NeedExchange = bool.Parse(reader["NeedExchange"].ToString());
                        info.Version = reader["Version"].ToString();
                        info.ProjectDesc = reader["ProjectDesc"].ToString();
                        info.UserName = reader["ShortName"].ToString();
                        result.Add(info);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        /// <summary>
        /// Установка статуса по передаваемому ID триггера
        /// </summary>
        /// <param name="triggerId">ID триггера</param>
        /// <param name="state">номер статуса для установки</param>
        private static void SendTriggerState(int triggerId, short state)
        {
            // 1 - информация экспортирована из источника
            // 2 - информация импортирована в получателя
            // 3 - запись намеренно проигнорирована, в связи с ее не актуальностью или отсутствием необходимости выгрузки
            // 4 - ошибка инициализации проекта

            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "UPDATE [Aveva_Poly_Db].[dbo].[ExchangeTrigger] " +
                                 "SET [StatusTrigger] = @StatusId " +
                                 "WHERE [IdExchangeTrigger] = @RowId";

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@StatusId", state);
                    sqlCmd.Parameters.AddWithValue("@RowId", triggerId);
                    using (sqlCmd)
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                }
                sqlConnection.Close();
            }
        }

        private static bool StartProcess(TriggerInfo info)
        {
            var oldDir = Environment.CurrentDirectory;
            var proc = new Process();
            var pathPolymetalExchangeAveva = pathPolymetalExchangeE3D;

            if (info.Source == TriggerInfo.TriggerSource.Tekla)
            {
                if (info.Status == 0)
                {
                    var fileName = string.Empty;
                    switch (info.Version)
                    {
                        case "20.0":
                            fileName = pathPolymetalExchangeTeklav20;
                            break;
                        case "21.0":
                            fileName = pathPolymetalExchangeTeklav21;
                            break;
                        case "21.1":
                            fileName = pathPolymetalExchangeTeklav211;
                            break;
                        case "2017i":
                            fileName = pathPolymetalExchangeTeklav2017i;
                            break;
                        case "2018":
                            fileName = pathPolymetalExchangeTeklav2018;
                            break;
                        case "2021":
                            fileName = pathPolymetalExchangeTeklav2021;
                            break;
                    }
                    if (string.IsNullOrEmpty(fileName)) return false;
                    proc.StartInfo.FileName = fileName;
                    proc.StartInfo.Arguments = info.Id.ToString();
                }
                else if (info.Status == 1)
                {
                    proc.StartInfo.FileName = pathPolymetalExchangeAveva;
                    proc.StartInfo.Arguments = info.Id.ToString();
                }
            }
            else if (info.Source == TriggerInfo.TriggerSource.Pdms)
            {
                if (info.Status == 0)
                {
                    proc.StartInfo.FileName = pathPolymetalExchangeAveva;
                    proc.StartInfo.Arguments = info.Id.ToString();
                }
                else if (info.Status == 1)
                {
                    var fileName = string.Empty;
                    switch (info.Version)
                    {
                        case "20.0":
                            fileName = pathPolymetalExchangeTeklav20;
                            break;
                        case "21.0":
                            fileName = pathPolymetalExchangeTeklav21;
                            break;
                        case "21.1":
                            fileName = pathPolymetalExchangeTeklav211;
                            break;
                        case "2017i":
                            fileName = pathPolymetalExchangeTeklav2017i;
                            break;
                        case "2018":
                            fileName = pathPolymetalExchangeTeklav2018;
                            break;
                        case "2021":
                            fileName = pathPolymetalExchangeTeklav2021;
                            break;
                    }
                    if (string.IsNullOrEmpty(fileName)) return false;
                    proc.StartInfo.FileName = fileName;
                    proc.StartInfo.Arguments = info.Id.ToString();
                }
            }
            else if (info.Source == TriggerInfo.TriggerSource.MagiCAD && info.Status == 1)
            {
                proc.StartInfo.FileName = pathPolymetalExchangeAveva;
                proc.StartInfo.Arguments = info.Id.ToString();
            }
            else if (info.Source == TriggerInfo.TriggerSource.Revit && info.Status == 1)
            {
                proc.StartInfo.FileName = pathPolymetalExchangeAveva;
                proc.StartInfo.Arguments = info.Id.ToString();
            }
            else
            {
                return false;
            }

            proc.StartInfo.WorkingDirectory = oldDir;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;

            var pdmsResult = false;
            DataReceivedEventHandler handler = (sender, e) =>
            {
                var message = e.Data;
                if (message == null)
                {
                    Console.WriteLine(DateTime.Now + " null");
                    return;
                }
                if (message.Contains("Current progress"))
                {
                    Console.Write("\r{0}", message);
                }
                else if (message.Contains("Catalogue error") || message.Contains("MCAD-Unsupported") ||
                         message.Contains("unable to evaluate") || message.TrimEnd().EndsWith(" -") ||
                         message.Contains(".png") || message.Contains(".pmlfrm") || message.Contains(".pmlobj") ||
                         message.Contains(".pmlfnc"))
                {
                    return;
                }
                else
                {
                    Console.WriteLine(message);
                }

                if (message.Contains("E3DStandalone Result:"))
                {
                    message = message.Substring(message.IndexOf("E3DStandalone Result:") + ("E3DStandalone Result:").Length);
                    int result;
                    int.TryParse(message, out result);
                    pdmsResult = result == 1;
                    ((Process)sender).CancelOutputRead();
                }
            };

            proc.EnableRaisingEvents = true;
            proc.Start();
            proc.OutputDataReceived += handler;
            proc.BeginOutputReadLine();
            proc.WaitForExit();
            proc.CancelOutputRead();
            proc.OutputDataReceived -= handler;
            var exitCode = proc.ExitCode;

            Environment.CurrentDirectory = oldDir;

            Console.WriteLine();
            return exitCode == 1 || pdmsResult;
        }
    }

    internal class TriggerInfo
    {
        internal int Id;
        internal short Status;
        internal int ProjectId;
        internal bool NeedExchange;
        internal TriggerSource Source;
        internal string Version;
        internal string UserName;
        internal string ProjectDesc;
        public enum TriggerSource
        {
            Tekla,
            Pdms,
            MagiCAD,
            Revit
        }
    }
}
