using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SvajpKlaviatura.Engine;
using Xunit;

namespace SvajpKlaviatura.Tests
{
    internal static class Sta
    {
        public static void Run(Action a)
        {
            Exception err = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { err = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start(); t.Join();
            if (err != null) throw new Exception("STA: " + err.Message, err);
        }
    }

    internal sealed class TempDir : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sk-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        public TempDir() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    public class SettingsTests
    {
        [Fact]
        public void RoundTrip()
        {
            var s = new AppSettings { Language = "en", X = 10, Y = 20, W = 900, H = 330, Opacity = 70, Mode = SwipeMode.Click, Theme = Theme.Light, ShowTrail = false, Hotkey = "Ctrl+Shift+K" };
            var p = AppSettings.Parse(s.Serialize());
            Assert.Equal(s.Serialize(), p.Serialize());
            Assert.Empty(p.LoadWarnings);
        }

        [Fact]
        public void BadValues_Fixed()
        {
            var p = AppSettings.Parse("Opacity=5\nMode=zzz\nLanguage=de\nHotkey=Ctrl+???\nW=5\nгде равно\nUnknown=1\n");
            Assert.Equal(AppSettings.OpacityMin, p.Opacity);
            Assert.Equal(SwipeMode.Hold, p.Mode);
            Assert.Equal("ru", p.Language);
            Assert.Equal("Ctrl+Alt+K", p.Hotkey);
            Assert.Equal(-1, p.W);
            Assert.True(p.LoadWarnings.Count >= 4);
        }

        [Fact]
        public void BrokenFile_Reset()
        {
            using (var d = new TempDir())
            {
                string f = Path.Combine(d.Path, "settings.ini");
                File.WriteAllBytes(f, new byte[] { 0, 1, 2, 0 });
                var s = AppSettings.Load(f);
                Assert.Equal("ru", s.Language);
                Assert.True(File.Exists(f + ".broken"));
                s.Save(f);
                Assert.Contains("Language=ru", File.ReadAllText(f));
            }
        }

        [Fact]
        public void Hotkeys()
        {
            uint m, vk;
            Assert.True(HotkeyText.TryParse("Ctrl+Alt+K", out m, out vk));
            Assert.Equal(Native.MOD_CONTROL | Native.MOD_ALT, m); Assert.Equal((uint)'K', vk);
            Assert.True(HotkeyText.TryParse("ctrl + alt + space", out m, out vk)); Assert.Equal(0x20u, vk);
            Assert.True(HotkeyText.TryParse("Ctrl+Alt+F9", out m, out vk)); Assert.Equal("Ctrl+Alt+F9", HotkeyText.Format(m, vk));
            Assert.False(HotkeyText.TryParse("K", out m, out vk));
            foreach (var h in SettingsForm.HotkeyChoices) Assert.True(HotkeyText.TryParse(h, out m, out vk), h);
        }
    }

    public class InputTests
    {
        [Fact]
        public void Build_UnicodeAndKeys()
        {
            var inp = InputSender.Build(new[] { OutAction.Type("Ёж\n"), OutAction.Back(2), OutAction.Press(VKey.Left) });
            Assert.Equal(2 + 2 + 2 + 4 + 2, inp.Length);
            Assert.Equal((ushort)'Ё', inp[0].u.ki.wScan);
            Assert.Equal(Native.KEYEVENTF_UNICODE, inp[0].u.ki.dwFlags);
            Assert.Equal(Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP, inp[1].u.ki.dwFlags);
            Assert.Equal(InputSender.VK_RETURN, inp[4].u.ki.wVk);
            Assert.Equal(InputSender.VK_BACK, inp[6].u.ki.wVk);
            Assert.Equal(InputSender.VK_LEFT, inp[10].u.ki.wVk);
            Assert.Equal(Native.KEYEVENTF_EXTENDEDKEY, inp[10].u.ki.dwFlags);
            Assert.Equal(IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }

    public class WindowTests
    {
        [Fact]
        public void EmbeddedDictionaries_Load()
        {
            using (var d = new TempDir())
            {
                var dicts = new Dictionaries(d.Path);
                dicts.Load();
                Assert.Equal(50000, dicts.Ru.Count);
                Assert.Equal(50000, dicts.En.Count);
                dicts.Ru.AddUserWord("тестслово");
                dicts.SaveIfChanged();
                Assert.Contains("тестслово\t5", File.ReadAllText(dicts.UserFile("ru")));
                var again = new Dictionaries(d.Path); again.Load();
                Assert.Equal(5, again.Ru.Find("тестслово").UserCount);
            }
        }

        [Fact]
        public void KeyboardWindow_NoActivate_TopMost_NotInTaskbar()
        {
            Sta.Run(() =>
            {
                Native.EnsureDpiAwareness();
                using (var d = new TempDir())
                {
                    var dicts = new Dictionaries(d.Path); dicts.Load();
                    using (var f = new KeyboardForm(new AppSettings(), dicts, d.Path, true))
                    {
                        IntPtr fgBefore = Native.GetForegroundWindow();
                        f.ShowKeyboard();
                        Application.DoEvents();
                        int ex = Native.GetWindowLong(f.Handle, Native.GWL_EXSTYLE);
                        int need = Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
                        Assert.Equal(need, ex & need);
                        Assert.NotEqual(f.Handle, Native.GetForegroundWindow());
                        Assert.True(f.Width >= 480 && f.Height >= 150, f.Size.ToString());
                        Assert.True(File.Exists(Path.Combine(d.Path, "keys.txt")));
                        string keys = File.ReadAllText(Path.Combine(d.Path, "keys.txt"));
                        Assert.Contains("LAYOUT\tru", keys);
                        Assert.Contains("\nп\t", keys);
                        f.SetLanguage("en", false);
                        Assert.Contains("LAYOUT\ten", File.ReadAllText(Path.Combine(d.Path, "keys.txt")));
                        // отрисовка не падает на разных размерах
                        foreach (var sz in new[] { new System.Drawing.Size(360, 130), new System.Drawing.Size(1900, 700) })
                        {
                            f.Size = sz;
                            using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height)) f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        }
                        f.HideKeyboard();
                        Assert.False(f.Visible);
                    }
                }
            });
        }

        [Fact]
        public void Autostart_CommandAndPath()
        {
            Assert.Equal("\"C:\\a b\\SvajpKlaviatura.exe\" --autostart", Autostart.CommandFor("C:\\a b\\SvajpKlaviatura.exe"));
            Assert.Equal("C:\\a b\\SvajpKlaviatura.exe", Autostart.ExePathFromCommand("\"C:\\a b\\SvajpKlaviatura.exe\" --autostart"));
            Assert.NotNull(Autostart.RiskyLocationWarning("C:\\Temp\\x\\SvajpKlaviatura.exe", "C:\\Temp\\", null));
        }

        [Fact]
        public void Args()
        {
            var o = Program.ParseArgs(new[] { "--config-dir", "x", "--test", "--show" });
            Assert.True(o.Test && o.Show); Assert.Equal("x", o.ConfigDir); Assert.Null(o.Error);
            Assert.NotNull(Program.ParseArgs(new[] { "--bogus" }).Error);
        }
    }
}
