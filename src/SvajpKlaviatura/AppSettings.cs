using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SvajpKlaviatura
{
    internal enum SwipeMode { Hold, Click }
    internal enum Theme { Dark, Light }

    internal sealed class AppSettings
    {
        public const int OpacityMin = 30, OpacityMax = 100;

        public string Language = "ru";
        /// <summary>Положение и размер окна в физических пикселях; -1 — по умолчанию (внизу по центру).</summary>
        public int X = -1, Y = -1, W = -1, H = -1;
        public int Opacity = 96;
        public SwipeMode Mode = SwipeMode.Hold;
        public Theme Theme = Theme.Dark;
        public bool ShowTrail = true;
        public bool LivePreview = true;
        public bool AutoCapitalize = true;
        public bool SmartPunctuation = true;
        public bool HideProfanity = true;
        public bool Learn = true;
        public bool ShowOnStart = true;
        public bool ShowOnAutostart = false;
        public string Hotkey = "Ctrl+Alt+K";

        public readonly List<string> LoadWarnings = new List<string>();

        public AppSettings Clone() { return (AppSettings)MemberwiseClone(); }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Свайп-клавиатура — настройки. Файл можно удалить: будут настройки по умолчанию.");
            sb.AppendLine("Language=" + Language);
            sb.AppendLine("X=" + I(X)); sb.AppendLine("Y=" + I(Y)); sb.AppendLine("W=" + I(W)); sb.AppendLine("H=" + I(H));
            sb.AppendLine("Opacity=" + I(Opacity));
            sb.AppendLine("Mode=" + (Mode == SwipeMode.Click ? "click" : "hold"));
            sb.AppendLine("Theme=" + (Theme == Theme.Light ? "light" : "dark"));
            sb.AppendLine("ShowTrail=" + B(ShowTrail));
            sb.AppendLine("LivePreview=" + B(LivePreview));
            sb.AppendLine("AutoCapitalize=" + B(AutoCapitalize));
            sb.AppendLine("SmartPunctuation=" + B(SmartPunctuation));
            sb.AppendLine("HideProfanity=" + B(HideProfanity));
            sb.AppendLine("Learn=" + B(Learn));
            sb.AppendLine("ShowOnStart=" + B(ShowOnStart));
            sb.AppendLine("ShowOnAutostart=" + B(ShowOnAutostart));
            sb.AppendLine("Hotkey=" + Hotkey);
            return sb.ToString();
        }

        private static string B(bool v) { return v ? "true" : "false"; }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static bool TryBool(string s, out bool v)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "да" || s == "on") { v = true; return true; }
            if (s == "false" || s == "0" || s == "no" || s == "нет" || s == "off") { v = false; return true; }
            v = false; return false;
        }

        public static AppSettings Parse(string text)
        {
            var s = new AppSettings();
            if (text == null) return s;
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { s.LoadWarnings.Add("Непонятная строка в настройках пропущена: " + line); continue; }
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "Language":
                        if (val == "ru" || val == "en") s.Language = val; else s.LoadWarnings.Add("Неизвестный язык «" + val + "», выбран русский.");
                        break;
                    case "X": Int(s, key, val, ref s.X); break;
                    case "Y": Int(s, key, val, ref s.Y); break;
                    case "W": Int(s, key, val, ref s.W); break;
                    case "H": Int(s, key, val, ref s.H); break;
                    case "Opacity": Int(s, "Непрозрачность", val, ref s.Opacity); break;
                    case "Mode":
                        if (val == "click") s.Mode = SwipeMode.Click; else if (val == "hold") s.Mode = SwipeMode.Hold;
                        else s.LoadWarnings.Add("Неизвестный способ свайпа «" + val + "», выбран «зажать кнопку».");
                        break;
                    case "Theme": s.Theme = val == "light" ? Theme.Light : Theme.Dark; break;
                    case "ShowTrail": Bool(s, key, val, ref s.ShowTrail); break;
                    case "LivePreview": Bool(s, key, val, ref s.LivePreview); break;
                    case "AutoCapitalize": Bool(s, key, val, ref s.AutoCapitalize); break;
                    case "SmartPunctuation": Bool(s, key, val, ref s.SmartPunctuation); break;
                    case "HideProfanity": Bool(s, key, val, ref s.HideProfanity); break;
                    case "Learn": Bool(s, key, val, ref s.Learn); break;
                    case "ShowOnStart": Bool(s, key, val, ref s.ShowOnStart); break;
                    case "ShowOnAutostart": Bool(s, key, val, ref s.ShowOnAutostart); break;
                    case "Hotkey":
                        uint m, vk;
                        if (HotkeyText.TryParse(val, out m, out vk)) s.Hotkey = HotkeyText.Format(m, vk);
                        else s.LoadWarnings.Add("Непонятное сочетание клавиш «" + val + "», оставлено Ctrl+Alt+K.");
                        break;
                    default: break;
                }
            }
            s.Sanitize();
            return s;
        }

        private static void Bool(AppSettings s, string key, string val, ref bool field)
        {
            bool b;
            if (TryBool(val, out b)) field = b; else s.LoadWarnings.Add("Неверное значение «" + val + "» для " + key + ", оставлено по умолчанию.");
        }

        private static void Int(AppSettings s, string what, string val, ref int field)
        {
            int i;
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) field = i;
            else s.LoadWarnings.Add(what + ": неверное число «" + val + "», оставлено по умолчанию.");
        }

        public void Sanitize()
        {
            if (Opacity < OpacityMin || Opacity > OpacityMax)
            {
                int f = Math.Max(OpacityMin, Math.Min(OpacityMax, Opacity));
                LoadWarnings.Add("Непрозрачность должна быть от " + OpacityMin + " до " + OpacityMax + " % — установлено " + f + " %.");
                Opacity = f;
            }
            if (W != -1 && (W < 200 || W > 20000)) W = -1;
            if (H != -1 && (H < 80 || H > 20000)) H = -1;
        }

        public static AppSettings Load(string path)
        {
            if (!File.Exists(path)) return new AppSettings();
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex)
            {
                Log.Error("Чтение настроек", ex);
                var d = new AppSettings();
                d.LoadWarnings.Add("Не удалось прочитать настройки, используются настройки по умолчанию.");
                return d;
            }
            if (text.IndexOf('\0') >= 0)
            {
                try { File.Copy(path, path + ".broken", true); } catch { }
                var d = new AppSettings();
                d.LoadWarnings.Add("Файл настроек был повреждён и сброшен (копия: settings.ini.broken).");
                return d;
            }
            return Parse(text);
        }

        public void Save(string path) { AtomicFile.WriteAllText(path, Serialize()); }
    }

    internal static class AtomicFile
    {
        /// <summary>Сначала во временный файл, затем замена — файл не портится при сбое.</summary>
        public static void WriteAllText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; } catch (Exception ex) { Log.Error("File.Replace " + Path.GetFileName(path), ex); }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }
    }

    internal static class HotkeyText
    {
        public static bool TryParse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (string raw in s.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= Native.MOD_CONTROL;
                else if (p == "alt") mods |= Native.MOD_ALT;
                else if (p == "shift") mods |= Native.MOD_SHIFT;
                else if (p == "win") mods |= Native.MOD_WIN;
                else if (p.Length == 1 && ((p[0] >= 'a' && p[0] <= 'z') || (p[0] >= '0' && p[0] <= '9'))) vk = (uint)char.ToUpperInvariant(p[0]);
                else if (p == "space" || p == "пробел") vk = 0x20;
                else if (p.Length >= 2 && p[0] == 'f') { int f; if (int.TryParse(p.Substring(1), out f) && f >= 1 && f <= 12) vk = (uint)(0x70 + f - 1); else return false; }
                else return false;
            }
            return vk != 0 && mods != 0;
        }

        public static string Format(uint mods, uint vk)
        {
            var parts = new List<string>();
            if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & Native.MOD_WIN) != 0) parts.Add("Win");
            if (vk == 0x20) parts.Add("Space");
            else if (vk >= 0x70 && vk <= 0x7B) parts.Add("F" + (vk - 0x70 + 1));
            else parts.Add(((char)vk).ToString());
            return string.Join("+", parts);
        }
    }
}
