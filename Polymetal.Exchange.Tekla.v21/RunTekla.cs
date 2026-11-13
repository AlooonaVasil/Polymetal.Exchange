using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Polymetal.Exchange.Utilities;
using Tekla.Structures;
using Tekla.Structures.Model.Operations;

namespace Polymetal.Exchange.Tekla.v21
{
    class RunTekla
    {
        internal static string TeklaProcessName { get { return "TeklaStructures"; } }
        internal static string ExePath { get { return @"C:\\TeklaStructures\\21.0\\nt\\bin\\TeklaStructures.exe"; } }
        internal static string ExeArguments { get { return "-I \"C:\\TeklaStructures\\21.0\\Environments\\PolymetalUser.ini\""; } }
        internal static ProjectInformation ProjectInfo;
        internal static Common.ExchangeType ExchangeType;
        internal static int TriggerId;

        static int Main(string[] args)
        {
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
            decimal projectId, status;
            Common.Source source;
            if (!Common.GetTriggerInfoById(TriggerId, out projectId, out source, out status)) return 3;

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
        private static bool badModelPath = false;
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
            var done = StartProcess(true);

            if (!done)
            {
                SendMessage("Не удалось запустить процесс " + RunTekla.TeklaProcessName);
                return false;
            }

            Console.WriteLine("Открытие модели " + RunTekla.ProjectInfo.Program.Path);
            done = OpenModel();
            if (!done)
            {
                SendMessage("Не удалось открыть модель " + RunTekla.ProjectInfo.Program.Path);
                return false;
            }

            var xsProject = string.Empty;
            //TeklaStructuresSettings.GetAdvancedOption("XS_PROJECT", ref xsProject);
            //SendMessage("XS_PROJECT="+(string.IsNullOrWhiteSpace(xsProject)?"EMPTY":xsProject));
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
                Console.WriteLine("Импорт моделей завершен " + (exchangeResult ? "полностью" : "с ошибками"));
            }

            done = CloseProcess(RunTekla.ExchangeType == Common.ExchangeType.Import && exchangeResult);

            return done;
        }

        #region Работа с процессом

        private static bool StartProcess(bool firstTry)
        {
            if (IsTeklaRunning())
            {
                var sw1 = new Stopwatch();
                sw1.Start();
                Console.WriteLine("Предыдущий процесс " + RunTekla.TeklaProcessName + " еще не завершен");
                while (IsTeklaRunning() && sw1.Elapsed.TotalSeconds < 60)
                {
                    Thread.Sleep(2000);
                }
                if (IsTeklaRunning())
                {
                    SendMessage("Пытаюсь убить ранее запущенный процесс " + RunTekla.TeklaProcessName);
                    TeklaProcess.Kill();
                    Thread.Sleep(2000);
                    var killed = !IsTeklaRunning();
                    SendMessage("Процесс " + RunTekla.TeklaProcessName + (killed ? " убит" : " не убит"));
                    if (!killed) return false;
                }
                //started = true;
                //SendMessage("Присоединяюсь к запущенному процессу " + RunTekla.TeklaProcessName);
                sw1.Stop();
            }

            Console.WriteLine("Запускаю процесс " + RunTekla.TeklaProcessName);
            TeklaProcess = new Process {StartInfo = new ProcessStartInfo(RunTekla.ExePath, RunTekla.ExeArguments)};
            var started = TeklaProcess.Start();

            var sw = new Stopwatch();
            sw.Start();

            while (TeklaProcess.MainWindowHandle == IntPtr.Zero)
            {
                Thread.Sleep(1000);
                if (sw.Elapsed.TotalMinutes <= 2) continue;
                sw.Stop();
                started = false;
                TeklaProcess.Kill();
                TeklaProcess = null;
                SendMessage("Программа " + RunTekla.TeklaProcessName + " не запустилась");
                break;
            }

            if (firstTry && !started)
            {
                SendMessage("Повторно запускаю процесс " + RunTekla.TeklaProcessName);
                started = StartProcess(false);
            }

            if (started)
            {
                sw.Reset();
                sw.Start();
                while (!TeklaProcess.Responding)
                {
                    Thread.Sleep(1000);
                    if (sw.Elapsed.TotalMinutes <= 2) continue;
                    started = false;
                    TeklaProcess.Kill();
                    TeklaProcess = null;
                    SendMessage("Процесс " + RunTekla.TeklaProcessName + " убит (не отвечает)");
                    break;
                }
                SendMessage("Процесс " + RunTekla.TeklaProcessName + " отвечает");
            }

            sw.Reset();
            sw.Start();
            while (!IsTeklaRunning() || !TeklaProcess.MainWindowTitle.Contains("Tekla Structures"))
            {
                Thread.Sleep(1000);
                if (sw.Elapsed.TotalMinutes <= 2) continue;
                started = false;
                TeklaProcess.Kill();
                TeklaProcess = null;
                break;
            }

            return started;
        }

        static bool IsTeklaRunning()
        {
            var teklas = Process.GetProcessesByName(RunTekla.TeklaProcessName);
            if (teklas.Length == 0) return false;

            TeklaProcess = teklas[0];
            return true;
        }

