using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v2021
{
    class RunTekla
    {
        internal static string TeklaProcessName { get { return "TeklaStructures"; } }
        internal static string ExePath { get { return @"C:\\TeklaStructures\\2021.0\\nt\\bin\\TeklaStructures.exe"; } }
        internal static string ExeArguments { get { return "-I \"C:\\TeklaStructures\\2021.0\\Environments\\PolymetalUser.ini\""; } }
        internal static ProjectInformation ProjectInfo;
        internal static Common.ExchangeType ExchangeType;
        internal static int TriggerId;
        internal static DateTime TaskDateTime;

        static int Main(string[] args)
        {
            //ProjectInfo = new ProjectInformation(22);
            //Export.Run();
            //return 0;

            /* возвращаемые значение:
             * 0 - не выполнено
             * 1 - выполнено
             * 2 - не нуждается в выгрузке
             * 3 - ошибка инициализации
             */

            if (args.Length == 0)
            {
                args = new string[1];
                Console.WriteLine("Enter TriggerID");
                args[0] = Console.ReadLine();
            }
            if (args.Length < 1 || !int.TryParse(args[0], out TriggerId))
            {
                return 3;
            }
            if (!Common.GetTriggerInfoById(TriggerId, out decimal projectId, out Common.Source source, out decimal status)) return 3;
            TaskDateTime = DateTime.Now;
            ProjectInfo = new ProjectInformation(projectId);
            if (ProjectInfo.ProjectId == 0) return 3;
            if (!ProjectInfo.NeedExchange) return 2;

            if (source == Common.Source.Tekla && status == 0)
            {
                ExchangeType = Common.ExchangeType.Export;
            }
            else if (source == Common.Source.Pdms && status == 1)
            {
                ExchangeType = Common.ExchangeType.Import;
                //TODO:отмена импорта опорных моделей по BKR
                if (projectId == 10 || projectId == 12 || projectId == 13)
                    return 1;
            }
            else
            {
                return 3;
            }

            var done = Standalone.Run();
            return done ? 1 : 0;
        }
    }

    class Standalone
    {
        internal static Process TeklaProcess = null;
        private static string[] _windowNames = { "Открыть", "Tekla Structures", "Tekla Structures - удалить ненужные файлы чертежей?" };
        #region работа с окнами
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;

        private static string GetWindowText(IntPtr hWnd)
        {
            var len = GetWindowTextLength(hWnd) + 1;
            var sb = new StringBuilder(len);
            len = GetWindowText(hWnd, sb, len);
            return sb.ToString(0, len);
        }

        #endregion

        internal static bool Run()
        {
            if (!StartProcess())
            {
                SendMessage("Не удалось запустить процесс " + RunTekla.TeklaProcessName);
                return false;
            }
            
            bool exchangeResult;
            if (RunTekla.ExchangeType == Common.ExchangeType.Export)
            {
                Console.WriteLine("Выполняется экспорт из модели");
                exchangeResult = Export.Run();
                Console.WriteLine("Экспорт модели завершен " + (exchangeResult ? "полностью" : "с ошибками"));
            }
            else
            {
                Console.WriteLine("Выполняется импорт опорных моделей");
                exchangeResult = Import.Run();
                Console.WriteLine("Импорт моделей завершен полностью, сохранение {0}",exchangeResult?"требуется": "не требуется");
            }

            return CloseProcess(RunTekla.ExchangeType == Common.ExchangeType.Import && exchangeResult);
        }
        #region Работа с процессом

        private static bool StartProcess()
        {
            var checkStatusTimer = new Timer(CheckStatusTimer, true, 60 * 60 * 1000, 1000);
            while (IsTeklaRunning())
            {
                Console.WriteLine("Предыдущий процесс " + RunTekla.TeklaProcessName + " еще не завершен");
                Thread.Sleep(2000);
            }

            Console.WriteLine("Запускаю процесс {0} с моделью {1}", RunTekla.TeklaProcessName,
                RunTekla.ProjectInfo.Program.Path);

            if (string.IsNullOrEmpty(RunTekla.ProjectInfo.Program.Path) || !Directory.Exists(RunTekla.ProjectInfo.Program.Path))
            {
                Console.WriteLine("Не корректно указан путь к модели [" + RunTekla.ProjectInfo.Program.Path + "]");
                return false;
            }

            TeklaProcess = new Process
            {
                StartInfo = new ProcessStartInfo(RunTekla.ExePath, $"\"{RunTekla.ProjectInfo.Program.Path}\" {RunTekla.ExeArguments}")
            };

            if (!TeklaProcess.Start()) return false;

            var sw = new Stopwatch();
            sw.Start();
            while (!IsTeklaRunning() || TeklaProcess.MainWindowHandle == IntPtr.Zero || !TeklaProcess.Responding || !TeklaProcess.MainWindowTitle.Contains("Tekla Structures"))
            {
                Thread.Sleep(1000);
                if (sw.Elapsed.TotalMinutes <= 10) continue;
                TeklaProcess.Kill();
                TeklaProcess = null;
                SendMessage("Процесс " + RunTekla.TeklaProcessName + " убит (не отвечает)");
                return false;
            }

            var isOpened = IsModelOpened(out bool badModelPath);
            if (!isOpened && badModelPath) return false;

            sw.Restart();

            while (!isOpened)
            {
                Thread.Sleep(1000);
                if (sw.Elapsed.TotalMinutes <= 10)
                {
                    isOpened = IsModelOpened(out badModelPath);
                    continue;
                }
                TeklaProcess.Kill();
                TeklaProcess = null;
                SendMessage("Процесс " + RunTekla.TeklaProcessName + " убит (не отвечает)");
                return false;
            }

            SendMessage("Процесс запущен");
            checkStatusTimer.Dispose();
            return true;
        }
        
        private static void CheckStatusTimer(object state)
        {
            var teklas = Process.GetProcessesByName(RunTekla.TeklaProcessName);
            foreach (var proc in teklas)
                proc.Kill();
            Process.GetCurrentProcess().Kill();
        }

        static bool IsTeklaRunning()
        {
            var teklas = Process.GetProcessesByName(RunTekla.TeklaProcessName);
            if (teklas.Length == 0) return false;

            TeklaProcess = teklas[0];
            return true;
        }

        static bool IsModelOpened(out bool badModelPath)
        {            
            var badModel = false;
            var res = true;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowTextLength(hWnd) == 0) return true;

                var text = GetWindowText(hWnd);
                if (text.Contains((_windowNames)[0]))
                {
                    var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&OK");
                    if (btnWin == IntPtr.Zero) return true;
                    Console.WriteLine("Отсутствует файл для открытия модели");
                    SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                    SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                    badModel = true;
                    res = false;
                }
                else if (text.Contains((_windowNames)[1]))
                {
                    if (text.Contains((_windowNames)[2]))
                    {
                        var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Нет");
                        if (btnWin == IntPtr.Zero) return true;
                        Console.WriteLine("Удаление ненужных чертежей");
                        SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                        SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                        res = false;
                    }
                    else
                    {
                        var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Да");
                        if (btnWin == IntPtr.Zero) return true;
                        Console.WriteLine("Модель заблокирована, ожидание");
                        var btnWinNo = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Нет");
                        SendMessage(btnWinNo, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                        SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                        SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                        res = false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            badModelPath = badModel;
            var connected = true;
            if (res)
            {
                try
                {
                    var model = new Model();
                    var objects = model.GetModelObjectSelector().GetAllObjects();
                    connected = objects.GetSize() > 0;
                }
                catch {
                    return false;
                }
            }
            return res && connected;
        }

        internal static bool CloseProcess(bool save)
        {
            var handler = new ModelHandler();
            if (save && !handler.IsModelSaved())
            {
                handler.Save("Import reference models");
                handler.Close();
                TeklaProcess.CloseMainWindow();
            }
            else
            {
                handler.Close();
                TeklaProcess.CloseMainWindow();
            }

            var sw = new Stopwatch();
            sw.Start();
            while (IsTeklaRunning() && sw.Elapsed.TotalSeconds <= 30*60)
            {
                if (sw.Elapsed.TotalSeconds%10 < 0.0001)
                    Console.WriteLine("{0}\tПриложение {1} в процессе закрытия", Math.Round(sw.Elapsed.TotalSeconds),
                        RunTekla.TeklaProcessName);
            }
            sw.Stop();

            if (!IsTeklaRunning())
            {
                Console.WriteLine(
                    "Приложение " + RunTekla.TeklaProcessName + " успешно завершено " +
                    (save ? "с сохранением" : " без сохранения"), true);
                TeklaProcess = null;
                return true;
            }

            TeklaProcess = null;
            return false;
        }

        #endregion

        internal static void SendMessage(string message)
        {
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, message);
        }
    }
}
