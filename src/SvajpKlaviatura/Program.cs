using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SvajpKlaviatura
{
    internal static class Program
    {
        private const string Id = "SvajpKlaviatura-3f6a91c2";
        private static TrayApp app;
        private static readonly string[] Signals = { "show", "hide", "toggle", "settings", "quit" };

        internal sealed class Options
        {
            public string ConfigDir;
            public bool Quit, Autostart, Settings, Show, Hide, Toggle, Test;
            public string Error;
        }

        internal static Options ParseArgs(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                switch (a)
                {
                    case "--config-dir":
                        if (i + 1 < args.Length) o.ConfigDir = args[++i]; else o.Error = "После --config-dir нужна папка";
                        break;
                    case "--quit": o.Quit = true; break;
                    case "--settings": o.Settings = true; break;
                    case "--autostart": o.Autostart = true; break;
                    case "--show": o.Show = true; break;
                    case "--hide": o.Hide = true; break;
                    case "--toggle": o.Toggle = true; break;
                    case "--test": o.Test = true; break;
                    default: o.Error = "Неизвестный параметр: " + args[i]; break;
                }
            }
            return o;
        }

        private static string ResolveConfigDir(string requested)
        {
            foreach (string dir in new[]
            {
                requested,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SvajpKlaviatura"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SvajpKlaviatura"),
                Path.Combine(Path.GetTempPath(), "SvajpKlaviatura"),
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try { Directory.CreateDirectory(dir); return dir; } catch { }
            }
            return Path.GetTempPath();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnsureDpiAwareness();
            Options opt = ParseArgs(args);
            string configDir = ResolveConfigDir(opt.ConfigDir);
            Log.FilePath = Path.Combine(configDir, "log.txt");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                if (app != null) app.ReportCrash("интерфейс", e.Exception); else Log.Error("ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("UnhandledException", e.ExceptionObject as Exception);

            if (opt.Error != null)
            {
                Log.Write(opt.Error);
                try { if (Native.AttachConsole(-1)) Console.WriteLine(opt.Error); } catch { }
                return 2;
            }
            if (opt.Quit) { Signal("quit"); return 0; }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\" + Id, out createdNew))
            {
                if (!createdNew)
                {
                    if (opt.Autostart) return 0;
                    string sig = opt.Settings ? "settings" : opt.Hide ? "hide" : opt.Toggle ? "toggle" : "show";
                    if (!Signal(sig))
                        Dialogs.Warn("«Свайп-клавиатура» уже запущена, но не отвечает (возможно, она запущена от имени администратора).\n\nЗакройте её через значок в трее или Диспетчер задач и запустите снова.");
                    return 0;
                }

                var handles = new EventWaitHandle[Signals.Length];
                try
                {
                    for (int i = 0; i < Signals.Length; i++) handles[i] = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + "-" + Signals[i]);
                    try { app = new TrayApp(configDir, opt); }
                    catch (Exception ex)
                    {
                        Log.Error("Запуск", ex);
                        Dialogs.Error("«Свайп-клавиатура» не смогла запуститься: " + ErrorText.Friendly(ex) + "\n\nЖурнал: " + Log.FilePath);
                        return 1;
                    }
                    var waiter = new Thread(() =>
                    {
                        while (true)
                        {
                            int i;
                            try { i = WaitHandle.WaitAny(handles); } catch { return; }
                            switch (Signals[i])
                            {
                                case "show": app.Post(() => app.SetKeyboardVisible(true)); break;
                                case "hide": app.Post(() => app.SetKeyboardVisible(false)); break;
                                case "toggle": app.Post(app.ToggleKeyboard); break;
                                case "settings": app.Post(app.OpenSettings); break;
                                default: app.Post(app.ExitThread); return;
                            }
                        }
                    }) { IsBackground = true, Name = "signals" };
                    waiter.Start();
                    Application.Run(app);
                }
                finally { foreach (var h in handles) if (h != null) h.Dispose(); }
            }
            return 0;
        }

        private static bool Signal(string name)
        {
            try
            {
                try { Native.AllowSetForegroundWindow(-1); } catch { }
                using (var ev = EventWaitHandle.OpenExisting(@"Local\" + Id + "-" + name)) ev.Set();
                return true;
            }
            catch (Exception ex) { Log.Error("Сигнал " + name, ex); return false; }
        }
    }
}
