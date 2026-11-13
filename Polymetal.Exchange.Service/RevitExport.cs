using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Polymetal.Exchange.Geometry;
using Polymetal.Exchange.Utilities;

namespace Polymetal.Exchange.Service
{
    class RevitExport
    {
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc func, IntPtr lParam);

        private static string _ifcPath;
        private static XmlSerializer _xmlFormat = new XmlSerializer(typeof(Utilities.DTO.IfcElementInformation));
        private static XmlSerializerNamespaces _xmlSerializerNamespaces = new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty });
        private static string _revitTempPath;

        public static bool Run(int triggerId)
        {
            _ifcPath = @"C:\Aveva\temp\";
            var swa = new Stopwatch();
            swa.Start();
            decimal projectId, status;
            Common.Source source;
            _revitTempPath = string.Empty;
            if (!Common.GetTriggerInfoById(triggerId, out projectId, out source, out status)) 
                return false;

            var projInfo = new ProjectInformation(projectId);

            var revitPath = string.Format(@"C:\Program Files\Autodesk\Revit {0}\Revit.exe", projInfo.Program.Version);
            var modelName = projInfo.Program.Path;

            var file = new FileInfo(modelName);
            if (!file.Exists || !File.Exists(revitPath)) 
                return false;

            //var tempFile = new FileInfo(@"c:\");
            //var tryCounter = 0;
            //while (tryCounter < 10)
            //{
            //    try
            //    {
            //        tempFile = file.CopyTo(Path.Combine(file.DirectoryName, "tmp.rvt"), true);
            //        break;
            //    }
            //    catch
            //    {
            //        tryCounter++;
            //        Console.WriteLine("attempt to copy a file, try number {0}", tryCounter);
            //    }
            //}

            //if (!tempFile.Exists) return false;

            _ifcPath = Path.Combine(_ifcPath, projInfo.Program.TypeSource.ToString());
            if (!Directory.Exists(_ifcPath)) 
                Directory.CreateDirectory(_ifcPath);

            var ifcFile = new FileInfo(Path.Combine(_ifcPath, projectId + ".ifc"));
            if (ifcFile.Exists) 
                ifcFile.Delete();

            Console.WriteLine("{0}: Запуск Revit", DateTime.Now.ToLongTimeString());

            var oldDir = Environment.CurrentDirectory;
            Environment.SetEnvironmentVariable("PLMT_EXCHANGE",
                string.Format($"" +
                $"{modelName}#" +
                $"export#" +
                $"{Path.Combine(_ifcPath, projectId + ".ifc")}#" +
                $"{projInfo.Program.TypeSource.ToString()}#" +
                $"{GetSubObjectId(projectId)}"));

            var checkPluginLoadTimer = new Timer(LoadRevitPlugins, null, 10000, 10000);

            var procTimer = new Stopwatch();
            procTimer.Start();

            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo(revitPath, "/language RUS");
            proc.StartInfo.WorkingDirectory = oldDir;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += Proc_OutputDataReceived;
            proc.Start();
            proc.BeginOutputReadLine();

            while (procTimer.Elapsed.TotalMinutes < 90 && Process.GetProcesses().Any(x => x.Id == proc.Id))
            {
                Thread.Sleep(5000);
            }
            //proc.WaitForExit(10000000);
            if (!proc.HasExited) 
                proc.Kill();

            checkPluginLoadTimer.Dispose();

            if (!string.IsNullOrEmpty(_revitTempPath) && Directory.Exists(_revitTempPath) &&
                !Path.GetTempPath().Equals(_revitTempPath, StringComparison.OrdinalIgnoreCase))
            {
                var revitTempPathInfo = new DirectoryInfo(_revitTempPath);
                try
                {
                    revitTempPathInfo.Delete(true);
                }
                catch
                {
                }
            }

            swa.Stop();

            Console.WriteLine($"Экспорт выполнен за {Math.Floor(swa.Elapsed.TotalMinutes)}:{swa.Elapsed.Seconds}");

            ifcFile = new FileInfo(Path.Combine(_ifcPath, projectId + ".ifc"));
            if (!ifcFile.Exists) 
                return false;

            if (ifcFile.Length == 0) 
                return true;

            bool result;
            if (projInfo.Program.TypeSource == Common.Source.Revit)
            {
                result = DoArchitecture(ifcFile, projInfo, projectId, triggerId);
            }
            else
            {
                result = DoDucting(ifcFile, projInfo, projectId, triggerId);
            }

            Console.WriteLine(result ? "Данные обновлены" : "Данные не обновлены");

            var tmpFiles = Directory.GetFiles(_ifcPath);
            foreach (var f in tmpFiles)
            {
                try
                {
                    File.Delete(f);
                }
                catch { }
            }

            return result;
        }

        private static void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e?.Data == null || !e.Data.StartsWith("RVT_TEMP_PATH"))
                return;

            _revitTempPath = e.Data.Substring(e.Data.IndexOf(' ')).Trim();
        }

        private static bool DoDucting(FileInfo ifcFile, ProjectInformation projInfo, decimal projectId, int triggerId)
        {
            var elementInformationList =
                RevitIfcParser.Run(ifcFile.FullName, projInfo.Program.TypeSource, projInfo.Transformation);

            ifcFile.Delete();

            if (elementInformationList == null)
                return false;

            if (elementInformationList.Count == 0)
                return true;

            var usersDict = Common.GetAllUsers();

            var create = Common.CreateMagiCadTable();
            var modify = Common.CreateMagiCadTable();
            var delete = Common.CreateMagiCadTable();

            var existingTable = GetExistingItemsFromMagiCadSQL(projectId);

            var existingDictionary = new Dictionary<string, string[]>();

            foreach (DataRow row in existingTable.Rows)
            {
                existingDictionary.Add((string)row["element_guid"],
                    new[] { (string)row["element_type"], (string)row["element_info"] });
            }

            var doneIdDictionary = new Dictionary<string, bool>();
            foreach (var elementInformation in elementInformationList)
            {
                var uniqueId = elementInformation.UniqueId;
                if (string.IsNullOrEmpty(uniqueId) || doneIdDictionary.ContainsKey(uniqueId))
                {
                    Console.WriteLine("doneIdDictionary contains key\t{0}", uniqueId);
                    continue;
                }

                string serializedString;
                using (var stream = new StringWriter())
                {
                    using (var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings { Indent = false }))
                    {
                        elementInformation.Id = string.Empty;
                        _xmlFormat.Serialize(xmlWriter, elementInformation, _xmlSerializerNamespaces);
                        serializedString = stream.ToString();
                    }
                }

                if (existingDictionary.ContainsKey(uniqueId))
                {
                    var row = modify.NewRow();
                    row["element_guid"] = uniqueId;
                    row["element_type"] = elementInformation.Type;
                    row["element_info"] = serializedString;

                    var createdByProperty = elementInformation.Properties?.FirstOrDefault(x => x.Name == "CreatedBy");
                    var modifiedByProperty = elementInformation.Properties?.FirstOrDefault(x => x.Name == "ModifiedBy");

                    if (createdByProperty != null && usersDict.ContainsKey(createdByProperty.Value))
                        row["element_created_by"] = usersDict[createdByProperty.Value].ToString();

                    if (modifiedByProperty != null && usersDict.ContainsKey(modifiedByProperty.Value))
                        row["element_modified_by"] = usersDict[modifiedByProperty.Value].ToString();

                    modify.Rows.Add(row);
                }
                else
                {
                    var row = create.NewRow();
                    row["element_guid"] = uniqueId;
                    row["element_type"] = elementInformation.Type;
                    row["element_info"] = serializedString;

                    var createdByProperty = elementInformation.Properties?.FirstOrDefault(x => x.Name == "CreatedBy");
                    var modifiedByProperty = elementInformation.Properties?.FirstOrDefault(x => x.Name == "ModifiedBy");

                    if (createdByProperty != null && usersDict.ContainsKey(createdByProperty.Value))
                        row["element_created_by"] = usersDict[createdByProperty.Value].ToString();

                    if (modifiedByProperty != null && usersDict.ContainsKey(modifiedByProperty.Value))
                        row["element_modified_by"] = usersDict[modifiedByProperty.Value].ToString();

                    create.Rows.Add(row);
                }

                doneIdDictionary.Add(uniqueId, false);
            }

            foreach (var uniqueId in existingDictionary.Keys)
            {
                if (doneIdDictionary.ContainsKey(uniqueId))
                    continue;

                var row = delete.NewRow();
                row["element_guid"] = uniqueId;
                delete.Rows.Add(row);
            }

            Console.WriteLine("Обновляем данные в SQL");

            return Common.UpdateMagiCadData(create, modify, delete, projectId, triggerId);
        }

        private static bool DoArchitecture(FileInfo ifcFile, ProjectInformation projInfo, decimal projectId, int triggerId)
        {
            DataTable dt, allGuidDt;
            var elementInformationList = RevitIfcParser.Run(ifcFile.FullName, projInfo.Program.TypeSource, projInfo.Transformation);

            ifcFile.Delete();

            if (elementInformationList.Count == 0) 
                return true;

            dt = Common.CreateRevitTable();
            allGuidDt = Common.CreateRevitTable();
            foreach (var elementInformation in elementInformationList)
            {
                var row = dt.NewRow();
                row["element_guid"] = elementInformation.UniqueId;
                row["element_type"] = elementInformation.Type;

                var guidRow = allGuidDt.NewRow();
                guidRow["element_guid"] = elementInformation.UniqueId;

                using (var stream = new StringWriter())
                {
                    using (var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true}))
                    {
                        elementInformation.Id = string.Empty;
                        _xmlFormat.Serialize(xmlWriter, elementInformation, _xmlSerializerNamespaces);
                        row["element_info"] = stream.ToString();
                    }
                }

                dt.Rows.Add(row);
                allGuidDt.Rows.Add(guidRow);
            }

            Console.WriteLine("Обновляем данные в SQL");

            return Common.UpdateSql(dt, allGuidDt, projInfo.Program.TypeSource, projectId, triggerId);
        }

        private static decimal GetSubObjectId(decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State != System.Data.ConnectionState.Open) return 0;

                var sqlCommand = string.Format($"select SubObjectID from ExchangeProject where ProjectID = {projectId}");
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var subObjectId = sqlCmd.ExecuteScalar();
                    return subObjectId == null ? 0 : (decimal)subObjectId;
                }
            }
        }

        private static DataTable GetExistingItemsFromMagiCadSQL(decimal projectId)
        {
            var dt = new DataTable();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = $"select element_guid, element_type, element_info from MagiCad where project_id = {projectId}";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    using (var sqlAdapter = new SqlDataAdapter(sqlCmd))
                    {
                        sqlAdapter.Fill(dt);
                    }
                }
                sqlConnection.Close();
            }
            return dt;
        }

        public static string CalculateMD5Hash(string input)
        {
            var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);

            var hash = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("X2"));
            }

            return sb.ToString();

        }

        private static string GetWindowText(IntPtr hWnd)
        {
            var len = GetWindowTextLength(hWnd) + 1;
            var sb = new StringBuilder(len);
            len = GetWindowText(hWnd, sb, len);
            return sb.ToString(0, len);
        }

        private static bool EnumWindow(IntPtr hWnd, IntPtr lParam)
        {
            GCHandle gcChildhandlesList = GCHandle.FromIntPtr(lParam);

            if (gcChildhandlesList == null || gcChildhandlesList.Target == null)
            {
                return false;
            }

            List<IntPtr> childHandles = gcChildhandlesList.Target as List<IntPtr>;
            childHandles.Add(hWnd);

            return true;
        }

        private static void LoadRevitPlugins(object obj)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowTextLength(hWnd) == 0) return true;

                if (GetWindowText(hWnd) == "Безопасность — Надстройка без подписи")
                {
                    var childHandles = new List<IntPtr>();
                    GCHandle gcChildhandlesList = GCHandle.Alloc(childHandles);
                    IntPtr pointerChildHandlesList = GCHandle.ToIntPtr(gcChildhandlesList);

                    try
                    {
                        EnumChildWindows(hWnd, EnumWindow, pointerChildHandlesList);
                    }
                    finally
                    {
                        gcChildhandlesList.Free();
                    }

                    var child = childHandles.FirstOrDefault(x => GetWindowText(x) == "Всегда загружать");
                    if (child != IntPtr.Zero)
                    {
                        Console.WriteLine("\tPlugin loaded by WinApi");
                        SendMessage(child, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                        SendMessage(child, WM_LBUTTONUP, 0, IntPtr.Zero);
                        return false;
                    }
                }
                else if (GetWindowText(hWnd) == "Предупреждение")
                {
                    var childHandles = new List<IntPtr>();
                    GCHandle gcChildhandlesList = GCHandle.Alloc(childHandles);
                    IntPtr pointerChildHandlesList = GCHandle.ToIntPtr(gcChildhandlesList);

                    try
                    {
                        EnumChildWindows(hWnd, EnumWindow, pointerChildHandlesList);
                    }
                    finally
                    {
                        gcChildhandlesList.Free();
                    }

                    if (childHandles.Any(x => GetWindowText(x).Contains("модели ОПС")))
                    {
                        var child = childHandles.FirstOrDefault(x => GetWindowText(x) == "&Нет");
                        if (child != IntPtr.Zero)
                        {
                            Console.WriteLine("\tOPS error ignored by WinApi");
                            SendMessage(child, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                            SendMessage(child, WM_LBUTTONUP, 0, IntPtr.Zero);
                            return false;
                        }
                    }
                }

                return true;
            }, IntPtr.Zero);

        }
    }
}
