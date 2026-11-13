using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Timer = System.Windows.Forms.Timer;

namespace Polymetal.Revit.Client
{
    internal class WatcherAddin : IExternalApplication
    {
        private static List<PdmsModels> _projectModels = new List<PdmsModels>();
        private static Thread _timerThread;
        private static Timer _checkStatusTimer;
        private static bool _fileOpening;
        private static Balloon _balloon;
        private static string _connectionString = new sqlReference.Service1().GetStrConnectPdms();

        private static string _configPath = @"\\spbfs02\CAD\Revit\PolyDLL\files_copy.config";

        public Result OnStartup(UIControlledApplication a)
        {
            a.ControlledApplication.DocumentSaved += ControlledApplication_DocumentSaved;
            a.ControlledApplication.DocumentOpening += ControlledApplication_DocumentOpening;
            a.ControlledApplication.DocumentOpened += ControlledApplication_DocumentOpened;
            LoadPolyDll();
            _timerThread = new Thread(() =>
            {
                RunTimer();
                Dispatcher.Run();
            }) { Name = "WPF UI Thread" };
            _timerThread.SetApartmentState(ApartmentState.STA);
            _timerThread.Start();

            return Result.Succeeded;
        }

        void LoadPolyDll()
        {
            try
            {
                //MessageBox.Show("copy files");
                XmlSerializer formatter = new XmlSerializer(typeof(CopyPath[]));

                using (FileStream fs = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CopyPath[] newpaths = (CopyPath[])formatter.Deserialize(fs);

                    foreach (CopyPath path in newpaths)
                    {
                        ProcessStartInfo processInfo;
                        Process process;
                        processInfo = new ProcessStartInfo("cmd.exe", $"/C xcopy \"{path.Source}\" \"{path.Destination}\" /I /C /Y /Z /D /S")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        process = Process.Start(processInfo);
                        process.WaitForExit();

                        process.Close();
                    }
                }
            }
            catch (Exception e)
            {
                //MessageBox.Show(e.Message);
            }
        }

        void ControlledApplication_DocumentOpening(object sender, DocumentOpeningEventArgs e)
        {
            _fileOpening = true;
        }

        private void RunTimer()
        {
            _checkStatusTimer = new Timer { Interval = 10000 };
            _checkStatusTimer.Tick += _checkStatusTimer_Tick;
            _checkStatusTimer.Start();
        }

        void _checkStatusTimer_Tick(object sender, EventArgs e)
        {
            if (_fileOpening) return;

            for (var index = 0; index < _projectModels.Count; index++)
            {
                var pm = _projectModels[index];
                var curModels = pm.PdmsDirectory.GetFiles("*.dwg", SearchOption.TopDirectoryOnly).ToList();

                if (curModels.Count != pm.ModelsList.Count)
                {
                    pm.ModelsList.Clear();
                    pm.ModelsList.AddRange(curModels);
                    pm.LastModelsUpdate = DateTime.Now;
                }
                else
                {
                    var needUpdate = false;
                    for (var i = 0; i < curModels.Count;i++)
                    {
                        var cm = curModels[i];
                        var mm = pm.ModelsList[i];
                        if (cm.FullName.Equals(mm.FullName) && cm.LastWriteTime == mm.LastWriteTime) continue;
                        needUpdate = true;
                        break;
                    }
                    if (needUpdate)
                    {
                        pm.ModelsList.Clear();
                        pm.ModelsList.AddRange(curModels);
                        pm.LastModelsUpdate = DateTime.Now;
                    }
                }

                if ((DateTime.Now - pm.LastModelsUpdate).TotalMinutes < 1) continue;

                pm.LastModelsUpdate = DateTime.MaxValue;
                ShowBaloon(pm.PdmsProject, pm.MdbName);
            }
        }

