using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura
{
    /// <summary>Ввод в активное окно через SendInput: буквы — KEYEVENTF_UNICODE (не зависит от раскладки Windows),
    /// ⌫/Enter/стрелки — виртуальными клавишами.</summary>
    internal static class InputSender
    {
        public static readonly IntPtr Marker = new IntPtr(0x5357504B); // «SWPK» — наш ввод
        public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        public static Native.INPUT[] Build(IList<OutAction> actions)
        {
            var list = new List<Native.INPUT>();
            foreach (var a in actions)
            {
                switch (a.Kind)
                {
                    case OutKind.Text:
                        foreach (char c in a.Text)
                        {
                            if (c == '\n') { Vk(list, VK_RETURN, false); continue; }
                            if (c == '\t') { Vk(list, VK_TAB, false); continue; }
                            Uni(list, c);
                        }
                        break;
                    case OutKind.Backspace:
                        for (int i = 0; i < a.Count; i++) Vk(list, VK_BACK, false);
                        break;
                    case OutKind.Key:
                        switch (a.Key)
                        {
                            case VKey.Enter: Vk(list, VK_RETURN, false); break;
                            case VKey.Tab: Vk(list, VK_TAB, false); break;
                            case VKey.Left: Vk(list, VK_LEFT, true); break;
                            case VKey.Right: Vk(list, VK_RIGHT, true); break;
                            case VKey.Up: Vk(list, VK_UP, true); break;
                            case VKey.Down: Vk(list, VK_DOWN, true); break;
                        }
                        break;
                }
            }
            return list.ToArray();
        }

        private static void Uni(List<Native.INPUT> list, char c)
        {
            foreach (bool up in new[] { false, true })
                list.Add(new Native.INPUT
                {
                    type = Native.INPUT_KEYBOARD,
                    u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0), dwExtraInfo = Marker } }
                });
        }

        private static void Vk(List<Native.INPUT> list, ushort vk, bool extended)
        {
            foreach (bool up in new[] { false, true })
                list.Add(new Native.INPUT
                {
                    type = Native.INPUT_KEYBOARD,
                    u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0) | (up ? Native.KEYEVENTF_KEYUP : 0), dwExtraInfo = Marker } }
                });
        }

        /// <summary>Отправить; возвращает текст ошибки или null.</summary>
        public static string Send(IList<OutAction> actions)
        {
            var inputs = Build(actions);
            if (inputs.Length == 0) return null;
            // Большие пачки — частями, чтобы приложения успевали
            const int chunk = 200;
            for (int i = 0; i < inputs.Length; i += chunk)
            {
                int n = Math.Min(chunk, inputs.Length - i);
                var part = new Native.INPUT[n];
                Array.Copy(inputs, i, part, 0, n);
                uint sent = Native.SendInput((uint)n, part, Marshal.SizeOf(typeof(Native.INPUT)));
                if (sent != n)
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Write("SendInput: отправлено " + sent + " из " + n + ", код " + err);
                    return err == 5 ? "Windows не дала ввести текст в это окно (нет доступа)." : "Windows не приняла ввод (код " + err + ").";
                }
            }
            return null;
        }
    }
}
