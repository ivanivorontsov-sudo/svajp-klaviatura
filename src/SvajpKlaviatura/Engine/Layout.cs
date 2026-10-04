using System;
using System.Collections.Generic;

namespace SvajpKlaviatura.Engine
{
    /// <summary>Точка в «клавишных» единицах: ширина и высота обычной клавиши = 1.</summary>
    public struct Pt
    {
        public double X, Y;
        public Pt(double x, double y) { X = x; Y = y; }
        public static double Dist(Pt a, Pt b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        public static double Dist2(Pt a, Pt b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
        public override string ToString() { return X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ";" + Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }
    }

    public enum KeyKind { Char, Backspace, Enter, Shift, Space, ToSymbols, ToLetters, ToSymbols2, Language, Tab, Left, Right, Up, Down }

    public sealed class KeyDef
    {
        public KeyKind Kind;
        /// <summary>Что вводит клавиша (для Char).</summary>
        public string Text;
        public string Label;
        public double X, Y, W, H;
        /// <summary>Буква слова: участвует в свайпе и в заглавных/строчных.</summary>
        public bool IsLetter;
        public Pt Center { get { return new Pt(X + W / 2, Y + H / 2); } }
        public bool Contains(Pt p) { return p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H; }

        /// <summary>Расстояние от точки до прямоугольника клавиши (0 — внутри).</summary>
        public double DistanceTo(Pt p)
        {
            double dx = Math.Max(Math.Max(X - p.X, 0), p.X - (X + W));
            double dy = Math.Max(Math.Max(Y - p.Y, 0), p.Y - (Y + H));
            return Math.Sqrt(dx * dx + dy * dy);
        }
        public override string ToString() { return Label; }
    }

    public sealed class KeyboardLayout
    {
        public readonly string Id;
        public readonly string Name;
        public readonly List<KeyDef> Keys = new List<KeyDef>();
        public readonly Dictionary<char, KeyDef> Letters = new Dictionary<char, KeyDef>();
        public double Width, Height;

        public KeyboardLayout(string id, string name) { Id = id; Name = name; }

        public bool IsLetterLayout { get { return Letters.Count > 0; } }

        public KeyDef Add(KeyKind kind, string text, string label, double x, double y, double w, double h = 1)
        {
            var k = new KeyDef { Kind = kind, Text = text, Label = label ?? text, X = x, Y = y, W = w, H = h };
            if (kind == KeyKind.Char && text != null && text.Length == 1 && char.IsLetter(text[0]))
            {
                k.IsLetter = true;
                Letters[text[0]] = k;
            }
            Keys.Add(k);
            Width = Math.Max(Width, x + w);
            Height = Math.Max(Height, y + h);
            return k;
        }

        /// <summary>Ряд одиночных символов шириной 1, начиная с x.</summary>
        public double Row(double y, double x, string chars)
        {
            foreach (char c in chars) { Add(KeyKind.Char, c.ToString(), null, x, y, 1); x += 1; }
            return x;
        }

        /// <summary>Клавиша под точкой; в зазорах и у краёв — ближайшая.</summary>
        public KeyDef KeyAt(Pt p)
        {
            KeyDef best = null; double bd = double.MaxValue;
            foreach (var k in Keys)
            {
                double d = k.DistanceTo(p);
                if (d < bd) { bd = d; best = k; }
                if (d == 0) break;
            }
            return best;
        }

        /// <summary>Центр клавиши для буквы слова (ё ищется на месте е, если отдельной нет).</summary>
        public bool TryLetterCenter(char c, out Pt p)
        {
            KeyDef k;
            if (Letters.TryGetValue(c, out k)) { p = k.Center; return true; }
            p = default(Pt);
            return false;
        }
    }

    public static class Layouts
    {
        public const string Ru = "ru", En = "en", Sym1 = "sym1", Sym2 = "sym2";

        /// <summary>ЙЦУКЕН: 13,5 × 4 клавиши.</summary>
        public static KeyboardLayout Russian()
        {
            var l = new KeyboardLayout(Ru, "Русская (ЙЦУКЕН)");
            double x = l.Row(0, 0, "йцукенгшщзхъ");
            l.Add(KeyKind.Backspace, null, "⌫", x, 0, 1.5);
            x = l.Row(1, 0.25, "фывапролджэ");
            l.Add(KeyKind.Enter, null, "⏎", x, 1, 13.5 - x);
            l.Add(KeyKind.Shift, null, "⇧", 0, 2, 1.75);
            x = l.Row(2, 1.75, "ячсмитьбюё");
            l.Add(KeyKind.Char, "-", "-", x, 2, 13.5 - x);
            BottomRow(l, "?123", "RU", new[] { 1.75, 1.5, 1, 6.25, 1, 1, 1 }, ",", ".", "?", "!");
            return l;
        }

        /// <summary>QWERTY: 11,5 × 4 клавиши.</summary>
        public static KeyboardLayout English()
        {
            var l = new KeyboardLayout(En, "Английская (QWERTY)");
            double x = l.Row(0, 0, "qwertyuiop");
            l.Add(KeyKind.Backspace, null, "⌫", x, 0, 1.5);
            x = l.Row(1, 0.5, "asdfghjkl");
            l.Add(KeyKind.Enter, null, "⏎", x, 1, 11.5 - x);
            l.Add(KeyKind.Shift, null, "⇧", 0, 2, 1.5);
            x = l.Row(2, 1.5, "zxcvbnm");
            x = l.Row(2, x, "'");
            l.Add(KeyKind.Char, "-", "-", x, 2, 11.5 - x);
            BottomRow(l, "?123", "EN", new[] { 1.5, 1.5, 1, 5.5, 1, 1 }, ",", ".", "?");
            return l;
        }

        private static void BottomRow(KeyboardLayout l, string symLabel, string langLabel, double[] w, params string[] punct)
        {
            double x = 0;
            l.Add(KeyKind.ToSymbols, null, symLabel, x, 3, w[0]); x += w[0];
            l.Add(KeyKind.Language, null, langLabel, x, 3, w[1]); x += w[1];
            l.Add(KeyKind.Char, punct[0], null, x, 3, w[2]); x += w[2];
            l.Add(KeyKind.Space, " ", "", x, 3, w[3]); x += w[3];
            for (int i = 1; i < punct.Length; i++) { l.Add(KeyKind.Char, punct[i], null, x, 3, w[3 + i]); x += w[3 + i]; }
        }

        public static KeyboardLayout Symbols1(string lettersLabel)
        {
            var l = new KeyboardLayout(Sym1, "Цифры и знаки");
            double x = l.Row(0, 0, "1234567890");
            l.Add(KeyKind.Backspace, null, "⌫", x, 0, 1.5);
            x = l.Row(1, 0, "@#№₽$%&*()");
            l.Add(KeyKind.Enter, null, "⏎", x, 1, 1.5);
            l.Add(KeyKind.ToSymbols2, null, "=\\<", 0, 2, 1.5);
            x = l.Row(2, 1.5, "-+=/_\"':;");
            l.Add(KeyKind.Char, "…", null, x, 2, 11.5 - x);
            SymBottom(l, lettersLabel);
            return l;
        }

        public static KeyboardLayout Symbols2(string lettersLabel)
        {
            var l = new KeyboardLayout(Sym2, "Знаки и стрелки");
            double x = l.Row(0, 0, "~`|\\^°€£¥§");
            l.Add(KeyKind.Backspace, null, "⌫", x, 0, 1.5);
            x = l.Row(1, 0, "[]{}<>«»—•");
            l.Add(KeyKind.Enter, null, "⏎", x, 1, 1.5);
            l.Add(KeyKind.ToSymbols, null, "?123", 0, 2, 1.5);
            l.Add(KeyKind.Tab, null, "Tab ⇥", 1.5, 2, 1.5);
            l.Add(KeyKind.Left, null, "←", 3, 2, 1);
            l.Add(KeyKind.Up, null, "↑", 4, 2, 1);
            l.Add(KeyKind.Down, null, "↓", 5, 2, 1);
            l.Add(KeyKind.Right, null, "→", 6, 2, 1);
            x = l.Row(2, 7, "©×÷");
            l.Add(KeyKind.Char, "±", null, x, 2, 11.5 - x);
            SymBottom(l, lettersLabel);
            return l;
        }

        private static void SymBottom(KeyboardLayout l, string lettersLabel)
        {
            l.Add(KeyKind.ToLetters, null, lettersLabel, 0, 3, 1.5);
            l.Add(KeyKind.Char, ",", null, 1.5, 3, 1);
            l.Add(KeyKind.Space, " ", "", 2.5, 3, 6);
            l.Add(KeyKind.Char, ".", null, 8.5, 3, 1);
            l.Add(KeyKind.Char, "!", null, 9.5, 3, 1);
            l.Add(KeyKind.Char, "?", null, 10.5, 3, 1);
        }

        public static KeyboardLayout ForLanguage(string lang) { return lang == En ? English() : Russian(); }
    }
}