        private static void ShowBaloon(string projectName, string mdbName)
        {
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

        void ControlledApplication_DocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            var docName = e.Document.PathName;
            if (e.Document.IsWorkshared && File.Exists(docName))
            {
                var bfiLocal = BasicFileInfo.Extract(e.Document.PathName);
                docName= bfiLocal.CentralPath;
            }

            docName = GetUNCPath(docName);

            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                decimal projectId = 0;
                var pdmsProject = string.Empty;
                var mdbName = string.Empty;
                var sqlCommand = "select " +
                                 "  ProjectID, " +
                                 "  Project, " +
                                 "  MDB " +
                                 "from " +
                                 "  ExchangeProject " +
                                 "join vPdmsProjects on vPdmsProjects.IdSubObject = SubObjectID " +
                                 "where " +
                                 "  Program = 'Revit' AND " +
                                 "  ModelPath LIKE '%" + docName + "%'";

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        projectId = decimal.Parse(reader["ProjectID"].ToString());
                        pdmsProject = reader["Project"].ToString();
                        mdbName = reader["MDB"].ToString();
                    }
                }
                if (projectId > 0)
                {
                    var pdmsDir = new DirectoryInfo(Path.Combine(Directory.GetParent(docName).FullName, "pdms"));

                    if (pdmsDir.Exists)
                    {
                        var modelFiles = pdmsDir.GetFiles("*.dwg", SearchOption.TopDirectoryOnly).ToList();
                        var pm = new PdmsModels()
                        {
                            DocumentName = docName,
                            PdmsDirectory = pdmsDir,
                            ModelsList = modelFiles,
                            LastModelsUpdate =  DateTime.MaxValue,
                            PdmsProject = pdmsProject,
                            MdbName = mdbName
                        };

                        var indexPm = -1;
                        for (var i = 0; i < _projectModels.Count; i++)
                        {
                            if (!_projectModels[i].DocumentName.Equals(docName)) continue;
                            indexPm = i;
                            break;
                        }

                        if (indexPm >= 0)
                        {
                            _projectModels.RemoveAt(indexPm);
                        }

                        _projectModels.Add(pm);
                    }
                }
                sqlConnection.Close();
            }
            _fileOpening = false;
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

        void ControlledApplication_DocumentSaved(object sender, DocumentSavedEventArgs e)
        {
            if (e?.Document == null) 
                return;

            if (e.Document.IsWorkshared && File.Exists(e.Document.PathName))
            {
                var bfiLocal = BasicFileInfo.Extract(e.Document.PathName);
                SendTrigger(bfiLocal.CentralPath);
            }
            else
            {
                SendTrigger(e.Document.PathName);
            }
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }

        private void SendTrigger(string fileName)
        {
            fileName = GetUNCPath(fileName);
            using (var sw = new StreamWriter(Path.Combine(Path.GetTempPath(), "revit_save.log"), true))
            {
                using (var sqlConnection = new SqlConnection(_connectionString))
                {
                    sqlConnection.Open();
                    var projectId = 0M;
                    var source = string.Empty;
                    var sqlCommand = "select ProjectID, Program from ExchangeProject where " +
                                     "ModelPath LIKE '%" + fileName + "%'";
                    sw.WriteLine(fileName);
                    using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                    {
                        var reader = sqlCmd.ExecuteReader();

                        if (reader.Read())
                        {
                            projectId = (decimal)reader["ProjectID"];
                            source = (string)reader["Program"];
                        }
                        reader.Close();
                    }
                    sw.WriteLine(projectId);
                    if (projectId > 0)
                    {
                        using (var sqlCmd = new SqlCommand("SetExchangeTrigger", sqlConnection))
                        {
                            sqlCmd.Parameters.AddWithValue("@UserLogin", Environment.UserName);
                            sqlCmd.Parameters.AddWithValue("@ProjectId", projectId);
                            sqlCmd.Parameters.AddWithValue("@SourceTrigger", source);
                            sqlCmd.Parameters.AddWithValue("@StatusTrigger", 0);

                            sqlCmd.CommandType = CommandType.StoredProcedure;
                            sqlCmd.ExecuteNonQuery();
                        }
                    }
                    sqlConnection.Close();
                }
            }
        }
    }

    internal class PdmsModels
    {
        internal string DocumentName;
        internal DirectoryInfo PdmsDirectory;
        internal List<FileInfo> ModelsList;
        internal DateTime LastModelsUpdate;
        internal string PdmsProject;
        internal string MdbName;
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
        {
        }
    }
}
