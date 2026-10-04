using System;
using System.Drawing;
using System.Windows.Forms;

namespace SvajpKlaviatura
{
    /// <summary>Окно настроек. Прозрачность и тема видны сразу; «Отмена» возвращает как было.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings original;
        private readonly Action<AppSettings> preview;
        private bool loading;

        private RadioButton rbHold, rbClick, rbRu, rbEn, rbDark, rbLight;
        private TrackBar tbOpacity;
        private Label lblOpacity;
        private CheckBox chkTrail, chkPreview, chkCaps, chkPunct, chkProfanity, chkLearn, chkShowOnStart, chkShowOnAutostart, chkAutostart;
        private ComboBox cbHotkey;

        public AppSettings Result { get; private set; }
        public bool AutostartResult { get; private set; }

        public static readonly string[] HotkeyChoices = { "Ctrl+Alt+K", "Ctrl+Alt+Space", "Ctrl+Shift+K", "Ctrl+Alt+F9", "Win+Alt+K" };

        public SettingsForm(AppSettings current, bool autostart, Action<AppSettings> preview)
        {
            original = current.Clone();
            this.preview = preview;
            Text = "Свайп-клавиатура — настройки";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);
            try { Icon = TrayApp.LoadAppIcon(32); } catch { }

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            Controls.Add(root);

            TableLayoutPanel t;
            var g1 = Group("Как набирать слово", out t);
            rbHold = Radio("Зажать кнопку мыши на первой букве и вести (как пальцем на смартфоне)"); Span(t, rbHold);
            rbClick = Radio("Без зажатия: щелчок на первой букве, вести, щелчок в конце (удобно на тачпаде)"); Span(t, rbClick);
            chkTrail = Check("Показывать след свайпа"); Span(t, chkTrail);
            chkPreview = Check("Показывать угадываемое слово во время свайпа"); Span(t, chkPreview);
            root.Controls.Add(g1);

