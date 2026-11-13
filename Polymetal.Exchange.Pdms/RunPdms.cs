using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Aveva.Pdms.Database;
using Aveva.Pdms.Standalone;
using Aveva.Pdms.Utilities.CommandLine;
using Polymetal.Exchange.Utilities;
using System.Data.SqlClient;
using Polymetal.Exchange.Pdms.Properties;
using System.Text;

namespace Polymetal.Exchange.Pdms
{
    public class RunPdms
    {
        internal static ProjectInformation ProjectInfo;
        internal static Common.ExchangeType ExchangeType;
        internal static Common.Source Source;
        internal static int TriggerId;

        internal struct ElementHistory
        {
            public string ItemRef;
            public string ItemFlnm;
            public string ItemType;
            public string CreatedBy;
            public string ModifiedBy;
            public string DeletedBy;
            public DateTime Created;
            public DateTime Modified;
            public DateTime Deleted;
        }      

        static int Main(string[] args)
        {
            /* возвращаемые значение:
             * 0 - не выполнено
             * 1 - выполнено
             * 2 - не нуждается в выгрузке
             * 3 - ошибка инициализации
             */
            AppDomain.CurrentDomain.AssemblyResolve+=CurrentDomain_AssemblyResolve;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            UnpackMMServices();

            if (args.Length < 1)
            {
                args = new string[1];
                Console.WriteLine("Enter TriggerID");
                args[0] = Console.ReadLine();
            }

            if (!int.TryParse(args[0], out TriggerId))
            {
                return 3;
            }

            decimal projectId, status;
            if (!Common.GetTriggerInfoById(TriggerId, out projectId, out Source, out status)) return 3;
            ProjectInfo = new ProjectInformation(projectId);
            if (ProjectInfo.ProjectId == 0) return 3;
            if (!ProjectInfo.NeedExchange) return 2;

            if (Source == Common.Source.Pdms && status == 0)
            {
                ExchangeType = Common.ExchangeType.Export;
            }
            else if (status == 1)
            {
                ExchangeType = Common.ExchangeType.Import;
            }
            else
            {
                return 3;
            }

            var done = Standalone.Run();
            return done ? 1 : 0;
        }

        private static void UnpackMMServices()
        {
            var directory = Directory.GetParent(Process.GetCurrentProcess().MainModule.FileName);
            var files = directory.GetFiles("MMServices.*");
            if (files.Length < 2)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var path = Path.Combine(directory.FullName, "MMServices.dll");
                using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.Pdms.Resources.MMServices.dll"))
                {
                    var bytes = new byte[(int)stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    File.WriteAllBytes(path, bytes);
                }

                path = Path.Combine(directory.FullName, "MMServices.resources");
                using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.Pdms.Resources.MMServices.resources"))
                {
                    var bytes = new byte[(int)stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    File.WriteAllBytes(path, bytes);
                }
            }
        }

        static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
            AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var dllName = args.Name.Split(' ').First().Replace(",", ".dll");
            var probingPath = Environment.GetEnvironmentVariable("pdms_installed_dir");
            var files = Directory.Exists(probingPath) ? Directory.GetFiles(probingPath, dllName) : new string[0];
            if (files.Length > 0)
            {
                return Assembly.LoadFile(files.First());
            }

            probingPath = Path.Combine(probingPath, "PolyDLL");
            files = Directory.Exists(probingPath) ? Directory.GetFiles(probingPath, dllName) : new string[0];
            if (files.Length > 0)
            {
                return Assembly.LoadFile(files.First());
            }
            return null;
        }

        internal static void SendMessage(string message)
        {
            Common.SendMessage(Common.Source.Pdms, TriggerId, message);
        }

