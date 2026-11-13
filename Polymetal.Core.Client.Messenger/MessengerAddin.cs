using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aveva.ApplicationFramework;
using Aveva.ApplicationFramework.Presentation;
using Aveva.Core.Database;
using Timer = System.Windows.Forms.Timer;

namespace Polymetal.Core.Client.Messenger
{
    public class MessengerAddin : IAddin
    {
        public string Description => "MessengerAddin";

        public string Name => "MessengerAddin";

        private static string connectionString = Polymetal.Core.Common.SQL.ConnectionString;

        private const int CHECK_INTERVAL = 10000;

        private static int _triggerLastRowId;

        private static Timer _checkStatusTimer;

        private WindowManager _wndManager;

        private static string _сurrentProjectName = Project.CurrentProject.Name;
        private static string _сurrentMdbName = MDB.CurrentMDB.Name;

        private static Dictionary<int, TriggerState> _triggers;

        public void Start(ServiceManager serviceManager)
        {
            _wndManager = (DependencyResolver.GetImplementationOf<IWindowManager>() as WindowManager);
            _wndManager.WindowLayoutLoaded += WindowManagerWindowLayoutLoaded;
        }

        public void Stop()
        {
        }

        void WindowManagerWindowLayoutLoaded(object sender, EventArgs e)
        {
            _wndManager.WindowLayoutLoaded -= WindowManagerWindowLayoutLoaded;

            _triggers = new Dictionary<int, TriggerState>();
            _triggerLastRowId = 0;

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
            _checkStatusTimer = new Timer { Interval = CHECK_INTERVAL };
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();
        }

        private void _checkStatusTimer_Tick(object sender, EventArgs e)
        {
            _checkStatusTimer.Stop();

            UpdateTriggerStates(out var notificationSource);

            foreach (var source in notificationSource)
            {
                ShowBaloon(source);
            }

            _checkStatusTimer.Start();
        }

        private static void UpdateTriggerStates(out List<TriggerState.SourceEnum> notificationSource)
        {
            notificationSource = new List<TriggerState.SourceEnum>();

            var cmd = "select IdExchangeTrigger, StatusTrigger, SourceTrigger from ExchangeTrigger " +
                      "where ProjectId in " +
                      "(select ProjectID from ExchangeProject where NeedExchange = 1 and SubObjectID = " +
                      $"(select top 1 IdSubObject from vPdmsProjects where Project = '{_сurrentProjectName}' and mdb = '{_сurrentMdbName}')) " +
                      $"and SourceTrigger <>'pdms' and IdExchangeTrigger >= {_triggerLastRowId} order by IdExchangeTrigger";

            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();

                if (sqlConnection.State == ConnectionState.Open)
                {
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        using (var reader = sqlCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var id = (int)reader["IdExchangeTrigger"];
                                var state = (byte)reader["StatusTrigger"];

                                _triggerLastRowId = id;

                                if (_triggers.ContainsKey(id))
                                {
                                    var trigger = _triggers[id];
                                    if (trigger.State == state)
                                        continue;

                                    trigger.State = state;

                                    if (trigger.State >= 2)
                                    {
                                        if (state == 2 && !notificationSource.Contains(trigger.Source))
                                            notificationSource.Add(trigger.Source);

                                        _triggers.Remove(id);
                                    }
                                }
                                else if (state < 2)
                                {
                                    _triggers.Add(id, new TriggerState(id, state, (string)reader["SourceTrigger"]));
                                }
                            }
                        }
                    }
                }

                sqlConnection.Close();
            }

            if (_triggers.Count > 0)
                _triggerLastRowId = Math.Min(_triggerLastRowId, _triggers.Keys.Min());
        }

        private static void ShowBaloon(TriggerState.SourceEnum source)
        {
            string iconName, notificationText, balloonText;
            switch (source)
            {
                case TriggerState.SourceEnum.Tekla:
                    iconName = "Images/tekla.png";
                    notificationText =
                        "Обновлена строительная часть из Tekla Structures. Для получения актуальных данных обновите модель.";
                    balloonText = "Уведомление Tekla Structures";
                    break;
                case TriggerState.SourceEnum.Revit:
                    iconName = "Images/revit.png";
                    notificationText =
                        "Обновлена строительная часть из Revit. Для получения актуальных данных обновите модель.";
                    balloonText = "Уведомление Revit";
                    break;
                case TriggerState.SourceEnum.MagiCAD:
                    iconName = "Images/magicad.png";
                    notificationText =
                        "Обновлена сантехническая часть из MagiCAD. Для получения актуальных данных обновите модель.";
                    balloonText = "Уведомление MagiCAD";
                    break;
                default:
                    return;
            }

            var balloon = new Balloon()
            {
                ImageSource = new BitmapImage(new Uri(iconName, UriKind.RelativeOrAbsolute)),
                MdbName = _сurrentMdbName,
                NotificationText = notificationText,
                ProjectName = _сurrentProjectName,
                BalloonText = balloonText
            };

            balloon.RaisePropertyChanged("ImageSource");
            balloon.RaisePropertyChanged("BalloonText");
            balloon.RaisePropertyChanged("MdbName");
            balloon.RaisePropertyChanged("NotificationText");
            balloon.RaisePropertyChanged("ProjectName");

            balloon.Show();
        }
    }
}