        internal static bool CloseProcess(bool save)
        {
            var macro = save ? @"Tekla_Close_Save.cs" : @"Tekla_Close_NotSave.cs";
            var xsMacro = string.Empty;
            TeklaStructuresSettings.GetAdvancedOption("XS_MACRO_DIRECTORY", ref xsMacro);
            xsMacro = xsMacro.Split(';').First();
            if (Directory.Exists(xsMacro))
            {
                var macroPath = Path.Combine(xsMacro, macro);
                if (!File.Exists(macroPath))
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.Tekla.v211.Resources." + macro))
                    {
                        if (stream != null)
                        {
                            var bytes = new byte[(int)stream.Length];
                            stream.Read(bytes, 0, bytes.Length);
                            File.WriteAllBytes(macroPath, bytes);
                        }
                    }
                }
            }

            Thread.Sleep(1000);
            var timer = new Timer(CheckModelState, _windowNames, 2000, 2000);
            try
            {              
                Operation.RunMacro("..\\" + macro);
            }
            catch
            {
                SendMessage("Не удалось закрыть приложение " + RunTekla.TeklaProcessName + " с помощью макроса");
            }
            timer.Dispose();
            var sw = new Stopwatch();
            sw.Start();
            while (IsTeklaRunning() && sw.Elapsed.TotalSeconds <= 300)
            {
                Console.WriteLine("Приложение " + RunTekla.TeklaProcessName + " в процессе закрытия");
                Thread.Sleep(1000);
            }
            sw.Stop();

            if (!IsTeklaRunning())
            {
                Console.WriteLine("Приложение " + RunTekla.TeklaProcessName + " успешно завершено " + (save ? "с сохранением" : " без сохранения"), true);
                TeklaProcess = null;
                return true;
            }

            TeklaProcess.CloseMainWindow();
            var tsWin = FindWindow(null, "Выход");
            if (tsWin == IntPtr.Zero)
            {
                Thread.Sleep(2000);
                tsWin = FindWindow(null, "Выход");
            }

            if (tsWin != IntPtr.Zero)
            {
                var btnWin = FindWindowEx(tsWin, IntPtr.Zero, "Button", save ? "&Сохранить" : "&Не сохранять");
                if (btnWin != IntPtr.Zero)
                {
                    SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                    SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);

                    if (save)
                    {
                        tsWin = FindWindow(null, "Комментарий к редакции модели");
                        if (tsWin == IntPtr.Zero)
                        {
                            Thread.Sleep(1000);
                            tsWin = FindWindow(null, "Комментарий к редакции модели");
                        }

                        if (tsWin != IntPtr.Zero)
                        {
                            var chWin = FindWindowEx(tsWin, IntPtr.Zero, "Static", "");
                            if (chWin != IntPtr.Zero)
                            {
                                btnWin = FindWindowEx(chWin, IntPtr.Zero, "Button", "&OK");
                                if (btnWin != IntPtr.Zero)
                                {
                                    SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                                    SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                                }
                            }
                        }
                    }
                }
            }

            Thread.Sleep(2000);
            if (!IsTeklaRunning())
            {
                Console.WriteLine("Приложение " + RunTekla.TeklaProcessName + " успешно завершено " + (save ? "с сохранением" : " без сохранения") + " c помощью WinApi", true);
                TeklaProcess = null;
                return true;
            }


            SendMessage("Не удалось закрыть приложение " + RunTekla.TeklaProcessName + " с помощью WinApi");
            try
            {
                TeklaProcess.Kill();
                SendMessage("Приложение " + RunTekla.TeklaProcessName + " было завершено аварийно");
                TeklaProcess = null;
                return true;
            }
            catch (Exception exc)
            {
                SendMessage("[TeklaStop] " + exc.Message);
            }

            //процесс чудом выжил
            SendMessage("Приложение Tekla Structures бессмертно");
            TeklaProcess = null;
            return false;
        }

        #endregion

        #region Открытие модели
        static bool OpenModel()
        {
            if (String.IsNullOrEmpty(RunTekla.ProjectInfo.Program.Path) || !Directory.Exists(RunTekla.ProjectInfo.Program.Path))
            {
                Console.WriteLine("Не корректно указан путь к модели [" + RunTekla.ProjectInfo.Program.Path + "]");
                return false;
            }

            bool opened;
            var timer = new Timer(CheckModelState, _windowNames, 2000, 2000);
            try
            {
                opened = Operation.Open(RunTekla.ProjectInfo.Program.Path);
            }
            catch (Exception exc)
            {
                opened = false;
                SendMessage("[OpenModel]" + exc.Message);
                SendMessage(RunTekla.ProjectInfo.Program.Path);
            }
            timer.Dispose();
            return !badModelPath && opened;
        }

        private static void CheckModelState(Object windowNames)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowTextLength(hWnd) != 0)
                {
                    var text = GetWindowText(hWnd);
                    if (text.Contains(((string[])windowNames)[0]))
                    {
                        var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&OK");
                        if (btnWin != IntPtr.Zero)
                        {
                            SendMessage("Отсутствует файл для открытия модели");
                            SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                            SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                            badModelPath = true;
                        }
                    }
                    else if (text.Contains(((string[])windowNames)[1]))
                    {
                        if (text.Contains(((string[])windowNames)[2]))
                        {
                            var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Нет");
                            if (btnWin != IntPtr.Zero)
                            {
                                SendMessage("Удаление ненужных чертежей");
                                SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                                SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                            }
                        }
                        else
                        {
                            var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Да");
                            if (btnWin != IntPtr.Zero)
                            {
                                SendMessage("Модель заблокирована, ожидание");
                                SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                                SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        #endregion

        internal static void SendMessage(string message)
        {
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, message);
        }
    }
}