        internal static void UpdateHistory(List<ElementHistory> histories, Common.Source source)
        {
            if (ProjectInfo.Pdms.Project.Equals("TRA") || histories.Count == 0) return;
            var documentMark = source == Common.Source.Tekla ? 41 : (source == Common.Source.MagiCAD) ? 59 : 37;
            var projectCode = GetHistoryProjectCode();
            var users = Common.GetAllUsers();
            int userId;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var i = 0.0;
                var oldPercent = 0.0;
                foreach (var hist in histories)
                {
                    i++;
                    var percent = Math.Ceiling(i * 100 / histories.Count);
                    if (oldPercent < percent)
                    {
                        Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                        oldPercent = percent;
                    }
                    string sqlCommand;
                    if (!string.IsNullOrEmpty(hist.CreatedBy) && users.TryGetValue(hist.CreatedBy.ToLower(), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * " +
                                     "   FROM " +
                                     "       LogUserSession " +
                                     "   WHERE " +
                                     "       LogUserSessionRefEle = '" + hist.ItemRef +
                                     "' " +
                                     "       AND LogUserSessionType = 'Create' AND LogUserSessionProjectCode = '" + projectCode + "')" +
                                     "   ) " +
                                     "   BEGIN " +
                                     "       RETURN" +
                                     "   END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogUserSession " +
                                     "       (" +
                                     "           LogUserSessionProjectCode, " +
                                     "           UserID, " +
                                     "           LogUserSessionDateTime, " +
                                     "           LogUserSessionRefEle, " +
                                     "           LogUserSessionFullNameEle, " +
                                     "           LogUserSessionType, " +
                                     "           LogUserSessionTypeEle, " +
                                     "           ModuleID, " +
                                     "           LogUserSessionProp, " +
                                     "           DocumentMarkID, " +
                                     "           LogUserSessionLastEditByUser " +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           '" + projectCode + "', " +
                                     "           " + userId + ", " +
                                     "           '" + hist.Created + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemFlnm + "', " +
                                     "           'Create', " +
                                     "           '" + hist.ItemType + "', " +
                                     "           1, " +
                                     "           'JOB', " +
                                     "           " +documentMark+ "," +
                                     "           64 "+
                                     "       ) END";
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }

                    if (!string.IsNullOrEmpty(hist.ModifiedBy) && users.TryGetValue(hist.ModifiedBy.ToLower(), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * " +
                                     "   FROM " +
                                     "       LogUserSession " +
                                     "   WHERE " +
                                     "       LogUserSessionRefEle = '" + hist.ItemRef +
                                     "' " +
                                     "       AND LogUserSessionType = 'Modify' " +
                                     "       AND LogUserSessionDateTime = '" + hist.Modified + "' AND LogUserSessionProjectCode = '" + projectCode + "')" +
                                     "   ) " +
                                     "   BEGIN  " +
                                     "      RETURN " +
                                     "   END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogUserSession " +
                                     "       (" +
                                     "           LogUserSessionProjectCode, " +
                                     "           UserID, " +
                                     "           LogUserSessionDateTime, " +
                                     "           LogUserSessionRefEle, " +
                                     "           LogUserSessionFullNameEle, " +
                                     "           LogUserSessionType, " +
                                     "           LogUserSessionTypeEle, " +
                                     "           ModuleID, " +
                                     "           LogUserSessionProp, " +
                                     "           DocumentMarkID, " +
                                     "           LogUserSessionLastEditByUser " +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           '" + projectCode + "', " +
                                     "           " + userId + ", " +
                                     "           '" + hist.Modified + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemFlnm + "', " +
                                     "           'Modify', " +
                                     "           '" + hist.ItemType + "', " +
                                     "           1, " +
                                     "           'JOB'," +
                                     "           " + documentMark + "," +
                                     "           64 " +
                                     "       ) END";
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }

                    if (!string.IsNullOrEmpty(hist.DeletedBy) && users.TryGetValue(hist.DeletedBy.ToLower(), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * " +
                                     "   FROM " +
                                     "       LogUserSession " +
                                     "   WHERE " +
                                     "       LogUserSessionRefEle = '" + hist.ItemRef +
                                     "' " +
                                     "       AND LogUserSessionType = 'Delete' " +
                                     "       AND LogUserSessionDateTime = '" + hist.Deleted + "' AND LogUserSessionProjectCode = '" + projectCode + "')" +
                                     "   ) " +
                                     "   BEGIN  " +
                                     "      RETURN " +
                                     "   END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogUserSession " +
                                     "       (" +
                                     "           LogUserSessionProjectCode, " +
                                     "           UserID, " +
                                     "           LogUserSessionDateTime, " +
                                     "           LogUserSessionRefEle, " +
                                     "           LogUserSessionFullNameEle, " +
                                     "           LogUserSessionType, " +
                                     "           LogUserSessionTypeEle, " +
                                     "           ModuleID, " +
                                     "           LogUserSessionProp, " +
                                     "           DocumentMarkID, " +
                                     "           LogUserSessionLastEditByUser " +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           '" + projectCode + "', " +
                                     "           " + userId + ", " +
                                     "           '" + hist.Deleted + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemFlnm + "', " +
                                     "           'Delete', " +
                                     "           '" + hist.ItemType + "', " +
                                     "           1, " +
                                     "           'JOB'," +
                                     "           " + documentMark + "," +
                                     "           64 " +
                                     "       ) END";
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }
                }
                Console.WriteLine("Current progress {0}%   ", 100);
                sqlConnection.Close();
            }
        }

        private static string GetHistoryProjectCode()
        {
            var result = string.Empty;
            var sqlCommand = "USE Aveva_Poly_Db " +
                             "SELECT (CompanyCode+AreaCode+ObjectCode+SubObjectCode) " +
                             "FROM vPdmsProjects " +
                             "JOIN SubObject ON SubObject.SubObjectID = vPdmsProjects.IdSubObject " +
                             "JOIN [Object] ON [Object].ObjectID = vPdmsProjects.IdObject " +
                             "JOIN Area ON Area.AreaID = vPdmsProjects.IdArea " +
                             "JOIN Company ON Company.CompanyID = vPdmsProjects.IdCompany " +
                             "WHERE Project = '" + ProjectInfo.Pdms.Project + "' AND MDB = '" +
                             ProjectInfo.Pdms.Mdb + "'";
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null)
                    {
                        result = res.ToString();
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }

        internal static string GetUserByTriggerId()
        {
            var result = string.Empty;
            var sqlCommand = "USE Aveva_Poly_Db " +
                             "SELECT RTRIM(UserLogin) " +
                             "FROM ExchangeTrigger " +
                             "JOIN [User] ON [User].UserID = ExchangeTrigger.UserId " +
                             "WHERE IdExchangeTrigger = " + TriggerId;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null)
                    {
                        result = res.ToString();
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }
    }

    class Standalone
    {
        internal static bool Run()
        {
            if (RunPdms.ProjectInfo.Program.TypeSource == Common.Source.Tekla &&
                RunPdms.ExchangeType == Common.ExchangeType.Export)
            {
                var startMacro = Path.GetTempFileName();
                var libraryPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Polymetal.Exchange.Pdms.Library.dll");
                if (!File.Exists(libraryPath))
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.Pdms.Resources.Polymetal.Exchange.Pdms.Library.dll"))
                    {
                        var bytes = new byte[(int)stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                        File.WriteAllBytes(libraryPath, bytes);
                    }
                }
                using (var sw = new StreamWriter(startMacro))
                {
                    sw.WriteLine("$m \"%PDMSUI%/CLIB/FILES/FLOAD\"");
                    sw.WriteLine();
                    sw.WriteLine("IMPORT '{0}'", Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Polymetal.Exchange.Pdms.Library"));
                    sw.WriteLine("handle any");
                    sw.WriteLine("endhandle");
                    sw.WriteLine("using namespace 'Polymetal.Exchange.Pdms.Library'");
                    sw.WriteLine("!exportobject = object ExportingClass()");
                    sw.WriteLine("!exportobject.ExportFromDesign()");
                    sw.Close();
                }

                Environment.SetEnvironmentVariable("PDMSENTRYMACRO", @"$M '" + startMacro + "'");
                Environment.SetEnvironmentVariable("PDMSEXPORTTRIGGER", RunPdms.TriggerId.ToString());
                //выгрузка stp из под Standalone косячная, поэтому запускаю .bat
                var proc = new Process();
                proc.StartInfo.FileName = @"C:\AVEVA\Plant\PDMS12.1.SP4\des.exe";
                proc.StartInfo.Arguments = @"-proj=" + RunPdms.ProjectInfo.Pdms.Project + " -username=" +
                                           RunPdms.ProjectInfo.Pdms.UserName + " -pass=" +
                                           RunPdms.ProjectInfo.Pdms.Password + " -mdb=/" + RunPdms.ProjectInfo.Pdms.Mdb;
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;

                proc.EnableRaisingEvents = true;
                proc.Start();
                proc.WaitForExit();
                try
                {
                    File.Delete(startMacro);
                }
                catch
                {
                }
                return true;
            }

            var done = false;
            if (!Start())
            {
                return done;
            }
            if (!OpenProject())
            {
                Close(done);
                return done;
            }

            if (RunPdms.ProjectInfo.Program.TypeSource == Common.Source.Tekla)
            {
                switch (RunPdms.ExchangeType)
                {
                    case Common.ExchangeType.Import:
                        done = TeklaImport.Run();
                        done = SaveProject() & done;
                        break;
                    case Common.ExchangeType.Export:
                        done = Export.Run();
                        break;
                }
            }
            else if (RunPdms.ProjectInfo.Program.TypeSource == Common.Source.MagiCAD)
            {
                switch (RunPdms.ExchangeType)
                {
                    case Common.ExchangeType.Import:
                        done = MagicImport.Run();
                        done = SaveProject() & done;
                        break;
                    case Common.ExchangeType.Export:
                        done = Export.Run();
                        break;
                }
            }
            else if (RunPdms.ProjectInfo.Program.TypeSource == Common.Source.Revit)
            {
                switch (RunPdms.ExchangeType)
                {
                    case Common.ExchangeType.Import:
                        done = RevitImport.Run();
                        done = SaveProject() & done;
                        break;
                    case Common.ExchangeType.Export:
                        done = Export.Run();
                        break;
                }
            }
            done = Close(done) & done;
            return done;
        }

        internal static bool Start()
        {
            //Environment.CurrentDirectory = Environment.GetEnvironmentVariable("pdms_installed_dir");
            bool result;
            Console.WriteLine("Запускаем PdmsStandalone");
            try
            {
                result = PdmsStandalone.Start();
                if (!result)
                {
                    PdmsStandalone.ExitError("Failed to initialise Standalone");
                }
            }
            catch (Exception exc)
            {
                RunPdms.SendMessage("[PdmsStandalone] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "PdmsStandalone успешно запущен" : "Не удалось запустить PdmsStandalone");

            return result;
        }

        internal static bool Close(bool done)
        {
            bool result;
            Console.WriteLine("Закрываем PdmsStandalone");
            try
            {
                PdmsStandalone.Close();
                result = true;
                Console.WriteLine("Pdms Standalone Result: " + (done ? 1 : 0));
                PdmsStandalone.Finish();
            }
            catch (Exception exc)
            {
                RunPdms.SendMessage("[StandaloneClose] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "PdmsStandalone закрыт" : "Не удалось закрыть PdmsStandalone");

            return result;
        }

        internal static bool OpenProject()
        {
            bool result;
            Console.WriteLine("Открываем проект " + RunPdms.ProjectInfo.Pdms.Project + " / " + RunPdms.ProjectInfo.Pdms.Mdb);
            try
            {
                result = PdmsStandalone.Open(RunPdms.ProjectInfo.Pdms.Project, RunPdms.ProjectInfo.Pdms.UserName,
                    RunPdms.ProjectInfo.Pdms.Password, RunPdms.ProjectInfo.Pdms.Mdb);
            }
            catch (Exception exc)
            {
                RunPdms.SendMessage("[OpenProject] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "Проект успешно открыт" : "Не удалось открыть проект");

            return result;
        }

        internal static bool SaveProject()
        {
            var result = true;
            Console.WriteLine("Сохраняем проект");
            try
            {
                result = MDB.CurrentMDB.SaveWork("Import from "+RunPdms.Source);
            }
            catch (Exception exc)
            {
                RunPdms.SendMessage("[SaveProject] " + exc.Message);
            }

            Console.WriteLine("Проект " + (result ? "сохранен" : "не сохранен"));
            Command.CreateCommand("unclaim all").Run();
            return result;
        }

        internal static bool CloseProject()
        {
            bool result;
            Console.WriteLine("Закрываем проект");
            try
            {
                PdmsStandalone.Close();
                result = true;
            }
            catch (Exception exc)
            {
                RunPdms.SendMessage("[CloseProject] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "Проект закрыт" : "Не удалось закрыть проект");
            return result;
        }
    }
}
