using Polymetal.Exchange.Geometry;
using Polymetal.Exchange.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace Polymetal.Exchange.Revit.Manual
{
    public partial class FormExport : Form
    {
        private static XmlSerializerNamespaces _xmlSerializerNamespaces = new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty });
        private static XmlSerializer _xmlFormat = new XmlSerializer(typeof(Utilities.DTO.IfcElementInformation));
        private static string _revitVersion = "2024";

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

        public FormExport()
        {
            InitializeComponent();
        }

        private void buttonSelectFile_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK && File.Exists(openFileDialog1.FileName))
            {
                if (Path.GetExtension(openFileDialog1.FileName).ToLower() == ".rvt")
                {
                    Export(openFileDialog1.FileName);
                }
                else if (Path.GetExtension(openFileDialog1.FileName).ToLower() == ".ifc")
                {
                    Import(openFileDialog1.FileName);
                }
            }
        }

        private void Export(string modelName)
        {
            var revitPath = string.Format(@"C:\Program Files\Autodesk\Revit {0}\Revit.exe", _revitVersion);
            
            var file = new FileInfo(modelName);
            if (!file.Exists || !File.Exists(revitPath))
                return;

            var outputDir = @"C:\AVEVA\Exchange\output";
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var ifcFile = new FileInfo(Path.Combine(outputDir,
                Path.GetFileName(modelName).Replace(Path.GetExtension(modelName), ".ifc")));
            if (ifcFile.Exists)
                ifcFile.Delete();

            this.Text = $"{DateTime.Now.ToLongTimeString()}: Запуск Revit";

            var oldDir = Environment.CurrentDirectory;
            Environment.SetEnvironmentVariable("PLMT_EXCHANGE", $"{modelName.Replace("#", "?")}#export#{ifcFile.FullName}#Revit#");

            var checkPluginLoadTimer = new System.Threading.Timer(LoadRevitPlugins, null, 10000, 10000);

            var procTimer = new Stopwatch();
            procTimer.Start();

            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo(revitPath, "/language RUS");
            proc.StartInfo.WorkingDirectory = oldDir;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = false;
            proc.EnableRaisingEvents = true;

            proc.Start();

            while (procTimer.Elapsed.TotalMinutes < 90 && Process.GetProcesses().Any(x => x.Id == proc.Id) && !proc.HasExited)
            {
                Thread.Sleep(5000);
            }
            ////proc.WaitForExit(10000000);
            if (!proc.HasExited)
                proc.Kill();

            checkPluginLoadTimer.Dispose();
            var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), "tmp.rvt"));
            Thread.Sleep(3000);
            try
            {
                tempFile.Delete();
            }
            catch
            { }

            Process.Start(outputDir);

            Close();
        }

        private void Import(string ifcPath)
        {
            var selectForm = new FormSelectProject(ifcPath);
            selectForm.ShowDialog(this);

            if (selectForm.DialogResult != DialogResult.OK)
                return;

            var projInfo = new ProjectInformation(selectForm.SelectedProjectId);
            var ifcFile = new FileInfo(ifcPath);

            var triggerId = InsertTrigger(selectForm.SelectedUser, selectForm.SelectedProjectId);
            if (projInfo.Program.TypeSource == Common.Source.Revit)
            {
                var result= DoArchitecture(ifcFile, projInfo, projInfo.ProjectId, triggerId);
                if (result)
                {
                    UpdateTrigger(triggerId, 1);
                }
            }

            Close();
        }

        private int InsertTrigger(string userLogin, decimal projectId)
        {
            var triggerId = 0;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand("SetExchangeTrigger", sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@UserLogin", userLogin);
                    sqlCmd.Parameters.AddWithValue("@ProjectId", projectId);
                    sqlCmd.Parameters.AddWithValue("@SourceTrigger", "Revit");
                    sqlCmd.Parameters.AddWithValue("@StatusTrigger", 4);

                    sqlCmd.CommandType = System.Data.CommandType.StoredProcedure;
                    sqlCmd.ExecuteNonQuery();
                }

                var cmd = $"SELECT TOP 1 IdExchangeTrigger FROM ExchangeTrigger WHERE SourceTrigger = 'Revit' AND ProjectId = {projectId} and StatusTrigger = 4 ORDER BY IdExchangeTrigger DESC";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    int.TryParse(res.ToString(), out triggerId);
                }

                sqlConnection.Close();
            }

            return triggerId;
        }

        private void UpdateTrigger(int triggerId, int status)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                var cmd = $"update ExchangeTrigger set StatusTrigger = {status} where IdExchangeTrigger = {triggerId}";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    sqlCmd.ExecuteNonQuery();
                }

                sqlConnection.Close();
            }
        }

        private static bool DoArchitecture(FileInfo ifcFile, ProjectInformation projInfo, decimal projectId, int triggerId)
        {
            DataTable dt, allGuidDt;
            var elementInformationList = RevitIfcParser.Run(ifcFile.FullName, projInfo.Program.TypeSource, projInfo.Transformation);

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
                            SendMessage(child, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                            SendMessage(child, WM_LBUTTONUP, 0, IntPtr.Zero);
                            return false;
                        }
                    }
                }
                else if (GetWindowText(hWnd) == "Предупреждение системы безопасности о макросах")
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

                    var child = childHandles.FirstOrDefault(x => GetWindowText(x).Contains("Отключить макросы"));
                    if (child != IntPtr.Zero)
                    {
                        SendMessage(child, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                        SendMessage(child, WM_LBUTTONUP, 0, IntPtr.Zero);
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);

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
    }
}
