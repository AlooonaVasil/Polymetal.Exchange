using System;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using TSM = Tekla.Structures.Model;
using Timer = System.Windows.Forms.Timer;
using System.Xml.Serialization;
using System.Management;

namespace Polymetal.Tekla.Client.v2021
{
    static class Start
    {
        private static Process Parent;
        private static TSM.Events _events;
        private static readonly string UserLogin = Environment.UserName;
        private static decimal _projectId;
        private static string _modelName;
        private static int _avevaExportRowId;
        private static int _teklaSaveRowId;
        private static string _connectionString = new sqlReference.Service1().GetStrConnectPdms();
        private static Timer _checkStatusTimer;
        private static bool _waitAvevaUpdate;
        private static bool _waitTeklaUpdate;
        private static bool _showMessage;
        private static Thread timerThread;

        private static Balloon _balloon;

        private static string _configPath = @"\\spbfs02\cad\Tekla\MacrosPMI\PolyDLL\files_copy2021.config";

        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            PolyFilesCopy();

            Parent = GetParentProcess();
            Parent.EnableRaisingEvents = true;
            Parent.Exited += Parent_Exited;

            _events = new TSM.Events();
            _events.ModelLoad += Events_ModelLoad;
            _events.ModelSave += Events_ModelSave;
            _events.Register();

            timerThread = new Thread(() =>
            {
                RunTimer();
                System.Windows.Threading.Dispatcher.Run();
            })
            { Name = "WPF UI Thread" };
            timerThread.SetApartmentState(ApartmentState.STA);
            timerThread.Start();
        }

        /// <summary>
        /// Распространение библиотек ОТП с сервера
        /// </summary>
        private static void PolyFilesCopy()
        {
            if (!File.Exists(_configPath))
                return;

            try
            {
                //MessageBox.Show("copy files");
                XmlSerializer formatter = new XmlSerializer(typeof(CopyPath[]));

                using (FileStream fs = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CopyPath[] newpaths = (CopyPath[])formatter.Deserialize(fs);
                    //MessageBox.Show(newpaths.ToString());

                    foreach (CopyPath path in newpaths)
                    {
                        ProcessStartInfo processInfo;
                        Process process;
                        processInfo = new ProcessStartInfo("cmd.exe", $"/C xcopy \"{path.Source}\" \"{path.Destination}\" /I /C /Y /Z /D")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        process = Process.Start(processInfo);
                        process.WaitForExit();

                        process.Close();
                        //MessageBox.Show("FilesCopied");

                        //ProcessStartInfo processInfo;
                        //Process process = new Process();
                        //process.StartInfo.FileName = "cmd.exe";
                        //process.StartInfo.Arguments = $"/C xcopy \"{path.Source}\" \"{path.Destination}\" /I /C /Y /Z /D";
                        //process.StartInfo.RedirectStandardInput = true;

                        //process.Start();
                        //process.WaitForExit();

                        ////process.Close();
                        //MessageBox.Show("FilesCopied");
                    }
                }
            }
            catch (Exception e)
            {
                //MessageBox.Show(e.Message + "\n" + e.StackTrace);
            }
        }

        public static void Events_ModelLoad()
        {
            SetProjectId();
        }

        public static void Events_ModelSave()
        {
            SetProjectId();
            if (_projectId < 1) return;
            //запись в базу факта сохранения
            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand("SetExchangeTrigger", sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@UserLogin", UserLogin);
                    sqlCmd.Parameters.AddWithValue("@ProjectId", _projectId);
                    sqlCmd.Parameters.AddWithValue("@SourceTrigger", "Tekla");
                    sqlCmd.Parameters.AddWithValue("@StatusTrigger", 0);

                    sqlCmd.CommandType = System.Data.CommandType.StoredProcedure;
                    sqlCmd.ExecuteNonQuery();
                }

                var cmd = $"SELECT TOP 1 IdExchangeTrigger FROM ExchangeTrigger WHERE SourceTrigger = 'Tekla' AND ProjectId = {_projectId} and StatusTrigger < 2 ORDER BY IdExchangeTrigger DESC";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    int.TryParse(res.ToString(), out _teklaSaveRowId);
                }

