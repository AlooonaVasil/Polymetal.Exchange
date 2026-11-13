using System;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using Timer = System.Windows.Forms.Timer;

namespace Polymetal.Tekla.Client
{
    static class Start
    {
        private static string _logPath;
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            _logPath = Path.Combine(Path.GetTempPath(), "polymetal.tekla.client.log");
            if (File.Exists(_logPath))
            {
                try
                {
                    File.Delete(_logPath);
                }
                catch { }
            }

            var parentProcessId = 0;
            if (args.Length == 0)
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var pbi = new PROCESS_BASIC_INFORMATION();
                    int writed;

                    if (0 != NtQueryInformationProcess(process.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out writed) ||
                        writed == 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    parentProcessId = pbi.InheritedFromUniqueProcessId.ToInt32();
                }
            }
            else
            {
                parentProcessId = int.Parse(args[0]);
            }

            var teklaEvents = new TeklaEvents(parentProcessId);
            if (!teklaEvents.Running) return;
            Application.Run();
        }

        internal static void WriteLog(string message)
        {
            using (var sw = new StreamWriter(_logPath, true))
            {
                sw.WriteLine(message);                
                sw.Close();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public uint ExitStatus;
            public IntPtr PebBaseAddress; // Zero if 32 bit process try get info about 64 bit process 
            public IntPtr AffinityMask;
            public int BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll", SetLastError = true, ExactSpelling = true)]
        private static extern uint NtQueryInformationProcess(
            IntPtr ProcessHandle,
            uint ProcessInformationClass,
            ref PROCESS_BASIC_INFORMATION ProcessInformation,
            int ProcessInformationLength,
            out int ReturnLength
            );
    }

    class TeklaEvents
    {
        private static readonly string UserLogin = Environment.UserName;
        private static decimal _projectId;
        private static string _modelName;
        private static int _parentId;
        private static readonly string[] EventNames = { "ModelLoad", "ModelSave", "TeklaStructuresExit" };
        private static Type _eventsType;
        private static Type _modelType;
        private static Type _modelInfoType;
        private static Timer _checkStatusTimer;
        private static bool _waitUpdate = false;
        private static int _lastRowId;
        private static Balloon _balloon;
        private static Thread timerThread;
        internal bool Running;
       
        private static string _connectionString = new sqlReference.Service1().GetStrConnectPdms();

        internal TeklaEvents(int parentProcessId)
        {
            _parentId = parentProcessId;
            
            if (!IsProcessAlive(out Process teklaProcess)) return;
            
            if (!LoadAssembly(out Assembly assembly) || assembly == null) return;

            teklaProcess.EnableRaisingEvents = true;
            teklaProcess.Exited += teklaProcess_Exited;
            
            _eventsType = assembly.GetType("Tekla.Structures.Model.Events");
            _modelType = assembly.GetType("Tekla.Structures.Model.Model");
            _modelInfoType = assembly.GetType("Tekla.Structures.Model.ModelInfo");           

            Start.WriteLog($"{_eventsType.FullName}\n{_modelType.FullName}\n{_modelInfoType.FullName}");            

            SubscribeTeklaEvents();
            
            /*_checkStatusTimer = new Timer{Interval = 10000};
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();*/

            timerThread = new Thread(() =>
            {
                RunTimer();
                System.Windows.Threading.Dispatcher.Run();
            }) { Name = "WPF UI Thread" };
            timerThread.SetApartmentState(ApartmentState.STA);
            timerThread.Start();
            Running = true;
        }

        private void RunTimer()
        {
            _checkStatusTimer = new Timer { Interval = 60000 };
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();
        }

        private void _checkStatusTimer_Tick(object sender, EventArgs e)
        {
            if (_projectId < 1) return;

            _checkStatusTimer.Stop();

            try
            {
                if (_waitUpdate)
                {
                    //проверка текущего статуса, ожидание завершения операции обновления
                    var sqlCommand = "USE Aveva_Poly_Db " +
                                     "SELECT StatusTrigger " +
                                     "FROM ExchangeTrigger " +
                                     "WHERE IdExchangeTrigger = " + _lastRowId;

                    using (var sqlConnection = new SqlConnection(_connectionString))
                    {
                        sqlConnection.Open();
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            var reader = sqlCmd.ExecuteScalar();
                            int status;
                            int.TryParse(reader.ToString(), out status);
                            if (status > 1)
                            {
                                ShowBaloon();
                                _waitUpdate = false;
                                _checkStatusTimer.Interval = 10000;
                            }
                        }
                        sqlConnection.Close();
                    }
                }
                else
                {
                    //проверка поступления задания на обновление модели
                    var sqlCommand = "USE Aveva_Poly_Db " +
                                     "SELECT IdExchangeTrigger, StatusTrigger " +
                                     "FROM ExchangeTrigger " +
                                     "WHERE SourceTrigger = 'Pdms' AND ProjectId = " + _projectId + " AND IdExchangeTrigger > " +
                                     _lastRowId + " " +
                                     "ORDER BY IdExchangeTrigger DESC";

                    using (var sqlConnection = new SqlConnection(_connectionString))
                    {
                        sqlConnection.Open();
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            var reader = sqlCmd.ExecuteReader();
                            if (reader.Read())
                            {
                                int rowId, status;
                                int.TryParse(reader["IdExchangeTrigger"].ToString(), out rowId);
                                int.TryParse(reader["StatusTrigger"].ToString(), out status);
                                if (rowId > _lastRowId && status < 2)
                                {
                                    _lastRowId = rowId;
                                    _checkStatusTimer.Interval = 5000;
                                    _waitUpdate = true;
                                }
                            }
                            reader.Close();
                        }
                        sqlConnection.Close();
                    }
                }
            }
            catch (Exception exc)
            {
                Start.WriteLog($"_checkStatusTimer_Tick exception:\n{exc.Message}\n{exc.StackTrace}");
            }

            _checkStatusTimer.Start();
        }

        private static void ShowBaloon()
        {
            var mdbName = string.Empty;
            var projectName = string.Empty;
            if (_modelName.Split('-').Length > 1)
            {
                mdbName = _modelName.Split('-').First().Trim();
                projectName = _modelName.Substring(_modelName.IndexOf('-')+1).Trim();
            }
            else
            {
                projectName = _modelName.Trim();
            }
            _balloon = new Balloon
            {
                ImageSource = new BitmapImage(new Uri("/Images/" + "aveva.png", UriKind.Relative)),
                MdbName = mdbName,
                NotificationText = "Обновлены опорные модели Aveva PDMS. Для получения актуальных данных обновите модель.",
                ProjectName = projectName,
                BalloonText = "Уведомление Aveva PDMS"
            };
            _balloon.RaisePropertyChanged("ImageSource");
            _balloon.RaisePropertyChanged("BalloonText");
            _balloon.RaisePropertyChanged("MdbName");
            _balloon.RaisePropertyChanged("NotificationText");
            _balloon.RaisePropertyChanged("ProjectName");
            
            _balloon.Show();
            var timer = new Timer { Interval = 15000 };
            timer.Tick += timer_Tick;
            timer.Start();
        }

        private static void timer_Tick(object sender, EventArgs e)
        {
            _balloon.HideWindow();
        }

        /// <summary>
        /// Подписка и отписка от событий TeklaStructures
        /// </summary>
        private static void SubscribeTeklaEvents(bool unsubscribe = false)
        {
            var eventsObject = Activator.CreateInstance(_eventsType);

            foreach (var eventName in EventNames)
            {
                var teklaEvent = _eventsType.GetEvent(eventName);
                var handlerModelLoadType = teklaEvent.EventHandlerType;
                var curDelegate = Delegate.CreateDelegate(handlerModelLoadType,
                    typeof (TeklaEvents).GetMethod("Ts" + eventName));
                if (unsubscribe)
                    teklaEvent.RemoveEventHandler(eventsObject, curDelegate);
                else
                    teklaEvent.AddEventHandler(eventsObject, curDelegate);
            }

            try
            {
                _eventsType.InvokeMember(unsubscribe ? "UnRegister" : "Register", BindingFlags.InvokeMethod, null, eventsObject, new Object[] { });
            }
            catch (Exception exc)
            {
                Start.WriteLog($"SubscribeTeklaEvents exception:\n{exc.Message}\n{exc.StackTrace}");                
            }
        }

        /// <summary>
        /// Загрузка сборки Tekla.Structures.Model.dll
        /// </summary>
        /// <param name="assembly">сборка</param>
        /// <returns>true - если загрузка успешна</returns>
        private bool LoadAssembly(out Assembly assembly)
        {
            var path = GetModulePathFromProcess();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                assembly = null;
                return false;
            }
            assembly = Assembly.LoadFrom(path);
            return true;
        }

        /// <summary>
        /// Поиск сборки Tekla.Structures.Model.dll в родительском процессе TeklaStructures.exe
        /// </summary>
        /// <returns>путь к сборке</returns>
        private string GetModulePathFromProcess()
        {
            if (!IsProcessAlive(out Process teklaProcess)) return string.Empty;
            try
            {
                var modules = teklaProcess.Modules;
                foreach (ProcessModule module in modules)
                {
                    var info = module.FileVersionInfo;
                    if (info == null || info.OriginalFilename == null) continue;

                    if (info.OriginalFilename.Trim().Equals("Tekla.Structures.Model.dll"))
                    {
                        return info.FileName;
                    }

                    var fileInfo = new FileInfo(teklaProcess.MainModule.FileName);
                    var tempPath = Path.Combine(fileInfo.Directory.FullName, @"plugins\Tekla.Structures.Model.dll");
                    if (File.Exists(tempPath))
                        return tempPath;
                }
            }
            catch (Exception exc)
            {
                Start.WriteLog($"GetModulePathFromProcess exception:\n{exc.Message}\n{exc.StackTrace}");                
            }               
            
            return string.Empty;
        }

        /// <summary>
        /// Проверка присутствия родительского процесса в дереве процессов
        /// </summary>
        /// <param name="teklaProc">Объект Process родительского процесса</param>
        /// <returns>true - если процесс существует</returns>
        private bool IsProcessAlive(out Process teklaProc)
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes.Where(proc => proc.Id == _parentId))
            {
                teklaProc = proc;
                return true;
            }

            teklaProc = null;
            return false;
        }

        /// <summary>
        /// Событие открытия модели в TeklaStructures
        /// </summary>
        public static void TsModelLoad()
        {
            SetProjectId();
        }

        /// <summary>
        /// Событие сохранения модели в TeklaStructures
        /// </summary>
        public static void TsModelSave()
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
                sqlConnection.Close();
            }
            Start.WriteLog($"TsModelSave userlogin={UserLogin} projectid={_projectId}");
        }

        /// <summary>
        /// Событие выхода из приложения
        /// </summary>
        public static void TsTeklaStructuresExit()
        {
            timerThread.Abort();
            SubscribeTeklaEvents(true);
            Application.Exit();
        }

        /// <summary>
        /// Событие завершение родительского процесса TeklaStructures.exe
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void teklaProcess_Exited(object sender, EventArgs e)
        {
            timerThread.Abort();
            SubscribeTeklaEvents(true);
            Application.Exit();
        }

        /// <summary>
        /// Инициализация значения ID проекта
        /// </summary>
        private static void SetProjectId()
        {
            var modelObject = Activator.CreateInstance(_modelType);
            var modelInfoObject = _modelType.InvokeMember("GetInfo", BindingFlags.InvokeMethod, null, modelObject, new object[] { });
            var modelNameProp = _modelInfoType.GetProperty("ModelName", typeof(string));
            var modelNameValue = modelNameProp.GetValue(modelInfoObject, new object[] { });

            var modelPathProp = _modelInfoType.GetProperty("ModelPath", typeof(string));
            var modelPathValue = modelPathProp.GetValue(modelInfoObject, new object[] { });

            _modelName = (string) modelNameValue;
            _modelName = _modelName.Substring(0, _modelName.IndexOf(".db", StringComparison.OrdinalIgnoreCase));
            var sqlCommand = "SELECT " +
                                "ProjectID " +
                            "FROM " +
                                "[Aveva_Poly_Db].[dbo].[ExchangeProject] " +
                            "WHERE " +
                                "ModelPath = '" + modelPathValue + "'";

            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res!=null)
                    {
                        _projectId = (decimal)res;
                    }                    
                }
                sqlConnection.Close();
            }
            Start.WriteLog($"SetProjectId modelpath={modelPathValue} projectid={_projectId}");
        }
    }
}
