using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Aveva.Core.Database;
using Aveva.Core.Utilities.CommandLine;
using Polymetal.Exchange.Utilities;
using System.Data.SqlClient;
using Aveva.Core.Utilities.Messaging;

namespace Polymetal.Exchange.E3D
{
    public class RunE3D
    {
        internal static ProjectInformation ProjectInfo;
        internal static Common.ExchangeType ExchangeType;
        internal static Common.Source Source;
        internal static int TriggerId;                    

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

            //копирование обновленных библиотек при каждом старте Aveva
            ExecuteCommand("xcopy \"%aveva_server_dir%\\PMLLIB\\dotnet\\dll\" \"%aveva_design_installed_dir%PolyDLL\" /I /C /Y /Z /D");

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

        static void ExecuteCommand(string command)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            process.WaitForExit();
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Console.WriteLine(output);
            Console.WriteLine(error);

            process.Close();
        }

        private static void UnpackMMServices()
        {
            var directory = Directory.GetParent(Process.GetCurrentProcess().MainModule.FileName);
            var files = directory.GetFiles("MMServices.*");
            if (files.Length < 2)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var path = Path.Combine(directory.FullName, "MMServices.dll");
                using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.E3D.Resources.MMServices.dll"))
                {
                    var bytes = new byte[(int)stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    File.WriteAllBytes(path, bytes);
                }

                path = Path.Combine(directory.FullName, "MMServices.resources");
                using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.E3D.Resources.MMServices.resources"))
                {
                    var bytes = new byte[(int)stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    File.WriteAllBytes(path, bytes);
                }
            }

            var dllNames = new[] { "Aveva.Core3D.Graphics.dll", "Aveva.Core.Commands.dll" };
            //var avevaInstalledDir = Environment.GetEnvironmentVariable("AVEVA_DESIGN_INSTALLED_DIR");
            //if (!Directory.Exists(avevaInstalledDir)) return;

            foreach (var dllName in dllNames)
            {
                //var dllFile = Directory.GetFiles(avevaInstalledDir, dllName).FirstOrDefault();
                //if (!File.Exists(dllFile)) continue;
                Assembly.Load(dllName);
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
            var probingPath = Environment.GetEnvironmentVariable("AVEVA_DESIGN_INSTALLED_DIR");
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
                    if (!string.IsNullOrEmpty(hist.CreatedBy) && users.TryGetValue(hist.CreatedBy.ToLower().Replace("poly\\",string.Empty), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * FROM  LogE3D_Additional " +
                                     "   WHERE " +
                                     $"       RefElement = '{hist.ItemRef}' " +
                                     $"       AND ActionType = 0 AND DbName = '{hist.ItemDbna}')" +
                                     "   ) " +
                                     "   BEGIN RETURN END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogE3D_Additional " +
                                     "       (" +
                                     "           SubObjId, " +
                                     "           UserId, " +
                                     "           ElementTypeId, " +
                                     "           DateTimeAction, " +
                                     "           RefElement, " +
                                     "           RefOwnerElement, " +
                                     "           ModuleId, " +
                                     "           ActionType, " +
                                     "           DbName, " +
                                     "           AttrValues, " +
                                     "           RootZoneName, " +
                                     "           SitePurp, " +
                                     "           isManualInput, " +
                                     "           Category" +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           " + ProjectInfo.Pdms.SubObjectId + ", " +
                                     "           " + userId + ", " +
                                     "           '" + hist.ItemType + "', " +
                                     "           '" + hist.Created + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemOwnerRef + "', " +
                                     "           1, " +
                                     "           0, " +
                                     "           '" + hist.ItemDbna + "', " +
                                     "           '" + hist.ItemAttributes + "', " +
                                     "           '" + hist.ItemRootZoneName + "'," +
                                     "           '"+ hist.ItemSitePurp +"', " +
                                     "           1," +
                                     $"           '{hist.Category}'" +
                                     "       ) END";
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }

                    if (!string.IsNullOrEmpty(hist.ModifiedBy) && (hist.Modified != hist.Created) && users.TryGetValue(hist.ModifiedBy.ToLower().Replace("poly\\", string.Empty), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * FROM  LogE3D_Additional " +
                                     "   WHERE " +
                                     $"       RefElement = '{hist.ItemRef}' " +
                                     $"       AND ActionType = 1 AND DbName = '{hist.ItemDbna}' AND DateTimeAction = '{hist.Modified}')" +
                                     "   ) " +
                                     "   BEGIN RETURN END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogE3D_Additional " +
                                     "       (" +
                                     "           SubObjId, " +
                                     "           UserId, " +
                                     "           ElementTypeId, " +
                                     "           DateTimeAction, " +
                                     "           RefElement, " +
                                     "           RefOwnerElement, " +
                                     "           ModuleId, " +
                                     "           ActionType, " +
                                     "           DbName, " +
                                     "           AttrValues, " +
                                     "           RootZoneName, " +
                                     "           SitePurp, " +
                                     "           isManualInput, " +
                                     "           Category" +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           " + ProjectInfo.Pdms.SubObjectId + ", " +
                                     "           " + userId + ", " +
                                     "           '" + hist.ItemType + "', " +
                                     "           '" + hist.Modified + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemOwnerRef + "', " +
                                     "           1, " +
                                     "           1, " +
                                     "           '" + hist.ItemDbna + "', " +
                                     "           '" + hist.ItemAttributes + "', " +
                                     "           '" + hist.ItemRootZoneName + "'," +
                                     "           '" + hist.ItemSitePurp + "', " +
                                     "           1, " +
                                     $"          '{hist.Category}'" +
                                     "       ) END";
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }

                    if (!string.IsNullOrEmpty(hist.DeletedBy) && users.TryGetValue(hist.DeletedBy.ToLower().Replace("poly\\", string.Empty), out userId))
                    {
                        sqlCommand = "USE Aveva_Poly_Db " +
                                     "IF " +
                                     "(EXISTS " +
                                     "   (SELECT * FROM  LogE3D_Additional " +
                                     "   WHERE " +
                                     $"       RefElement = '{hist.ItemRef}' " +
                                     $"       AND ActionType = 2 AND DbName = '{hist.ItemDbna}' AND DateTimeAction = '{hist.Deleted}')" +
                                     "   ) " +
                                     "   BEGIN RETURN END " +
                                     "ELSE " +
                                     "   BEGIN " +
                                     "       INSERT INTO LogE3D_Additional " +
                                     "       (" +
                                     "           SubObjId, " +
                                     "           UserId, " +
                                     "           ElementTypeId, " +
                                     "           DateTimeAction, " +
                                     "           RefElement, " +
                                     "           RefOwnerElement, " +
                                     "           ModuleId, " +
                                     "           ActionType, " +
                                     "           DbName, " +
                                     "           AttrValues, " +
                                     "           RootZoneName, " +
                                     "           SitePurp, " +
                                     "           isManualInput, " +
                                     "           Category" +
                                     "       ) " +
                                     "       VALUES " +
                                     "       (" +
                                     "           " + ProjectInfo.Pdms.SubObjectId + ", " +
                                     "           " + userId + ", " +
                                     "           '" + hist.ItemType + "', " +
                                     "           '" + hist.Deleted + "', " +
                                     "           '" + hist.ItemRef + "', " +
                                     "           '" + hist.ItemOwnerRef + "', " +
                                     "           1, " +
                                     "           2, " +
                                     "           '" + hist.ItemDbna + "', " +
                                     "           '" + hist.ItemAttributes + "', " +
                                     "           '" + hist.ItemRootZoneName + "'," +
                                     "           '" + hist.ItemSitePurp + "', " +
                                     "           1," +
                                     $"          '{hist.Category}'" +
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
            if (RunE3D.ExchangeType == Common.ExchangeType.Export)
            {
                //выгрузка stp, ifc из под Standalone косячная, поэтому запускаю .bat
                var startMacro = Path.GetTempFileName();
                var libraryPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Polymetal.Exchange.E3D.Library.dll");
                if (!File.Exists(libraryPath))
                {
                    return false;
                }
                using (var sw = new StreamWriter(startMacro))
                {
                    sw.WriteLine("$m \"%PMLLIB%/dotnet/runmac/sharp_pseudoattribute_start.pmlmac\"");
                    sw.WriteLine();
                    sw.WriteLine($"IMPORT '{libraryPath.Replace(".dll", string.Empty)}'");
                    sw.WriteLine("handle any");
                    sw.WriteLine("endhandle");
                    sw.WriteLine("using namespace 'Polymetal.Exchange.E3D.Library'");
                    sw.WriteLine("!exportobject = object ExportingClass()");
                    sw.WriteLine($"!exportobject.ExportFromDesign({RunE3D.TriggerId})");
                    sw.Close();
                }

                var proc = new Process();
                proc.StartInfo.FileName = Path.Combine(Environment.GetEnvironmentVariable("aveva_design_installed_dir"), "des");
                proc.StartInfo.Arguments = $"tty {RunE3D.ProjectInfo.Pdms.Project} {RunE3D.ProjectInfo.Pdms.UserName}/{RunE3D.ProjectInfo.Pdms.Password} /{RunE3D.ProjectInfo.Pdms.Mdb}  $m/{startMacro}";
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
                Console.WriteLine("E3DStandalone Result: 1");
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

            if (RunE3D.ProjectInfo.Program.TypeSource == Common.Source.Tekla)
            {
                switch (RunE3D.ExchangeType)
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
            else if (RunE3D.ProjectInfo.Program.TypeSource == Common.Source.MagiCAD)
            {
                switch (RunE3D.ExchangeType)
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
            else if (RunE3D.ProjectInfo.Program.TypeSource == Common.Source.Revit)
            {
                switch (RunE3D.ExchangeType)
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
            Console.WriteLine("Запускаем E3DStandalone");
            try
            {
                result = Aveva.E3D.Standalone.Standalone.Start();
                if (!result)
                {
                    Aveva.E3D.Standalone.Standalone.ExitError("Failed to initialise Standalone");
                }
            }
            catch (Exception exc)
            {
                RunE3D.SendMessage("[E3DStandalone] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "E3DStandalone успешно запущен" : "Не удалось запустить E3DStandalone");

            return result;
        }

        internal static bool Close(bool done)
        {
            bool result;
            Console.WriteLine("Закрываем E3DStandalone");
            try
            {
                MDB.CurrentMDB.CloseMDB();
                Project.CurrentProject.Close();
                //Aveva.E3D.Standalone.Standalone.Close();
                result = true;
                Console.WriteLine("E3DStandalone Result: " + (done ? 1 : 0));                
                Aveva.E3D.Standalone.Standalone.Finish();
            }
            catch (Exception exc)
            {
                RunE3D.SendMessage("[E3DStandalone Close] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "E3DStandalone закрыт" : "Не удалось закрыть E3DStandalone");

            return result;
        }

        internal static bool OpenProject()
        {
            bool result;
            Console.WriteLine("Открываем проект " + RunE3D.ProjectInfo.Pdms.Project + " / " + RunE3D.ProjectInfo.Pdms.Mdb);
            try
            {
                result = Aveva.E3D.Standalone.Standalone.Open(RunE3D.ProjectInfo.Pdms.Project, RunE3D.ProjectInfo.Pdms.UserName,
                    RunE3D.ProjectInfo.Pdms.Password, RunE3D.ProjectInfo.Pdms.Mdb, out PdmsMessage mess);
            }
            catch (Exception exc)
            {
                RunE3D.SendMessage("[OpenProject] " + exc.Message);
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
                result = MDB.CurrentMDB.SaveWork("Import from "+RunE3D.Source);
            }
            catch (Exception exc)
            {
                RunE3D.SendMessage("[SaveProject] " + exc.Message);
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
                MDB.CurrentMDB.CloseMDB();
                Project.CurrentProject.Close();
                //Aveva.E3D.Standalone.Standalone.Close();
                result = true;
            }
            catch (Exception exc)
            {
                RunE3D.SendMessage("[CloseProject] " + exc.Message);
                result = false;
            }

            Console.WriteLine(result ? "Проект закрыт" : "Не удалось закрыть проект");
            return result;
        }
    }        
}