                sqlConnection.Close();
            }

            _showMessage = true;
            _waitTeklaUpdate = true;
        }

        private static void _checkStatusTimer_Tick(object sender, EventArgs e)
        {
            if (_projectId < 1) return;

            _checkStatusTimer.Stop();

            if (_waitTeklaUpdate)
            {
                if (_showMessage)
                {
                    ShowBaloon("Отправлено задание на обновление модели в Aveva", "Уведомление Tekla", "tekla.png");
                    _showMessage = false;
                }
                var cmd = $"SELECT count(IdExchangeTrigger) FROM ExchangeTrigger WHERE SourceTrigger = 'Tekla' AND ProjectId = {_projectId} AND IdExchangeTrigger >= {_teklaSaveRowId} and StatusTrigger = 2";
                using (var sqlConnection = new SqlConnection(_connectionString))
                {
                    sqlConnection.Open();
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        var res = sqlCmd.ExecuteScalar();
                        if (res != null && (int)res > 0)
                        {
                            ShowBaloon("Завершено обновление проекта в Aveva", "Уведомление Aveva", "aveva.png");
                            _waitTeklaUpdate = false;
                        }
                    }
                    sqlConnection.Close();
                }
            }

            if (_waitAvevaUpdate)
            {
                //проверка текущего статуса, ожидание завершения операции обновления
                var cmd = $"SELECT StatusTrigger FROM ExchangeTrigger WHERE IdExchangeTrigger = {_avevaExportRowId}";
                using (var sqlConnection = new SqlConnection(_connectionString))
                {
                    sqlConnection.Open();
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        var res = sqlCmd.ExecuteScalar();
                        int.TryParse(res.ToString(), out int status);
                        if (status > 1)
                        {
                            ShowBaloon("Обновлены опорные модели Aveva. Для получения актуальных данных обновите модель.", "Уведомление Aveva", "aveva.png");
                            _waitAvevaUpdate = false;
                        }
                    }
                    sqlConnection.Close();
                }
            }
            else
            {
                //проверка поступления задания на обновление модели
                var cmd = $"SELECT IdExchangeTrigger, StatusTrigger FROM ExchangeTrigger WHERE SourceTrigger = 'Pdms' AND ProjectId = {_projectId} AND IdExchangeTrigger > {_avevaExportRowId} ORDER BY IdExchangeTrigger DESC";
                using (var sqlConnection = new SqlConnection(_connectionString))
                {
                    sqlConnection.Open();
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        using (var reader = sqlCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int.TryParse(reader["IdExchangeTrigger"].ToString(), out int rowId);
                                int.TryParse(reader["StatusTrigger"].ToString(), out int status);
                                if (rowId > _avevaExportRowId && status < 2)
                                {
                                    _avevaExportRowId = rowId;
                                    _waitAvevaUpdate = true;
                                }
                            }
                        }
                    }
                    sqlConnection.Close();
                }
            }

            _checkStatusTimer.Start();
        }

        private static void Events_TeklaStructuresExit()
        {
            timerThread.Abort();
            _events.ModelLoad -= Events_ModelLoad;
            _events.ModelSave -= Events_ModelSave;
            _events.TeklaStructuresExit -= Events_TeklaStructuresExit;
            Application.Exit();
        }

        private static void Parent_Exited(object sender, EventArgs e)
        {
            timerThread.Abort();
            _events.ModelLoad -= Events_ModelLoad;
            _events.ModelSave -= Events_ModelSave;
            _events.TeklaStructuresExit -= Events_TeklaStructuresExit;
            Application.Exit();
        }

        private static void RunTimer()
        {
            _checkStatusTimer = new Timer { Interval = 10000 };
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();
        }

        private static void ShowBaloon(string notificationText, string balloonText, string pngName)
        {
            var mdbName = string.Empty;
            var projectName = string.Empty;
            if (_modelName.Split('-').Length > 1)
            {
                mdbName = _modelName.Split('-').First().Trim();
                projectName = _modelName.Substring(_modelName.IndexOf('-') + 1).Trim();
            }
            else
            {
                projectName = _modelName.Trim();
            }
            _balloon = new Balloon
            {
                ImageSource = new BitmapImage(new Uri($"/Images/{pngName}", UriKind.Relative)),
                MdbName = mdbName,
                NotificationText = notificationText,
                ProjectName = projectName,
                BalloonText = balloonText
            };

            _balloon.RaisePropertyChanged("ImageSource");
            _balloon.RaisePropertyChanged("BalloonText");
            _balloon.RaisePropertyChanged("MdbName");
            _balloon.RaisePropertyChanged("NotificationText");
            _balloon.RaisePropertyChanged("ProjectName");

            _balloon.Show();
            var tibaloonTimermer = new Timer { Interval = 15000 };
            tibaloonTimermer.Tick += baloonTimer_Tick;
            tibaloonTimermer.Start();
        }

        private static void baloonTimer_Tick(object sender, EventArgs e)
        {
            _balloon.HideWindow();
        }

        private static void SetProjectId()
        {
            var model = new TSM.Model();
            var modelPath = model.GetInfo().ModelPath;
            var modelPathUnc = GetUNCPath(modelPath);
            _modelName = model.GetInfo().ModelName;
            _modelName = _modelName.Substring(0, _modelName.IndexOf(".db", StringComparison.OrdinalIgnoreCase));


            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();

                var cmd = $"SELECT ProjectID FROM ExchangeProject WHERE ModelPath = '{modelPathUnc}'";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    _projectId = res == null ? 0 : (decimal)res;
                }

                if (_projectId == 0 && modelPath != modelPathUnc)
                {
                    cmd = $"SELECT ProjectID FROM ExchangeProject WHERE ModelPath = '{modelPath}'";
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        var res = sqlCmd.ExecuteScalar();
                        _projectId = res == null ? 0 : (decimal)res;
                    }
                }

                sqlConnection.Close();
            }
        }

        /// <summary>
        /// Получение полного сетевого пути
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string GetUNCPath(string path)
        {
            if (path.StartsWith(@"\\"))
                return path;

            var drive = new DriveInfo(Path.GetPathRoot(path));
            if (drive.DriveType != DriveType.Network)
                return path;

            string result;
            var driveLetter = drive.Name.Replace(":\\", ":");
            var managementObject = new ManagementObject(new ManagementPath($"Win32_LogicalDisk='{driveLetter}'"));

            try
            {
                var providerName = (string)managementObject["ProviderName"];
                result = $"{providerName}{path.Substring(driveLetter.Length)}";
            }
            catch
            {
                result = path;
            }

            return Directory.Exists(Path.GetDirectoryName(result)) ? result : path;
        }

        private static Process GetParentProcess()
        {
            var teklaProcesses = Process.GetProcessesByName("TeklaStructures");
            if (teklaProcesses.Length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (teklaProcesses.Length == 1) return teklaProcesses.First();
            return Process.GetCurrentProcess().Parent();
        }
    }

    /// <summary>
    /// Класс для сериализации конфига распространения библиотек. CopyPath - Является единицей конфига и содержит путь откуда копировать файлы, и путь назначения
    /// </summary>
    [Serializable]
    public class CopyPath
    {
        public string Source { get; set; }
        public string Destination { get; set; }

        public CopyPath()
        { }

        public CopyPath(string source, string destination)
        {
            Source = source;
            Destination = destination;
        }
    }
}
