using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SvajpKlaviatura
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly string configDir, settingsPath;
        private AppSettings settings;
        private readonly Dictionaries dicts;
        private readonly KeyboardForm kb;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem miShow, miAutostart, miMode;
        private readonly Control invoker;
        private readonly HotkeyWindow hotkey;
        private SettingsForm settingsForm;
        private DictionaryForm dictForm;
        private readonly Timer saveTimer = new Timer { Interval = 1500 };
        private DateTime lastCrashNote = DateTime.MinValue;

        public TrayApp(string configDir, Program.Options opt)
        {
            this.configDir = configDir;
            settingsPath = Path.Combine(configDir, "settings.ini");
            settings = AppSettings.Load(settingsPath);

            invoker = new Control();
            invoker.CreateControl();
            var h = invoker.Handle;

            dicts = new Dictionaries(configDir);
            dicts.Load();

            kb = new KeyboardForm(settings, dicts, configDir, opt.Test);
            kb.SettingsRequested += OpenSettings;
            kb.HideRequested += () => SetKeyboardVisible(false);
            kb.SettingsChanged += () => { settings = MergeFromKeyboard(); saveTimer.Stop(); saveTimer.Start(); };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); TrySaveSettings(); };

            menu = new ContextMenuStrip { ShowImageMargin = false };
            miShow = new ToolStripMenuItem("Показать клавиатуру", null, (s, e) => ToggleKeyboard()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            var miSize = new ToolStripMenuItem("Размер");
            miSize.DropDownItems.Add("Маленькая", null, (s, e) => Preset(0.42));
            miSize.DropDownItems.Add("Средняя", null, (s, e) => Preset(0.62));
            miSize.DropDownItems.Add("Большая", null, (s, e) => Preset(0.82));
            miSize.DropDownItems.Add("Во всю ширину экрана", null, (s, e) => Preset(1.0));
            miMode = new ToolStripMenuItem("Свайп без зажатия кнопки (для тачпада)", null, (s, e) => Safe("Режим", ToggleMode));
            miAutostart = new ToolStripMenuItem("Запускать вместе с Windows", null, (s, e) => Safe("Автозапуск", ToggleAutostart));
            menu.Items.Add(miShow);
            menu.Items.Add(miSize);
            menu.Items.Add(miMode);
            menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("Мой словарь…", null, (s, e) => OpenDictionary()));
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripMenuItem("Открыть журнал", null, (s, e) => Safe("Журнал", OpenLog)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, (s, e) => ExitThread()));
            menu.Opening += (s, e) =>
            {
                miShow.Text = kb.Visible ? "Скрыть клавиатуру\t" + settings.Hotkey : "Показать клавиатуру\t" + settings.Hotkey;
                miAutostart.Checked = Autostart.IsEnabled() && !Autostart.IsDisabledByWindows();
                miMode.Checked = kb.CurrentSettings.Mode == SwipeMode.Click;
            };

            tray = new NotifyIcon { Icon = LoadAppIcon(SystemInformation.SmallIconSize.Width), ContextMenuStrip = menu, Text = "Свайп-клавиатура", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleKeyboard(); };

            hotkey = new HotkeyWindow(() => Post(ToggleKeyboard));
            RegisterHotkey();

            invoker.BeginInvoke(new Action(() => Safe("Запуск", () => Startup(opt))));
        }

        public static Icon LoadAppIcon(int size)
        {
            using (var st = typeof(TrayApp).Assembly.GetManifestResourceStream("SvajpKlaviatura.app.ico"))
                return new Icon(st, new Size(size, size));
        }

        public void Post(Action a)
        {
            try { invoker.BeginInvoke(new Action(() => Safe("команда", a))); } catch (Exception ex) { Log.Error("Post", ex); }
        }

        private void Startup(Program.Options opt)
        {
            Log.Write("Запуск. Версия " + Application.ProductVersion + ", exe: " + Application.ExecutablePath + ", слежение за щелчками: " + (kb.WatcherInstalled ? "да" : "нет") + (opt.Test ? ", режим теста" : ""));
            if (settings.LoadWarnings.Count > 0)
            {
                Balloon("Настройки исправлены", string.Join("\n", settings.LoadWarnings), ToolTipIcon.Warning);
                TrySaveSettings();
            }
            Autostart.RefreshPath(Application.ExecutablePath);
            bool show = opt.Hide ? false : opt.Show ? true : opt.Autostart ? settings.ShowOnAutostart : settings.ShowOnStart;
            if (show) SetKeyboardVisible(true);
            else Balloon("Свайп-клавиатура работает", "Показать клавиатуру: " + settings.Hotkey + " или щелчок по значку.", ToolTipIcon.Info);
            if (opt.Settings) OpenSettings();
        }

        private AppSettings MergeFromKeyboard()
        {
            var k = kb.CurrentSettings;
            return k;
        }

        private void RegisterHotkey()
        {
            uint mods, vk;
            if (!HotkeyText.TryParse(settings.Hotkey, out mods, out vk)) return;
            string err = hotkey.Register(mods, vk);
            if (err != null)
            {
                Log.Write("Горячая клавиша " + settings.Hotkey + ": " + err);
                Balloon("Сочетание " + settings.Hotkey + " занято", "Другая программа уже использует " + settings.Hotkey + ". Выберите другое сочетание в настройках; клавиатуру можно открыть щелчком по значку.", ToolTipIcon.Warning);
            }
        }

        public void ToggleKeyboard() { SetKeyboardVisible(!kb.Visible); }

        public void SetKeyboardVisible(bool v)
        {
            if (v) kb.ShowKeyboard(); else kb.HideKeyboard();
            tray.Text = v ? "Свайп-клавиатура — открыта" : "Свайп-клавиатура — скрыта (" + settings.Hotkey + ")";
        }

        public void ShowRunning()
        {
            SetKeyboardVisible(true);
        }

        private void Preset(double f)
        {
            if (!kb.Visible) SetKeyboardVisible(true);
            kb.SetSizePreset(f);
        }

        private void ToggleMode()
        {
            var s = kb.CurrentSettings;
            s.Mode = s.Mode == SwipeMode.Click ? SwipeMode.Hold : SwipeMode.Click;
            kb.ApplySettings(s);
            settings = s;
            TrySaveSettings();
        }

        public void OpenSettings()
        {
            Safe("Настройки", () =>
            {
                if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
                using (DpiScope.SystemAware())
                {
                    var f = new SettingsForm(kb.CurrentSettings, Autostart.IsEnabled() && !Autostart.IsDisabledByWindows(), s => kb.ApplySettings(s));
                    settingsForm = f;
                    var before = kb.CurrentSettings;
                    f.FormClosed += (o, e) =>
                    {
                        settingsForm = null;
                        if (f.DialogResult == DialogResult.OK && f.Result != null)
                        {
                            string oldHotkey = settings.Hotkey;
                            settings = f.Result;
                            kb.ApplySettings(settings);
                            TrySaveSettings();
                            if (settings.Hotkey != oldHotkey) RegisterHotkey();
                            bool on = Autostart.IsEnabled() && !Autostart.IsDisabledByWindows();
                            if (f.AutostartResult != on) SetAutostart(f.AutostartResult);
                        }
                        else kb.ApplySettings(before);
                    };
                    f.Show();
                    f.Activate();
                }
            });
        }

        public void OpenDictionary()
        {
            Safe("Словарь", () =>
            {
                if (dictForm != null && !dictForm.IsDisposed) { dictForm.Activate(); return; }
                using (DpiScope.SystemAware())
                {
                    dictForm = new DictionaryForm(dicts, kb.CurrentSettings.Language);
                    dictForm.FormClosed += (o, e) => { dictForm = null; dicts.SaveIfChanged(); };
                    dictForm.Show();
                    dictForm.Activate();
                }
            });
        }

        private void TrySaveSettings()
        {
            try { settings.Save(settingsPath); }
            catch (Exception ex)
            {
                Log.Error("Сохранение настроек", ex);
                Balloon("Настройки не сохранены", "Не удалось записать «" + settingsPath + "»: " + ErrorText.Friendly(ex), ToolTipIcon.Warning);
            }
        }

        private void ToggleAutostart() { SetAutostart(!(Autostart.IsEnabled() && !Autostart.IsDisabledByWindows())); }

        private void SetAutostart(bool on)
        {
            try
            {
                if (on)
                {
                    string warn = Autostart.RiskyLocationWarning(Application.ExecutablePath, Path.GetTempPath(), Autostart.IsRemovable);
                    if (warn != null) { Dialogs.Warn(warn); return; }
                    Autostart.Enable(Application.ExecutablePath);
                    Balloon("Автозапуск включён", "Клавиатура будет запускаться вместе с Windows (значок в трее, " + settings.Hotkey + " — показать).", ToolTipIcon.Info);
                }
                else
                {
                    Autostart.Disable();
                    Balloon("Автозапуск выключен", "Программа больше не будет запускаться вместе с Windows.", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Автозапуск", ex);
                Balloon("Автозапуск не изменён", ErrorText.Friendly(ex), ToolTipIcon.Error);
            }
        }

        private void OpenLog()
        {
            if (!File.Exists(Log.FilePath)) Log.Write("Журнал создан");
            ShellOpen.Open(Log.FilePath);
        }

        private void Balloon(string title, string text, ToolTipIcon icon)
        {
            try { tray.ShowBalloonTip(6000, title, text.Length > 250 ? text.Substring(0, 250) + "…" : text, icon); }
            catch (Exception ex) { Log.Error("Уведомление", ex); }
        }

        private void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { ReportCrash(what, ex); }
        }

        public void ReportCrash(string what, Exception ex)
        {
            Log.Error(what, ex);
            if (invoker.InvokeRequired) { Post(() => ReportCrash(what, null)); return; }
            if ((DateTime.Now - lastCrashNote).TotalSeconds < 30) return;
            lastCrashNote = DateTime.Now;
            Balloon("Что-то пошло не так", "«" + what + "»: " + (ex == null ? "ошибка" : ErrorText.Friendly(ex)) + " Программа продолжает работать. Подробности — в журнале (меню значка).", ToolTipIcon.Error);
        }

        protected override void ExitThreadCore()
        {
            try { if (settingsForm != null) settingsForm.Close(); } catch { }
            try { if (dictForm != null) dictForm.Close(); } catch { }
            try { settings = kb.CurrentSettings; TrySaveSettings(); } catch { }
            try { dicts.SaveIfChanged(); } catch { }
            try { hotkey.Dispose(); } catch { }
            try { kb.Close(); kb.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            Log.Write("Выход");
            base.ExitThreadCore();
        }
    }

    /// <summary>Невидимое окно для RegisterHotKey.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action onHotkey;
        private bool registered;
        private const int Id = 0x5A17;

        public HotkeyWindow(Action onHotkey)
        {
            this.onHotkey = onHotkey;
            CreateHandle(new CreateParams { Caption = "SvajpKlaviatura-hotkey", Parent = new IntPtr(-3) /*HWND_MESSAGE*/ });
        }

        public string Register(uint mods, uint vk)
        {
            if (registered) { Native.UnregisterHotKey(Handle, Id); registered = false; }
            if (Native.RegisterHotKey(Handle, Id, mods | Native.MOD_NOREPEAT, vk)) { registered = true; return null; }
            return "не зарегистрирована, код " + System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Id) { try { onHotkey(); } catch (Exception ex) { Log.Error("Горячая клавиша", ex); } return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (registered) try { Native.UnregisterHotKey(Handle, Id); } catch { }
            DestroyHandle();
        }
    }
}