            var g2 = Group("Текст", out t);
            var langPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            rbRu = new RadioButton { Text = "Русский", AutoSize = true };
            rbEn = new RadioButton { Text = "English", AutoSize = true };
            langPanel.Controls.Add(new Label { Text = "Язык:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            langPanel.Controls.Add(rbRu); langPanel.Controls.Add(rbEn);
            Span(t, langPanel);
            chkCaps = Check("Заглавная буква в начале предложения"); Span(t, chkCaps);
            chkPunct = Check("Умная пунктуация: «слово, слово.» — пробел после знака ставится сам"); Span(t, chkPunct);
            chkLearn = Check("Запоминать мои слова (чаще предлагать то, что я набираю)"); Span(t, chkLearn);
            chkProfanity = Check("Не предлагать бранные слова"); Span(t, chkProfanity);
            root.Controls.Add(g2);

            var g3 = Group("Окно", out t);
            var op = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            op.Controls.Add(new Label { Text = "Непрозрачность:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
            tbOpacity = new TrackBar { Minimum = AppSettings.OpacityMin, Maximum = AppSettings.OpacityMax, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 220 };
            lblOpacity = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
            op.Controls.Add(tbOpacity); op.Controls.Add(lblOpacity);
            Span(t, op);
            var th = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            rbDark = new RadioButton { Text = "Тёмная", AutoSize = true };
            rbLight = new RadioButton { Text = "Светлая", AutoSize = true };
            th.Controls.Add(new Label { Text = "Тема:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            th.Controls.Add(rbDark); th.Controls.Add(rbLight);
            Span(t, th);
            var hk = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            hk.Controls.Add(new Label { Text = "Показать/скрыть клавиатуру:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            cbHotkey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cbHotkey.Items.AddRange(HotkeyChoices);
            hk.Controls.Add(cbHotkey);
            Span(t, hk);
            Span(t, new Label { Text = "Размер — потяните за правый нижний угол клавиатуры или выберите в меню значка.\nПередвинуть — за верхнюю полосу.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 2) });
            root.Controls.Add(g3);

            var g4 = Group("Запуск", out t);
            chkShowOnStart = Check("Показывать клавиатуру при запуске программы"); Span(t, chkShowOnStart);
            chkAutostart = Check("Запускать вместе с Windows"); Span(t, chkAutostart);
            chkShowOnAutostart = Check("…и сразу показывать клавиатуру (иначе — только значок в трее)"); Span(t, chkShowOnAutostart);
            root.Controls.Add(g4);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
            var btnDefaults = new Button { Text = "По умолчанию", AutoSize = true, MinimumSize = new Size(110, 0) };
            var btnOk = new Button { Text = "ОК", AutoSize = true, MinimumSize = new Size(90, 0) };
            var btnCancel = new Button { Text = "Отмена", AutoSize = true, MinimumSize = new Size(90, 0) };
            btnDefaults.Click += (s, e) =>
            {
                var d = new AppSettings { X = original.X, Y = original.Y, W = original.W, H = original.H };
                LoadValues(d, chkAutostart.Checked); Changed();
            };
            btnOk.Click += (s, e) => { Result = Collect(); AutostartResult = chkAutostart.Checked; DialogResult = DialogResult.OK; Close(); };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(btnDefaults); buttons.Controls.Add(btnOk); buttons.Controls.Add(btnCancel);
            root.Controls.Add(buttons);
            AcceptButton = btnOk; CancelButton = btnCancel;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

            LoadValues(current, autostart);
            foreach (var rb in new[] { rbDark, rbLight }) rb.CheckedChanged += (s, e) => Changed();
            tbOpacity.ValueChanged += (s, e) => Changed();
            chkTrail.CheckedChanged += (s, e) => Changed();
            chkAutostart.CheckedChanged += (s, e) => chkShowOnAutostart.Enabled = chkAutostart.Checked;
        }

        private void LoadValues(AppSettings s, bool autostart)
        {
            loading = true;
            rbHold.Checked = s.Mode == SwipeMode.Hold; rbClick.Checked = s.Mode == SwipeMode.Click;
            rbRu.Checked = s.Language != "en"; rbEn.Checked = s.Language == "en";
            rbDark.Checked = s.Theme == Theme.Dark; rbLight.Checked = s.Theme == Theme.Light;
            tbOpacity.Value = Math.Max(tbOpacity.Minimum, Math.Min(tbOpacity.Maximum, s.Opacity));
            lblOpacity.Text = tbOpacity.Value + " %";
            chkTrail.Checked = s.ShowTrail; chkPreview.Checked = s.LivePreview;
            chkCaps.Checked = s.AutoCapitalize; chkPunct.Checked = s.SmartPunctuation;
            chkLearn.Checked = s.Learn; chkProfanity.Checked = s.HideProfanity;
            chkShowOnStart.Checked = s.ShowOnStart; chkShowOnAutostart.Checked = s.ShowOnAutostart;
            chkAutostart.Checked = autostart; chkShowOnAutostart.Enabled = autostart;
            int hi = Array.IndexOf(HotkeyChoices, s.Hotkey);
            if (hi < 0) { cbHotkey.Items.Add(s.Hotkey); hi = cbHotkey.Items.Count - 1; }
            cbHotkey.SelectedIndex = hi;
            loading = false;
        }

        private AppSettings Collect()
        {
            var s = original.Clone();
            s.Mode = rbClick.Checked ? SwipeMode.Click : SwipeMode.Hold;
            s.Language = rbEn.Checked ? "en" : "ru";
            s.Theme = rbLight.Checked ? Theme.Light : Theme.Dark;
            s.Opacity = tbOpacity.Value;
            s.ShowTrail = chkTrail.Checked; s.LivePreview = chkPreview.Checked;
            s.AutoCapitalize = chkCaps.Checked; s.SmartPunctuation = chkPunct.Checked;
            s.Learn = chkLearn.Checked; s.HideProfanity = chkProfanity.Checked;
            s.ShowOnStart = chkShowOnStart.Checked; s.ShowOnAutostart = chkShowOnAutostart.Checked;
            s.Hotkey = cbHotkey.SelectedItem as string ?? "Ctrl+Alt+K";
            return s;
        }

        private void Changed()
        {
            lblOpacity.Text = tbOpacity.Value + " %";
            if (loading) return;
            try { var s = Collect(); s.Language = original.Language; preview(s); } catch (Exception ex) { Log.Error("Предпросмотр", ex); }
        }

        private static GroupBox Group(string title, out TableLayoutPanel t)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 6), Margin = new Padding(3, 3, 3, 6) };
            t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            g.Controls.Add(t);
            return g;
        }

        private static CheckBox Check(string text) { return new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 1) }; }
        private static RadioButton Radio(string text) { return new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 1) }; }
        private static void Span(TableLayoutPanel t, Control c) { t.Controls.Add(c); }
    }
}
