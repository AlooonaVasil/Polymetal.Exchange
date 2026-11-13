using System;
using System.Data.SqlClient;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aveva.ApplicationFramework;
using Aveva.ApplicationFramework.Presentation;
using Aveva.Pdms.Database;
using Timer = System.Windows.Forms.Timer;

namespace Polymetal.Pdms.Client.Messenger
{
    public class MessengerAddin : IAddin
    {
        public string Description
        {
            get {return "MessengerAddin";}
        }

        public string Name
        {
            get {return "MessengerAddin";}
        }

        private static int _teklaLastRowId;
        private static int _magicLastRowId;
        private static int _revitLastRowId;
        private static decimal _teklaProjectId;
        private static decimal _magicProjectId;
        private static decimal _revitProjectId;
        private static Timer _checkStatusTimer;
        private static bool _teklaWaitUpdate;
        private static bool _magicWaitUpdate;
        private static bool _revitWaitUpdate;
        private static Balloon _balloon;

        private static string _mdbName;
        private static string _projectName;

        private WindowManager _wndManager;

        private static string connectionString = new sqlReference.Service1().GetStrConnectPdms();
        
        private enum UpdateSource
        {
            Tekla,
            MagiCAD,
            Revit
        }

        public void Start(ServiceManager serviceManager)
        {
            _wndManager = (WindowManager)serviceManager.GetService((typeof(WindowManager)));
            _wndManager.WindowLayoutLoaded += WindowManagerWindowLayoutLoaded;
        }

        void WindowManagerWindowLayoutLoaded(object sender, EventArgs e)
        {
            _wndManager.WindowLayoutLoaded -= WindowManagerWindowLayoutLoaded;

            _mdbName = MDB.CurrentMDB.Name;
            _projectName = Project.CurrentProject.Name;

            if (!IsTeklaChecking() & !IsMagiCadChecking() & !IsRevitChecking()) return;
            var timerThread = new Thread(() =>
            {
                RunTimer();
                Dispatcher.Run();
            }) { Name = "WPF UI Thread" };
            timerThread.SetApartmentState(ApartmentState.STA);
            timerThread.Start();
        }

        private void RunTimer()
        {
            _checkStatusTimer = new Timer { Interval = 10000 };
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();
        }

