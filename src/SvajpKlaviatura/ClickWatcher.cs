using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SvajpKlaviatura
{
    /// <summary>Следит (WH_MOUSE_LL в отдельном потоке) за щелчками вне клавиатуры: после такого щелчка
    /// курсор в тексте мог переместиться, и «⌫ стирает слово целиком» уже небезопасно.
    /// Обработчик только запоминает время и никогда не задерживает мышь.</summary>
    internal sealed class ClickWatcher : IDisposable
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, int threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;

        private readonly HookProc proc;
        private readonly Func<int, int, bool> isInside;
        private Thread thread;
        private volatile int threadId;
        private IntPtr hook;
        private readonly ManualResetEvent started = new ManualResetEvent(false);
        public volatile bool Installed;
        private int outsideClicks;
        private long lastEventTick;

        public ClickWatcher(Func<int, int, bool> isInside)
        {
            this.isInside = isInside;
            proc = Callback;
        }

        /// <summary>Сколько щелчков вне клавиатуры с последнего вызова (и сброс счётчика).</summary>
        public int TakeOutsideClicks() { return Interlocked.Exchange(ref outsideClicks, 0); }
        /// <summary>Время (GetTickCount) последнего события мыши.</summary>
        public uint LastMouseTick { get { return (uint)Interlocked.Read(ref lastEventTick); } }

        public bool Start()
        {
            thread = new Thread(Main) { IsBackground = true, Name = "click-watcher", Priority = ThreadPriority.AboveNormal };
            thread.Start();
            started.WaitOne(3000);
            return Installed;
        }

        private void Main()
        {
            try
            {
                Native.MSG msg;
                Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
                threadId = Native.GetCurrentThreadId();
                hook = SetWindowsHookEx(WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
                Installed = hook != IntPtr.Zero;
                if (!Installed) Log.Write("Слежение за щелчками не установлено, код " + Marshal.GetLastWin32Error());
                started.Set();
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex) { Log.Error("Слежение за щелчками", ex); }
            finally
            {
                if (hook != IntPtr.Zero) { try { UnhookWindowsHookEx(hook); } catch { } hook = IntPtr.Zero; }
                Installed = false;
                started.Set();
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    Interlocked.Exchange(ref lastEventTick, Native.GetTickCount());
                    int m = wParam.ToInt32();
                    if (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN || m == WM_MBUTTONDOWN)
                    {
                        int x = Marshal.ReadInt32(lParam, 0), y = Marshal.ReadInt32(lParam, 4);
                        if (!isInside(x, y)) Interlocked.Increment(ref outsideClicks);
                    }
                }
                catch { }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            int id = threadId;
            if (id != 0) Native.PostThreadMessage(id, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(2000);
        }
    }
}
