using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura
{
    /// <summary>Окно экранной клавиатуры. Никогда не становится активным (WS_EX_NOACTIVATE),
    /// поэтому текст уходит в то окно, где мигает курсор.</summary>
    internal sealed class KeyboardForm : Form
    {
        private enum Op { None, Keys, Move, Resize, Slot, BarButton }
        private enum BarBtn { None, Mode, Settings, Hide }

        private AppSettings settings;
        private readonly Dictionaries dicts;
        private readonly TypingSession session;
        private readonly bool testMode;
        private readonly string configDir;
        private KeyboardLayout letters, sym1, sym2, current;
        private readonly ClickWatcher watcher;

        public event Action SettingsRequested;
        public event Action HideRequested;
        public event Action SettingsChanged;

        // геометрия, физические пиксели клиентской области
        private float pad, barH, unitW, unitH;
        private RectangleF keysRect;
        private Bitmap cache;
        private bool cacheDirty = true;
        private Font keyFont, smallFont, barFont, symbolFont;
        private Palette pal;

        // жест
        private Op op = Op.None;
        private bool freeActive;
        private KeyDef startKey, pressedKey;
        private readonly List<PointF> trail = new List<PointF>();
        private readonly List<Pt> gesture = new List<Pt>();
        private bool movedFar;
        private PointF downPx;
        private float trailFade = 1f;
        private readonly Timer fadeTimer = new Timer { Interval = 16 };
        private readonly Timer repeatTimer = new Timer();
        private readonly Timer saveTimer = new Timer { Interval = 3000 };
        private string preview;
        private long lastPreviewMs;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private int pressedSlot = -1;
        private BarBtn pressedBtn = BarBtn.None;

        // перетаскивание окна
        private Point dragCursor;
        private Rectangle dragBounds;

        // сообщения в строке подсказок
        private string notice;
        private long noticeUntil;
        private readonly Timer noticeTimer = new Timer { Interval = 500 };

        // активное окно
        private IntPtr lastTarget = IntPtr.Zero;
        private uint lastSendTick;
        private readonly Dictionary<int, bool> elevatedCache = new Dictionary<int, bool>();
        private readonly ToolTip tips = new ToolTip { ShowAlways = true, InitialDelay = 700 };
        private string lastTip;

        public int Swipes, Taps;

        public KeyboardForm(AppSettings s, Dictionaries d, string configDir, bool testMode)
        {
            settings = s.Clone();
            dicts = d;
            this.configDir = configDir;
            this.testMode = testMode;
            session = new TypingSession(d.For(settings.Language));
            ApplySessionSettings();
            letters = Layouts.ForLanguage(settings.Language);
            BuildSymbolLayouts();
            current = letters;

            Text = "Свайп-клавиатура";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            pal = Palette.For(settings.Theme);
            BackColor = pal.Bg;
            Opacity = settings.Opacity / 100.0;
            try { Icon = TrayApp.LoadAppIcon(32); } catch { }

            fadeTimer.Tick += (o, e) => { trailFade -= 0.08f; if (trailFade <= 0) { trailFade = 0; trail.Clear(); fadeTimer.Stop(); } Invalidate(); };
            repeatTimer.Tick += (o, e) => { repeatTimer.Interval = 70; if (pressedKey != null && pressedKey.Kind == KeyKind.Backspace) Do("⌫", session.Backspace); else repeatTimer.Stop(); };
            saveTimer.Tick += (o, e) => { saveTimer.Stop(); dicts.SaveIfChanged(); };
            noticeTimer.Tick += (o, e) => { if (notice != null && clock.ElapsedMilliseconds > noticeUntil) { notice = null; Invalidate(); } if (notice == null) noticeTimer.Stop(); };

            watcher = new ClickWatcher(IsInsideScreen);
            watcher.Start();
        }

        public bool WatcherInstalled { get { return watcher.Installed; } }
        public string CurrentLayoutId { get { return current.Id; } }

        private void BuildSymbolLayouts()
        {
            string back = letters.Id == Layouts.En ? "ABC" : "АБВ";
            sym1 = Layouts.Symbols1(back);
            sym2 = Layouts.Symbols2(back);
        }

        private void ApplySessionSettings()
        {
            session.AutoCapitalize = settings.AutoCapitalize;
            session.SmartPunctuation = settings.SmartPunctuation;
            session.HideBlocked = settings.HideProfanity;
            session.Learn = settings.Learn;
        }

        public void ApplySettings(AppSettings s)
        {
            bool langChanged = s.Language != settings.Language;
            settings = s.Clone();
            ApplySessionSettings();
            pal = Palette.For(settings.Theme);
            BackColor = pal.Bg;
            Opacity = settings.Opacity / 100.0;
            if (langChanged) SetLanguage(settings.Language, false);
            cacheDirty = true;
            Invalidate();
        }

        public AppSettings CurrentSettings { get { return settings.Clone(); } }

        // ---------------- окно без активации ----------------

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // для сенсорного экрана: без «нажать и удерживать = правая кнопка» и без росчерков-жестов
            try { Native.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(0x00000001 | 0x00000008 | 0x00000010 | 0x00010000)); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == Native.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(0x00000001 | 0x00000008 | 0x00000010 | 0x00010000); return; }
            if (m.Msg == Native.WM_DPICHANGED)
            {
                var r = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void ShowKeyboard()
        {
            if (!IsHandleCreated) CreateHandle();
            if (!Visible)
            {
                PlaceWindow();
                Show();
            }
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            DumpKeys();
        }

        public void HideKeyboard()
        {
            CancelGesture();
            Hide();
        }

        private float DpiK { get { return IsHandleCreated ? Native.DpiOf(Handle) / 96f : 1f; } }

        /// <summary>Положение из настроек, если оно на видимом экране; иначе — внизу по центру.</summary>
        private void PlaceWindow()
        {
            var r = new Rectangle(settings.X, settings.Y, settings.W, settings.H);
            if (settings.W <= 0 || settings.H <= 0 || !OnSomeScreen(r)) r = DefaultBounds(Screen.FromPoint(Cursor.Position).WorkingArea);
            Bounds = r;
        }

        public Rectangle DefaultBounds(Rectangle wa)
        {
            int w = (int)Math.Max(480, Math.Min(1500, wa.Width * 0.62));
            w = Math.Min(w, wa.Width);
            int h = Math.Min(wa.Height / 2, (int)(w * 0.37));
            return new Rectangle(wa.X + (wa.Width - w) / 2, wa.Bottom - h - 12, w, h);
        }

        public void SetSizePreset(double fraction)
        {
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            int w = (int)(wa.Width * fraction);
            int h = Math.Min(wa.Height * 2 / 3, (int)(w * (fraction >= 0.99 ? 0.30 : 0.37)));
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Bottom - h - (fraction >= 0.99 ? 0 : 12), w, h);
            RememberBounds();
        }

        private static bool OnSomeScreen(Rectangle r)
        {
            foreach (var s in Screen.AllScreens)
            {
                var i = Rectangle.Intersect(s.WorkingArea, r);
                if (i.Width >= 120 && i.Height >= 60) return true;
            }
            return false;
        }

        private bool IsInsideScreen(int x, int y)
        {
            try { return Visible && Bounds.Contains(x, y); } catch { return false; }
        }

        private void RememberBounds()
        {
            settings.X = Left; settings.Y = Top; settings.W = Width; settings.H = Height;
            if (SettingsChanged != null) SettingsChanged();
            DumpKeys();
        }

        // ---------------- геометрия ----------------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            cacheDirty = true;
            Invalidate();
        }

        private void Layout_()
        {
            float w = ClientSize.Width, h = ClientSize.Height;
            pad = Math.Max(3, 5 * DpiK);
            float uh = (h - 2 * pad) / ((float)current.Height + 0.8f);
            barH = uh * 0.8f;
            keysRect = new RectangleF(pad, pad + barH, w - 2 * pad, h - 2 * pad - barH);
            unitW = keysRect.Width / (float)current.Width;
            unitH = keysRect.Height / (float)current.Height;
            float u = Math.Min(unitW, unitH);
            SetFont(ref keyFont, "Segoe UI", u * 0.42f, FontStyle.Regular);
            SetFont(ref smallFont, "Segoe UI", u * 0.27f, FontStyle.Regular);
            SetFont(ref symbolFont, "Segoe UI Symbol", u * 0.36f, FontStyle.Regular);
            SetFont(ref barFont, "Segoe UI", Math.Max(8, barH * 0.42f), FontStyle.Regular);
        }

        private static void SetFont(ref Font f, string family, float px, FontStyle style)
        {
            px = Math.Max(6, px);
            if (f != null && Math.Abs(f.Size - px) < 0.5f && f.Style == style && f.FontFamily.Name == family) return;
            if (f != null) f.Dispose();
            f = new Font(family, px, style, GraphicsUnit.Pixel);
        }

        public RectangleF KeyRectPx(KeyDef k)
        {
            float gap = Math.Min(unitW, unitH) * 0.07f;
            return new RectangleF(keysRect.X + (float)k.X * unitW + gap / 2, keysRect.Y + (float)k.Y * unitH + gap / 2, (float)k.W * unitW - gap, (float)k.H * unitH - gap);
        }

        private Pt ToUnits(PointF p) { return new Pt((p.X - keysRect.X) / unitW, (p.Y - keysRect.Y) / unitH); }

        private RectangleF GripRect { get { return new RectangleF(pad, pad, barH * 1.1f, barH); } }
        private RectangleF ModeRect { get { return new RectangleF(ClientSize.Width - pad - barH * 4.4f, pad, barH * 2.2f, barH); } }
        private RectangleF SettingsRect { get { return new RectangleF(ClientSize.Width - pad - barH * 2.2f, pad, barH * 1.1f, barH); } }
        private RectangleF HideRect { get { return new RectangleF(ClientSize.Width - pad - barH * 1.1f, pad, barH * 1.1f, barH); } }
        private RectangleF ResizeRect { get { float s = Math.Max(14 * DpiK, Math.Min(unitW, unitH) * 0.3f); return new RectangleF(ClientSize.Width - s, ClientSize.Height - s, s, s); } }

        private RectangleF SlotRect(int i)
        {
            float x0 = GripRect.Right + pad, x1 = ModeRect.Left - pad;
            float w = (x1 - x0) / 3;
            return new RectangleF(x0 + i * w, pad, w, barH);
        }

        // ---------------- отрисовка ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width < 10 || ClientSize.Height < 10) return;
            if (cacheDirty || cache == null || cache.Width != ClientSize.Width || cache.Height != ClientSize.Height)
            {
                Layout_();
                RenderCache();
                cacheDirty = false;
            }
            var g = e.Graphics;
            g.DrawImageUnscaled(cache, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawBar(g);
            if (pressedKey != null && op == Op.Keys && !(movedFar && startKey != null && startKey.IsLetter)) DrawKey(g, pressedKey, true);
            if (freeActive || (op == Op.Keys && movedFar))
            {
                var k = trail.Count > 0 ? current.KeyAt(ToUnits(trail[trail.Count - 1])) : null;
                if (k != null && k.IsLetter) using (var b = new SolidBrush(Color.FromArgb(70, pal.Accent))) FillRound(g, b, KeyRectPx(k), Math.Min(unitW, unitH) * 0.12f);
            }
            if (settings.ShowTrail && trail.Count > 1 && trailFade > 0) DrawTrail(g);
        }

        private void RenderCache()
        {
            if (cache != null) cache.Dispose();
            cache = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(cache))
            {
                g.Clear(pal.Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(pal.Bar)) g.FillRectangle(b, 0, 0, ClientSize.Width, pad + barH + pad / 2);
                foreach (var k in current.Keys) DrawKey(g, k, false);
                // уголок для изменения размера
                var rr = ResizeRect;
                using (var p = new Pen(pal.TextDim, Math.Max(1, DpiK)))
                    for (int i = 1; i <= 3; i++)
                    {
                        float o = rr.Width * i / 4f;
                        g.DrawLine(p, rr.Right - o, rr.Bottom - 2, rr.Right - 2, rr.Bottom - o);
                    }
            }
        }

        private bool UpperLabels { get { return session.CurrentCase != CaseMode.Lower; } }

        private void DrawKey(Graphics g, KeyDef k, bool pressed)
        {
            var r = KeyRectPx(k);
            bool special = !(k.Kind == KeyKind.Char || k.Kind == KeyKind.Space);
            Color fill = pressed ? pal.Pressed : special ? pal.Special : pal.Key;
            if (k.Kind == KeyKind.Shift && session.Shift != ShiftState.Off) fill = session.Shift == ShiftState.Locked ? pal.Accent : pal.Pressed;
            float rad = Math.Min(unitW, unitH) * 0.12f;
            using (var sh = new SolidBrush(pal.Shadow)) FillRound(g, sh, new RectangleF(r.X, r.Y + Math.Max(1, DpiK), r.Width, r.Height), rad);
            using (var b = new SolidBrush(fill)) FillRound(g, b, r, rad);
            string label = k.Label;
            Font f = keyFont;
            if (k.Kind == KeyKind.Char && k.IsLetter) label = UpperLabels ? label.ToUpperInvariant() : label;
            else if (k.Kind == KeyKind.Space) { label = current.IsLetterLayout ? (letters.Id == Layouts.En ? "English" : "Русский") : "пробел"; f = smallFont; }
            else if (k.Kind == KeyKind.Language) { label = "🌐 " + (letters.Id == Layouts.En ? "EN" : "RU"); f = smallFont; }
            else if (k.Kind == KeyKind.ToSymbols || k.Kind == KeyKind.ToLetters || k.Kind == KeyKind.ToSymbols2 || k.Kind == KeyKind.Tab) f = smallFont;
            else if (k.Kind == KeyKind.Shift && session.Shift == ShiftState.Locked) label = "⇪";
            if (special && k.Kind != KeyKind.Language && k.Kind != KeyKind.ToSymbols && k.Kind != KeyKind.ToLetters && k.Kind != KeyKind.ToSymbols2 && k.Kind != KeyKind.Tab) f = symbolFont;
            Color tc = (k.Kind == KeyKind.Space) ? pal.TextDim : pal.Text;
            if (k.Kind == KeyKind.Shift && session.Shift == ShiftState.Locked) tc = pal.OnAccent;
            TextRenderer.DrawText(g, label, f, Rectangle.Round(r), tc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void DrawBar(Graphics g)
        {
            using (var b = new SolidBrush(pal.Bar)) g.FillRectangle(b, 0, 0, ClientSize.Width, pad + barH + pad / 2);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, "⠿", symbolFont, Rectangle.Round(GripRect), pal.TextDim, flags);
            float rad = barH * 0.2f;
            if (notice != null)
            {
                var nr = RectangleF.FromLTRB(SlotRect(0).Left, pad, SlotRect(2).Right, pad + barH);
                using (var b = new SolidBrush(pal.NoticeBg)) FillRound(g, b, nr, rad);
                TextRenderer.DrawText(g, notice, smallFont, Rectangle.Round(nr), pal.NoticeText, flags);
            }
            else if (freeActive && preview == null)
            {
                var nr = RectangleF.FromLTRB(SlotRect(0).Left, pad, SlotRect(2).Right, pad + barH);
                TextRenderer.DrawText(g, "Ведите по буквам и щёлкните в конце слова (правая кнопка — отмена)", smallFont, Rectangle.Round(nr), pal.TextDim, flags);
            }
            else if (preview != null)
            {
                var r = SlotRect(1);
                using (var b = new SolidBrush(pal.Special)) FillRound(g, b, r, rad);
                using (var f = new Font(barFont, FontStyle.Italic)) TextRenderer.DrawText(g, preview, f, Rectangle.Round(r), pal.TextDim, flags);
            }
            else
            {
                var sug = session.Suggestions;
                for (int i = 0; i < 3 && i < sug.Count; i++)
                {
                    var r = SlotRect(i);
                    using (var b = new SolidBrush(pressedSlot == i ? pal.Pressed : pal.Special)) FillRound(g, b, r, rad);
                    string text = sug[i].AddsWord ? "«" + sug[i].Word + "» +" : sug[i].Word;
                    TextRenderer.DrawText(g, text, barFont, Rectangle.Round(r), sug[i].AddsWord ? pal.Accent : pal.Text, flags);
                    if (i > 0) using (var p = new Pen(pal.Bg)) g.DrawLine(p, r.Left, r.Top + barH * 0.2f, r.Left, r.Bottom - barH * 0.2f);
                }
            }
            var mr = ModeRect;
            using (var b = new SolidBrush(pressedBtn == BarBtn.Mode ? pal.Pressed : pal.Special)) FillRound(g, b, mr, rad);
            TextRenderer.DrawText(g, settings.Mode == SwipeMode.Click ? "без зажатия" : "с зажатием", smallFont, Rectangle.Round(mr), pal.Text, flags);
            TextRenderer.DrawText(g, "⚙", symbolFont, Rectangle.Round(SettingsRect), pressedBtn == BarBtn.Settings ? pal.Accent : pal.TextDim, flags);
            TextRenderer.DrawText(g, "✕", symbolFont, Rectangle.Round(HideRect), pressedBtn == BarBtn.Hide ? pal.Accent : pal.TextDim, flags);
        }

        private void DrawTrail(Graphics g)
        {
            float w = Math.Max(3, Math.Min(unitW, unitH) * 0.13f);
            int n = trail.Count;
            for (int i = 1; i < n; i++)
            {
                float t = (float)i / n;
                int a = (int)(255 * trailFade * (0.25f + 0.75f * t));
                using (var p = new Pen(Color.FromArgb(Math.Max(0, Math.Min(255, a)), pal.Trail), w * (0.45f + 0.55f * t)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, trail[i - 1], trail[i]);
            }
        }

        private static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            if (rad < 1) { g.FillRectangle(b, r); return; }
            using (var p = new GraphicsPath())
            {
                float d = rad * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        // ---------------- мышь, тачпад, сенсорный экран ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            try { MouseDownCore(e); } catch (Exception ex) { Log.Error("Нажатие", ex); CancelGesture(); }
        }

        private void MouseDownCore(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { if (freeActive || op == Op.Keys) { CancelGesture(); Invalidate(); } return; }
            if (e.Button != MouseButtons.Left) return;
            var p = new PointF(e.X, e.Y);
            if (freeActive) { AddPoint(p); FinishGesture(p); return; }
            if (ResizeRect.Contains(p)) { BeginDrag(Op.Resize); return; }
            if (p.Y < keysRect.Top - pad / 2)
            {
                if (ModeRect.Contains(p)) { op = Op.BarButton; pressedBtn = BarBtn.Mode; }
                else if (SettingsRect.Contains(p)) { op = Op.BarButton; pressedBtn = BarBtn.Settings; }
                else if (HideRect.Contains(p)) { op = Op.BarButton; pressedBtn = BarBtn.Hide; }
                else
                {
                    int slot = -1;
                    for (int i = 0; i < 3; i++) if (SlotRect(i).Contains(p) && i < session.Suggestions.Count && notice == null) slot = i;
                    if (slot >= 0) { op = Op.Slot; pressedSlot = slot; }
                    else { BeginDrag(Op.Move); return; }
                }
                Capture = true;
                Invalidate();
                return;
            }
            var key = current.KeyAt(ToUnits(p));
            if (key == null) return;
            op = Op.Keys;
            Capture = true;
            startKey = pressedKey = key;
            downPx = p;
            movedFar = false;
            preview = null;
            trail.Clear(); gesture.Clear();
            trailFade = 1f; fadeTimer.Stop();
            AddPoint(p);
            if (settings.Mode == SwipeMode.Click && key.IsLetter && current.IsLetterLayout)
            {
                freeActive = true; // жест начат щелчком, закончится следующим щелчком
            }
            else if (key.Kind == KeyKind.Backspace)
            {
                Do("⌫", session.Backspace);
                repeatTimer.Interval = 450; repeatTimer.Start();
            }
            Invalidate();
        }

        private void BeginDrag(Op o)
        {
            op = o;
            dragCursor = Cursor.Position;
            dragBounds = Bounds;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            try
            {
                var p = new PointF(e.X, e.Y);
                UpdateCursor(p);
                if (op == Op.Move || op == Op.Resize) { DragTo(Cursor.Position); return; }
                if (op != Op.Keys && !freeActive) return;
                AddPoint(p);
                if (!movedFar)
                {
                    var u0 = ToUnits(downPx); var u1 = ToUnits(p);
                    if (Pt.Dist(u0, u1) > 0.45) { movedFar = true; repeatTimer.Stop(); }
                }
                if (movedFar && startKey != null && startKey.IsLetter && current.IsLetterLayout && settings.LivePreview && clock.ElapsedMilliseconds - lastPreviewMs > 110)
                {
                    lastPreviewMs = clock.ElapsedMilliseconds;
                    var r = GestureDecoder.Decode(session.Lex, gesture, 1, settings.HideProfanity);
                    preview = r.Best == null ? null : TypingSession.ApplyCase(r.Best, session.CurrentCase, session.Lex.Language);
                }
                Invalidate();
            }
            catch (Exception ex) { Log.Error("Движение", ex); CancelGesture(); }
        }

        private void UpdateCursor(PointF p)
        {
            Cursor c = Cursors.Default;
            string tip = null;
            if (op == Op.None && !freeActive)
            {
                if (ResizeRect.Contains(p)) { c = Cursors.SizeNWSE; tip = "Потяните, чтобы изменить размер"; }
                else if (p.Y < keysRect.Top - pad / 2)
                {
                    if (ModeRect.Contains(p)) tip = settings.Mode == SwipeMode.Click ? "Сейчас: щелчок на первой букве, ведите, щелчок в конце (удобно на тачпаде). Нажмите — «с зажатием кнопки»" : "Сейчас: зажмите кнопку на первой букве и ведите. Нажмите — режим «без зажатия» (удобно на тачпаде)";
                    else if (SettingsRect.Contains(p)) tip = "Настройки";
                    else if (HideRect.Contains(p)) tip = "Скрыть клавиатуру (" + settings.Hotkey + " — показать)";
                    else if (!(SlotRect(0).Contains(p) || SlotRect(1).Contains(p) || SlotRect(2).Contains(p)) || session.Suggestions.Count == 0) { c = Cursors.SizeAll; tip = "Потяните, чтобы передвинуть клавиатуру"; }
                }
            }
            if (Cursor != c) Cursor = c;
            if (tip != lastTip) { lastTip = tip; tips.SetToolTip(this, tip); }
        }

        private void DragTo(Point cur)
        {
            int dx = cur.X - dragCursor.X, dy = cur.Y - dragCursor.Y;
            if (op == Op.Move) Location = new Point(dragBounds.X + dx, dragBounds.Y + dy);
            else
            {
                int minW = (int)(360 * DpiK), minH = (int)(130 * DpiK);
                Size = new Size(Math.Max(minW, dragBounds.Width + dx), Math.Max(minH, dragBounds.Height + dy));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            try { MouseUpCore(e); } catch (Exception ex) { Log.Error("Отпускание", ex); CancelGesture(); }
        }

        private void MouseUpCore(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var p = new PointF(e.X, e.Y);
            repeatTimer.Stop();
            switch (op)
            {
                case Op.Move:
                case Op.Resize:
                    op = Op.None; Capture = false;
                    if (!OnSomeScreen(Bounds)) Bounds = DefaultBounds(Screen.FromPoint(Cursor.Position).WorkingArea);
                    RememberBounds();
                    return;
                case Op.Slot:
                    op = Op.None; Capture = false;
                    int slot = pressedSlot; pressedSlot = -1;
                    if (slot >= 0 && SlotRect(slot).Contains(p)) Do("подсказка", () => session.Choose(slot));
                    Invalidate();
                    return;
                case Op.BarButton:
                    op = Op.None; Capture = false;
                    var b = pressedBtn; pressedBtn = BarBtn.None;
                    if (b == BarBtn.Mode && ModeRect.Contains(p)) { settings.Mode = settings.Mode == SwipeMode.Click ? SwipeMode.Hold : SwipeMode.Click; if (SettingsChanged != null) SettingsChanged(); Notice(settings.Mode == SwipeMode.Click ? "Без зажатия: щёлкните на первой букве, ведите, щёлкните в конце" : "С зажатием: нажмите на первой букве и ведите, не отпуская"); }
                    else if (b == BarBtn.Settings && SettingsRect.Contains(p)) { if (SettingsRequested != null) SettingsRequested(); }
                    else if (b == BarBtn.Hide && HideRect.Contains(p)) { if (HideRequested != null) HideRequested(); }
                    Invalidate();
                    return;
                case Op.Keys:
                    if (freeActive) return; // ждём второго щелчка
                    AddPoint(p);
                    FinishGesture(p);
                    return;
            }
        }

        private void AddPoint(PointF p)
        {
            if (trail.Count > 0)
            {
                var last = trail[trail.Count - 1];
                if (Math.Abs(last.X - p.X) < 0.5f && Math.Abs(last.Y - p.Y) < 0.5f) return;
            }
            trail.Add(p);
            gesture.Add(ToUnits(p));
        }

        private void FinishGesture(PointF p)
        {
            bool wasFree = freeActive;
            freeActive = false;
            op = Op.None;
            Capture = false;
            var key = startKey;
            bool swipe = movedFar && key != null && key.IsLetter && current.IsLetterLayout;
            preview = null;
            if (swipe) DoSwipe();
            else if (key != null && !(key.Kind == KeyKind.Backspace && !wasFree))
            {
                // нажатие (в режиме «без зажатия» буква — двойной щелчок на месте)
                pressedKey = key;
                Tap(key);
            }
            pressedKey = null;
            startKey = null;
            if (swipe && trail.Count > 1) { trailFade = 1f; fadeTimer.Start(); } else trail.Clear();
            Invalidate();
        }

        private void CancelGesture()
        {
            freeActive = false;
            if (op == Op.Keys) op = Op.None;
            if (Capture) Capture = false;
            startKey = pressedKey = null;
            preview = null;
            trail.Clear(); gesture.Clear();
            repeatTimer.Stop();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && op == Op.Keys && !freeActive) { CancelGesture(); Invalidate(); }
        }

        // ---------------- действия ----------------

        private void DoSwipe()
        {
            var r = GestureDecoder.Decode(session.Lex, gesture, 4, settings.HideProfanity);
            Swipes++;
            if (testMode) Log.Write("Свайп: " + gesture.Count + " точек, кандидатов " + r.Pruned + ", " + r.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " мс → " + string.Join(", ", r.Candidates.ConvertAll(c => c.Word)));
            if (r.Candidates.Count == 0) { Notice("Слово не распознано — проведите ещё раз от первой буквы до последней"); return; }
            Do("свайп", () => session.Swipe(r));
        }

        private void Tap(KeyDef k)
        {
            Taps++;
            switch (k.Kind)
            {
                case KeyKind.Char: Do("буква", () => session.TapText(k.Text, k.IsLetter)); if (!k.IsLetter && current != letters && k.Text == "'") { } break;
                case KeyKind.Space:
                    Do("пробел", session.Space);
                    if (current != letters) SwitchLayout(letters);
                    break;
                case KeyKind.Backspace: Do("⌫", session.Backspace); break;
                case KeyKind.Enter: Do("Enter", session.Enter); break;
                case KeyKind.Shift: session.ToggleShift(); cacheDirty = true; break;
                case KeyKind.ToSymbols: SwitchLayout(sym1); break;
                case KeyKind.ToSymbols2: SwitchLayout(sym2); break;
                case KeyKind.ToLetters: SwitchLayout(letters); break;
                case KeyKind.Language: SetLanguage(letters.Id == Layouts.En ? Layouts.Ru : Layouts.En, true); break;
                case KeyKind.Tab: Do("Tab", () => session.Special(VKey.Tab)); break;
                case KeyKind.Left: Do("←", () => session.Special(VKey.Left)); break;
                case KeyKind.Right: Do("→", () => session.Special(VKey.Right)); break;
                case KeyKind.Up: Do("↑", () => session.Special(VKey.Up)); break;
                case KeyKind.Down: Do("↓", () => session.Special(VKey.Down)); break;
            }
        }

        private void SwitchLayout(KeyboardLayout l)
        {
            current = l;
            cacheDirty = true;
            Invalidate();
            DumpKeys();
        }

        public void SetLanguage(string lang, bool save)
        {
            settings.Language = lang;
            letters = Layouts.ForLanguage(lang);
            BuildSymbolLayouts();
            session.Lex = dicts.For(lang);
            session.Reset();
            SwitchLayout(letters);
            if (save && SettingsChanged != null) SettingsChanged();
        }

        public void Notice(string text)
        {
            notice = text;
            noticeUntil = clock.ElapsedMilliseconds + 4500;
            noticeTimer.Start();
            Invalidate();
        }

        /// <summary>Проверяет активное окно и сбрасывает контекст, если пользователь куда-то щёлкнул или печатал сам.</summary>
        private bool PrepareTarget(out string problem)
        {
            problem = null;
            IntPtr fg = Native.GetForegroundWindow();
            int pid = Native.PidOf(fg);
            if (fg == IntPtr.Zero || pid == Process.GetCurrentProcess().Id)
            {
                problem = "Сначала щёлкните в поле, куда нужно вводить текст";
                return false;
            }
            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
            {
                problem = "Сначала щёлкните в поле, куда нужно вводить текст";
                session.Reset();
                return false;
            }
            bool elevated;
            if (!elevatedCache.TryGetValue(pid, out elevated)) { elevated = Native.IsElevatedProcess(pid); elevatedCache[pid] = elevated; }
            if (elevated)
            {
                problem = "Это окно запущено от имени администратора: Windows не пропускает в него ввод. Запустите клавиатуру тоже от администратора";
                session.Reset();
                return false;
            }
            bool reset = false;
            if (fg != lastTarget) { reset = lastTarget != IntPtr.Zero; lastTarget = fg; }
            if (watcher.TakeOutsideClicks() > 0) reset = true;
            if (watcher.Installed && lastSendTick != 0)
            {
                // печатали на обычной клавиатуре после нашего ввода — позиция в тексте неизвестна
                uint li = Native.LastInputTick();
                uint ref_ = Math.Max(watcher.LastMouseTick, lastSendTick);
                if ((int)(li - ref_) > 300 && (int)(li - lastSendTick) > 300) reset = true;
            }
            if (reset) session.Reset();
            return true;
        }

        private void Do(string what, Func<List<OutAction>> action)
        {
            string problem;
            if (!PrepareTarget(out problem)) { Notice(problem); cacheDirty = true; Invalidate(); return; }
            var before = session.CurrentCase;
            List<OutAction> acts = action();
            if (acts.Count > 0)
            {
                string err = InputSender.Send(acts);
                lastSendTick = Native.GetTickCount();
                if (err != null) { Notice(err); session.Reset(); }
                if (testMode) Log.Write("Ввод (" + what + "): " + string.Join(" ", acts.ConvertAll(a => a.ToString())));
            }
            if (session.CurrentCase != before) cacheDirty = true;
            if (session.Lex != null && session.Lex.UserChanged) { saveTimer.Stop(); saveTimer.Start(); }
            Invalidate();
        }

        // ---------------- для автотестов ----------------

        /// <summary>В режиме --test записывает экранные координаты клавиш в keys.txt (для дымового теста).</summary>
        public void DumpKeys()
        {
            if (!testMode || !IsHandleCreated || !Visible) return;
            try
            {
                Layout_();
                var sb = new StringBuilder();
                var o = PointToScreen(Point.Empty);
                sb.AppendLine("LAYOUT\t" + current.Id + "\t" + letters.Id);
                sb.AppendLine("WINDOW\t" + Left + "\t" + Top + "\t" + Width + "\t" + Height);
                foreach (var k in current.Keys)
                {
                    var r = KeyRectPx(k);
                    string name = k.Kind == KeyKind.Char ? k.Text : k.Kind.ToString().ToUpperInvariant();
                    sb.AppendLine(name + "\t" + (int)(o.X + r.X + r.Width / 2) + "\t" + (int)(o.Y + r.Y + r.Height / 2) + "\t" + (int)r.Width + "\t" + (int)r.Height);
                }
                for (int i = 0; i < 3; i++) { var r = SlotRect(i); sb.AppendLine("SLOT" + i + "\t" + (int)(o.X + r.X + r.Width / 2) + "\t" + (int)(o.Y + r.Y + r.Height / 2) + "\t" + (int)r.Width + "\t" + (int)r.Height); }
                foreach (var kv in new[] { new KeyValuePair<string, RectangleF>("GRIP", GripRect), new KeyValuePair<string, RectangleF>("MODEBTN", ModeRect), new KeyValuePair<string, RectangleF>("SETTINGSBTN", SettingsRect), new KeyValuePair<string, RectangleF>("HIDEBTN", HideRect), new KeyValuePair<string, RectangleF>("RESIZE", ResizeRect) })
                    sb.AppendLine(kv.Key + "\t" + (int)(o.X + kv.Value.X + kv.Value.Width / 2) + "\t" + (int)(o.Y + kv.Value.Y + kv.Value.Height / 2) + "\t" + (int)kv.Value.Width + "\t" + (int)kv.Value.Height);
                AtomicFile.WriteAllText(Path.Combine(configDir, "keys.txt"), sb.ToString());
            }
            catch (Exception ex) { Log.Error("keys.txt", ex); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { watcher.Dispose(); } catch { }
                fadeTimer.Dispose(); repeatTimer.Dispose(); saveTimer.Dispose(); noticeTimer.Dispose(); tips.Dispose();
                if (cache != null) cache.Dispose();
                foreach (var f in new[] { keyFont, smallFont, barFont, symbolFont }) if (f != null) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class Palette
    {
        public Color Bg, Bar, Key, Special, Pressed, Shadow, Text, TextDim, Accent, OnAccent, Trail, NoticeBg, NoticeText;

        public static Palette For(Theme t)
        {
            if (t == Theme.Light)
                return new Palette
                {
                    Bg = Color.FromArgb(226, 229, 234), Bar = Color.FromArgb(214, 218, 224), Key = Color.White, Special = Color.FromArgb(200, 205, 212),
                    Pressed = Color.FromArgb(170, 199, 250), Shadow = Color.FromArgb(160, 165, 172), Text = Color.FromArgb(32, 33, 36), TextDim = Color.FromArgb(95, 99, 104),
                    Accent = Color.FromArgb(26, 115, 232), OnAccent = Color.White, Trail = Color.FromArgb(26, 115, 232), NoticeBg = Color.FromArgb(254, 239, 195), NoticeText = Color.FromArgb(90, 60, 0),
                };
            return new Palette
            {
                Bg = Color.FromArgb(32, 33, 36), Bar = Color.FromArgb(24, 25, 27), Key = Color.FromArgb(60, 62, 67), Special = Color.FromArgb(43, 45, 49),
                Pressed = Color.FromArgb(84, 110, 160), Shadow = Color.FromArgb(18, 18, 20), Text = Color.FromArgb(241, 243, 244), TextDim = Color.FromArgb(154, 160, 166),
                Accent = Color.FromArgb(138, 180, 248), OnAccent = Color.FromArgb(32, 33, 36), Trail = Color.FromArgb(79, 195, 247), NoticeBg = Color.FromArgb(70, 56, 20), NoticeText = Color.FromArgb(255, 220, 140),
            };
        }
    }
}
