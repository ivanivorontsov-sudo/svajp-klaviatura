using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SvajpKlaviatura
{
    internal static class Native
    {
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        public const int GWL_EXSTYLE = -20;
        public const int WM_MOUSEACTIVATE = 0x0021, WM_NCHITTEST = 0x0084, WM_HOTKEY = 0x0312, WM_DPICHANGED = 0x02E0,
            WM_DISPLAYCHANGE = 0x007E, WM_SETTINGCHANGE = 0x001A, WM_POINTERDOWN = 0x0246, WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02CC;
        public const int MA_NOACTIVATE = 3;
        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        // ---- SendInput ----
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr h, string name, IntPtr data);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int pid);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public int cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO li);
        public static uint LastInputTick()
        {
            var li = new LASTINPUTINFO { cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            try { return GetLastInputInfo(ref li) ? li.dwTime : 0; } catch { return 0; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time; public POINT pt; }
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool PostThreadMessage(int threadId, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern int GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern uint GetTickCount();
        public const int WM_QUIT = 0x0012;

        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);

        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public static void EnsureDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
            try { SetProcessDpiAwareness(2); return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static int DpiOf(IntPtr h)
        {
            try { uint d = GetDpiForWindow(h); if (d > 0) return (int)d; } catch { }
            return 96;
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            try { GetClassName(h, sb, sb.Capacity); } catch { }
            return sb.ToString();
        }

        public static int PidOf(IntPtr h) { int pid = 0; try { GetWindowThreadProcessId(h, out pid); } catch { } return pid; }

        /// <summary>Запущен ли процесс окна от имени администратора (выше нашего уровня) — тогда Windows не пропустит ввод.</summary>
        public static bool IsElevatedProcess(int pid)
        {
            if (pid <= 0 || CurrentIsElevated()) return false;
            IntPtr hp = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp == IntPtr.Zero) return false;
            try
            {
                IntPtr tok;
                if (!OpenProcessToken(hp, 0x8 /*TOKEN_QUERY*/, out tok)) return Marshal.GetLastWin32Error() == 5; // нет доступа — скорее всего, администратор
                try
                {
                    int elevated, len;
                    if (GetTokenInformation(tok, 20 /*TokenElevation*/, out elevated, 4, out len)) return elevated != 0 && !CurrentIsElevated();
                    return false;
                }
                finally { CloseHandle(tok); }
            }
            finally { CloseHandle(hp); }
        }

        private static bool? selfElevated;
        public static bool CurrentIsElevated()
        {
            if (selfElevated.HasValue) return selfElevated.Value;
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    selfElevated = new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { selfElevated = false; }
            return selfElevated.Value;
        }
    }
}