        private void _checkStatusTimer_Tick(object sender, EventArgs e)
        {
            if (_teklaProjectId < 1 && _magicProjectId <1 && _revitProjectId<1) return;

            _checkStatusTimer.Stop();
   
            if (_teklaProjectId > 0)
            {
                if (_teklaWaitUpdate)
                {
                    //проверка текущего статуса, ожидание завершения операции обновления
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  IdExchangeTrigger = " + _teklaLastRowId;

                    using (var sqlConnection = new SqlConnection(connectionString))
                    {
                        sqlConnection.Open();
                        int status;
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            var reader = sqlCmd.ExecuteScalar();
                            int.TryParse(reader.ToString(), out status);
                        }

                        if (status > 1)
                        {
                            if (status == 2)
                            {
                                sqlCommand = "USE " +
                                             "  Aveva_Poly_Db " +
                                             "SELECT TOP 1 " +
                                             "  [Message] " +
                                             "FROM " +
                                             "  ExchangeLog " +
                                             "WHERE " +
                                             "  IdExchangeTrigger = " + _teklaLastRowId + " AND " +
                                             "  SourceLog = 'PDMS' AND " +
                                             "  [Message] LIKE 'Изменений в модели:%' " +
                                             "ORDER BY " +
                                             "  IdExchangeLog DESC";
                                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                                {
                                    var reader = sqlCmd.ExecuteScalar();
                                    if (reader == null)
                                    {
                                        ShowBaloon(UpdateSource.Tekla);
                                    }
                                    else
                                    {
                                        int count;

                                        if (
                                            int.TryParse(reader.ToString().Replace("Изменений в модели:", "").Trim(),
                                                out count) && count > 0)
                                        {
                                            ShowBaloon(UpdateSource.Tekla);
                                        }
                                    }
                                }
                            }
                            _teklaWaitUpdate = false;
                            _checkStatusTimer.Interval = _magicWaitUpdate ||_revitWaitUpdate? 5000 : 10000;
                        }
                        sqlConnection.Close();
                    }
                }
                else
                {
                    //проверка поступления задания на обновление модели
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  IdExchangeTrigger, " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  SourceTrigger = 'Tekla' AND " +
                                     "  ProjectId = " + _teklaProjectId + " AND " +
                                     "  IdExchangeTrigger > " + _teklaLastRowId + " " +
                                     "ORDER BY " +
                                     "  IdExchangeTrigger DESC";

                    using (var sqlConnection = new SqlConnection(connectionString))
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
                                if (rowId > _teklaLastRowId && status < 2)
                                {
                                    _teklaLastRowId = rowId;
                                    _checkStatusTimer.Interval = 5000;
                                    _teklaWaitUpdate = true;
                                }
                            }
                            reader.Close();
                        }
                        sqlConnection.Close();
                    }
                }
            }

            if (_revitProjectId > 0)
            {
                if (_revitWaitUpdate)
                {
                    //проверка текущего статуса, ожидание завершения операции обновления
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  IdExchangeTrigger = " + _revitLastRowId;

                    using (var sqlConnection = new SqlConnection(connectionString))
                    {
                        sqlConnection.Open();
                        int status;
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            var reader = sqlCmd.ExecuteScalar();
                            int.TryParse(reader.ToString(), out status);
                        }

                        if (status > 1)
                        {
                            if (status == 2)
                            {
                                sqlCommand = "USE " +
                                             "  Aveva_Poly_Db " +
                                             "SELECT TOP 1 " +
                                             "  [Message] " +
                                             "FROM " +
                                             "  ExchangeLog " +
                                             "WHERE " +
                                             "  IdExchangeTrigger = " + _revitLastRowId + " AND " +
                                             "  SourceLog = 'PDMS' AND " +
                                             "  [Message] LIKE 'Изменений в модели:%' " +
                                             "ORDER BY " +
                                             "  IdExchangeLog DESC";
                                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                                {
                                    var reader = sqlCmd.ExecuteScalar();
                                    if (reader == null)
                                    {
                                        ShowBaloon(UpdateSource.Revit);
                                    }
                                    else
                                    {
                                        int count;
                                        if (
                                            int.TryParse(reader.ToString().Replace("Изменений в модели:", "").Trim(),
                                                out count) && count > 0)
                                        {
                                            ShowBaloon(UpdateSource.Revit);
                                        }
                                    }
                                }
                            }

                            _revitWaitUpdate = false;
                            _checkStatusTimer.Interval = _teklaWaitUpdate ||_magicWaitUpdate ? 5000 : 10000;
                        }
                        sqlConnection.Close();
                    }
                }
                else
                {
                    //проверка поступления задания на обновление модели
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  IdExchangeTrigger, " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  SourceTrigger = 'Revit' AND " +
                                     "  ProjectId = " + _revitProjectId + " AND " +
                                     "  IdExchangeTrigger > " + _revitLastRowId + " " +
                                     "ORDER BY " +
                                     "  IdExchangeTrigger DESC";

                    using (var sqlConnection = new SqlConnection(connectionString))
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
                                if (rowId > _revitLastRowId && status < 2)
                                {
                                    _revitLastRowId = rowId;
                                    _checkStatusTimer.Interval = 5000;
                                    _revitWaitUpdate = true;
                                }
                            }
                            reader.Close();
                        }
                        sqlConnection.Close();
                    }
                }
            }

            if (_magicProjectId > 0)
            {
                if (_magicWaitUpdate)
                {
                    //проверка текущего статуса, ожидание завершения операции обновления
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  IdExchangeTrigger = " + _magicLastRowId;

                    using (var sqlConnection = new SqlConnection(connectionString))
                    {
                        sqlConnection.Open();
                        int status;
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            var reader = sqlCmd.ExecuteScalar();
                            int.TryParse(reader.ToString(), out status);
                        }

                        if (status > 1)
                        {
                            if (status == 2)
                            {
                                sqlCommand = "USE " +
                                             "  Aveva_Poly_Db " +
                                             "SELECT TOP 1 " +
                                             "  [Message] " +
                                             "FROM " +
                                             "  ExchangeLog " +
                                             "WHERE " +
                                             "  IdExchangeTrigger = " + _magicLastRowId + " AND " +
                                             "  SourceLog = 'PDMS' AND " +
                                             "  [Message] LIKE 'Изменений в модели:%' " +
                                             "ORDER BY " +
                                             "  IdExchangeLog DESC";
                                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                                {
                                    var reader = sqlCmd.ExecuteScalar();
                                    if (reader == null)
                                    {
                                        ShowBaloon(UpdateSource.MagiCAD);
                                    }
                                    else
                                    {
                                        int count;
                                        if (
                                            int.TryParse(reader.ToString().Replace("Изменений в модели:", "").Trim(),
                                                out count) && count > 0)
                                        {
                                            ShowBaloon(UpdateSource.MagiCAD);
                                        }
                                    }
                                }
                            }

                            _magicWaitUpdate = false;
                            _checkStatusTimer.Interval = _teklaWaitUpdate ||_revitWaitUpdate ? 5000 : 10000;
                        }
                        sqlConnection.Close();
                    }
                }
                else
                {
                    //проверка поступления задания на обновление модели
                    var sqlCommand = "USE " +
                                     "  Aveva_Poly_Db " +
                                     "SELECT " +
                                     "  IdExchangeTrigger, " +
                                     "  StatusTrigger " +
                                     "FROM " +
                                     "  ExchangeTrigger " +
                                     "WHERE " +
                                     "  SourceTrigger = 'MagiCAD' AND " +
                                     "  ProjectId = " + _magicProjectId + " AND " +
                                     "  IdExchangeTrigger > " + _magicLastRowId + " " +
                                     "ORDER BY " +
                                     "  IdExchangeTrigger DESC";

                    using (var sqlConnection = new SqlConnection(connectionString))
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
                                if (rowId > _magicLastRowId && status < 2)
                                {
                                    _magicLastRowId = rowId;
                                    _checkStatusTimer.Interval = 5000;
                                    _magicWaitUpdate = true;
                                }
                            }
                            reader.Close();
                        }
                        sqlConnection.Close();
                    }
                }
            }

            _checkStatusTimer.Start();
        }

        private static void ShowBaloon(UpdateSource source)
        {
            var iconName = string.Format("Images/{0}", source == UpdateSource.Tekla ? "tekla.png" : (source== UpdateSource.MagiCAD?"magicad.png":"revit.png"));
            var notificationText = string.Format("Обновлена {0} {1}",
                source == UpdateSource.Tekla
                    ? "строительная часть из Tekla Structures."
                    : (source == UpdateSource.MagiCAD
                        ? "сантехническая часть из MagiCAD."
                        : "строительная часть из Revit."), "Для получения актуальных данных обновите модель.");
            var balloonText = string.Format("Уведомление {0}",
                source == UpdateSource.Tekla
                    ? "Tekla Structures"
                    : (source == UpdateSource.MagiCAD ? "MagiCAD" : "Revit"));
            _balloon = new Balloon()
            {
                ImageSource = new BitmapImage(new Uri(iconName, UriKind.RelativeOrAbsolute)),
                MdbName = _mdbName,
                NotificationText = notificationText,
                ProjectName = _projectName,
                BalloonText = balloonText
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

        private static bool IsTeklaChecking()
        {
            var result = false;
            var sqlCommand = "USE " +
                             "  Aveva_Poly_Db " +
                             "SELECT " +
                             "  ProjectID " +
                             "FROM " +
                             "  ExchangeProject " +
                             "JOIN " +
                             "  vPdmsProjects ON vPdmsProjects.IdSubObject = ExchangeProject.SubObjectID " +
                             "WHERE " +
                             "  NeedExchange = 1 AND " +
                             "  Program = 'Tekla' AND " +
                             "  MDB = '" + _mdbName + "' AND " +
                             "  Project = '" + _projectName + "'";

            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        result = decimal.TryParse(reader["ProjectID"].ToString(), out _teklaProjectId) && _teklaProjectId > 0;
                        //_teklaModelName = reader["TeklaModelName"].ToString();
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        private static bool IsMagiCadChecking()
        {
            var result = false;

            var sqlCommand = "USE " +
                             "  Aveva_Poly_Db " +
                             "SELECT " +
                             "  ProjectID " +
                             "FROM " +
                             "  ExchangeProject " +
                             "JOIN " +
                             "  vPdmsProjects ON vPdmsProjects.IdSubObject = ExchangeProject.SubObjectID " +
                             "WHERE " +
                             "  NeedExchange = 1 AND " +
                             "  Program = 'MagiCAD' AND " +
                             "  MDB = '" + _mdbName + "' AND " +
                             "  Project = '" + _projectName + "'";

            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        result = decimal.TryParse(reader["ProjectID"].ToString(), out _magicProjectId) && _magicProjectId > 0;
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        private static bool IsRevitChecking()
        {
            var result = false;

            var sqlCommand = "USE " +
                             "  Aveva_Poly_Db " +
                             "SELECT " +
                             "  ProjectID " +
                             "FROM " +
                             "  ExchangeProject " +
                             "JOIN " +
                             "  vPdmsProjects ON vPdmsProjects.IdSubObject = ExchangeProject.SubObjectID " +
                             "WHERE " +
                             "  NeedExchange = 1 AND " +
                             "  Program = 'Revit' AND " +
                             "  MDB = '" + _mdbName + "' AND " +
                             "  Project = '" + _projectName + "'";

            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        result = decimal.TryParse(reader["ProjectID"].ToString(), out _revitProjectId) && _revitProjectId > 0;
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        public void Stop()
        {
        }
    }
}
