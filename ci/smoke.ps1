# Дымовой тест на настоящем Windows: запуск, свайпы мышью по клавиатуре в тестовое поле и в Блокнот,
# проверка набранного текста и того, что фокус остался в поле; снимки экрана.
param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Exe).Path
$root = Join-Path ($env:RUNNER_TEMP ?? $env:TEMP) ("sk-smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$shots = Join-Path $root 'ui-shots'
New-Item -ItemType Directory -Force $root, $shots | Out-Null
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class In {
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(int tid, ref GUITHREADINFO gi);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static int ForegroundPid() { int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
  public static List<IntPtr> Visible(int pid) {
    var r = new List<IntPtr>();
    EnumWindows((h, l) => { int p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) r.Add(h); return true; }, IntPtr.Zero);
    return r;
  }
  public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
  /// Текст поля с фокусом в активном окне (WM_GETTEXT) — для Блокнота
  public static string FocusedText() {
    IntPtr fg = GetForegroundWindow(); int pid; int tid = GetWindowThreadProcessId(fg, out pid);
    var gi = new GUITHREADINFO(); gi.cbSize = Marshal.SizeOf(gi);
    if (!GetGUIThreadInfo(tid, ref gi) || gi.hwndFocus == IntPtr.Zero) return null;
    int len = SendMessage(gi.hwndFocus, 0x000E, IntPtr.Zero, IntPtr.Zero).ToInt32();
    var sb = new StringBuilder(len + 2); SendMessage(gi.hwndFocus, 0x000D, new IntPtr(len + 1), sb); return sb.ToString();
  }
  public static void MoveAbs(int x, int y) {
    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    mouse_event(0x8001, (int)Math.Round(x * 65535.0 / (w - 1)), (int)Math.Round(y * 65535.0 / (h - 1)), 0, UIntPtr.Zero);
  }
  public static void Glide(int x0, int y0, int x1, int y1, int steps, int sleep) {
    for (int i = 1; i <= steps; i++) { MoveAbs(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps); System.Threading.Thread.Sleep(sleep); }
  }
  public static void Down() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); }
  public static void Up() { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void RightClick() { mouse_event(8, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(16, 0, 0, 0, UIntPtr.Zero); }
  public static void Click() { Down(); System.Threading.Thread.Sleep(50); Up(); }
  public static void Hotkey(byte vk) {
    keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(50);
    keybd_event(vk, 0, 2, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
  }
  public static void CtrlEnd() { keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x23, 0, 1, UIntPtr.Zero); System.Threading.Thread.Sleep(30); keybd_event(0x23, 0, 3, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero); }
  public static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(30); keybd_event(vk, 0, 2, UIntPtr.Zero); }
}
"@
$failures = New-Object System.Collections.Generic.List[string]
function Check($cond, $msg) { if ($cond) { Write-Host "  OK  $msg" } else { Write-Host "  FAIL $msg" -ForegroundColor Red; $failures.Add($msg) } }
$W = [In]::GetSystemMetrics(0); $H = [In]::GetSystemMetrics(1)
Write-Host "Экран: ${W}x${H}, Windows $([Environment]::OSVersion.Version)"

function Grab() { $b = New-Object System.Drawing.Bitmap $W, $H; $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 0, 0, 0, $b.Size); $g.Dispose(); return $b }
function WithCursor($bmp) {
  $c = New-Object System.Drawing.Bitmap $bmp; $g = [System.Drawing.Graphics]::FromImage($c)
  $p = [System.Windows.Forms.Cursor]::Position
  [System.Windows.Forms.Cursors]::Arrow.Draw($g, (New-Object System.Drawing.Rectangle($p.X, $p.Y, 32, 32))); $g.Dispose(); return $c
}
function Shot($name) { $b = Grab; $c = WithCursor $b; $c.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $c.Dispose(); $b.Dispose() }
function ShotKb($name) {
  $b = Grab; $c = WithCursor $b
  $k = $script:keys['WINDOW']; $x = [Math]::Max(0, $k.X - 10); $y = [Math]::Max(0, $k.Y - 10)
  $w = [Math]::Min($W - $x, $k.W + 20); $h = [Math]::Min($H - $y, $k.H + 20)
  $crop = $c.Clone((New-Object System.Drawing.Rectangle($x, $y, $w, $h)), $c.PixelFormat)
  $crop.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $crop.Dispose(); $c.Dispose(); $b.Dispose()
}

$script:keys = @{}
function LoadKeys($cfg) {
  $f = Join-Path $cfg 'keys.txt'
  for ($i = 0; $i -lt 50 -and -not (Test-Path $f); $i++) { Start-Sleep -Milliseconds 100 }
  $script:keys = @{}
  foreach ($line in [IO.File]::ReadAllLines($f, [Text.Encoding]::UTF8)) {
    $p = $line.Split("`t")
    if ($p[0] -eq 'LAYOUT') { $script:keys['LAYOUT'] = $p[1]; continue }
    if ($p[0] -eq 'WINDOW') { $script:keys['WINDOW'] = [pscustomobject]@{ X = [int]$p[1]; Y = [int]$p[2]; W = [int]$p[3]; H = [int]$p[4] }; continue }
    if (-not $script:keys.ContainsKey($p[0])) { $script:keys[$p[0]] = [pscustomobject]@{ X = [int]$p[1]; Y = [int]$p[2]; W = [int]$p[3]; H = [int]$p[4] } }
  }
}
function K($name) { $k = $script:keys[$name]; if ($k -eq $null) { throw "нет клавиши $name" }; return $k }
function TapKey($name) { $k = K $name; [In]::Glide([System.Windows.Forms.Cursor]::Position.X, [System.Windows.Forms.Cursor]::Position.Y, $k.X, $k.Y, 6, 5); Start-Sleep -Milliseconds 60; [In]::Click(); Start-Sleep -Milliseconds 250 }
# Свайп: зажать на первой букве, провести через все буквы, отпустить. $shot — снимок до отпускания (виден след)
function Swipe($word, $shot = $null, [switch]$ClickMode) {
  $pts = @()
  $prev = ''
  foreach ($ch in $word.ToCharArray()) {
    $c = "$ch"; if ($c -eq 'ё') { $c = 'е' }
    if ($c -eq $prev) { continue }; $prev = $c
    $k = K $c
    $pts += ,@($k.X, $k.Y)
  }
  $cur = [System.Windows.Forms.Cursor]::Position
  [In]::Glide($cur.X, $cur.Y, $pts[0][0], $pts[0][1], 8, 5); Start-Sleep -Milliseconds 80
  [In]::Down(); Start-Sleep -Milliseconds 60
  if ($ClickMode) { [In]::Up(); Start-Sleep -Milliseconds 60 }
  for ($i = 1; $i -lt $pts.Count; $i++) { [In]::Glide($pts[$i - 1][0], $pts[$i - 1][1], $pts[$i][0], $pts[$i][1], 14, 9); Start-Sleep -Milliseconds 30 }
  Start-Sleep -Milliseconds 120
  if ($shot) { Shot $shot; ShotKb "$shot-kb" }
  if ($ClickMode) { [In]::Down(); Start-Sleep -Milliseconds 50 }
  [In]::Up(); Start-Sleep -Milliseconds 450
}
function TargetText() { Start-Sleep -Milliseconds 150; if (Test-Path $outFile) { return [IO.File]::ReadAllText($outFile, [Text.Encoding]::UTF8) } else { return '' } }
function Focused() { return [In]::ForegroundPid() -eq $target.Id }
function StartApp($cfg, $extra = @()) { return Start-Process $exe -ArgumentList (@('--config-dir', "`"$cfg`"", '--test', '--show') + $extra) -PassThru }
function Quit($app) { Start-Process $exe -ArgumentList @('--quit') -Wait | Out-Null; $app.WaitForExit(8000) | Out-Null; return $app.HasExited }
function LastCandidates($cfg) {
  $l = Get-Content (Join-Path $cfg 'log.txt') -Encoding UTF8 | Where-Object { $_ -match 'Свайп: .* → ' } | Select-Object -Last 1
  if (-not $l) { return @() }
  return @(($l -split ' → ', 2)[1].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

Write-Host "0) тестовое окно с полем ввода"
$outFile = Join-Path $root 'typed.txt'
$target = Start-Process pwsh -ArgumentList @('-NoProfile', '-File', "`"$PSScriptRoot\target.ps1`"", '-Out', "`"$outFile`"") -PassThru
Start-Sleep -Seconds 4
$target.Refresh()
[In]::SetForegroundWindow($target.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
Check (Focused) "поле ввода активно"

Write-Host "1) запуск: клавиатура появилась и не забрала фокус"
$cfgA = Join-Path $root 'cfgA'
$sw = [Diagnostics.Stopwatch]::StartNew()
$app = StartApp $cfgA
LoadKeys $cfgA
Write-Host "  клавиатура готова за $($sw.ElapsedMilliseconds) мс"
Start-Sleep -Milliseconds 800
Check (-not $app.HasExited) "программа работает"
Check (Focused) "фокус остался в поле ввода"
$wins = [In]::Visible($app.Id)
Check ($wins.Count -eq 1) "одно видимое окно клавиатуры ($($wins.Count))"
$need = 0x08000000 -bor 0x8 -bor 0x80
Check ((@($wins | Where-Object { ([In]::GetWindowLong($_, -20) -band $need) -ne $need }).Count) -eq 0) "окно: NOACTIVATE | TOPMOST | TOOLWINDOW"
Check ($script:keys['LAYOUT'] -eq 'ru') "раскладка ЙЦУКЕН"
$log = Get-Content (Join-Path $cfgA 'log.txt') -Raw -Encoding UTF8
Check ($log -match 'Словари загружены: ru 50000, en 50000') "словари: 50 000 + 50 000 слов"
Check ($log -match 'слежение за щелчками: да') "слежение за щелчками установлено"
Shot '01-start'; ShotKb '01-start-kb'

Write-Host "2) свайп «привет»"
Swipe 'привет' '02-swipe-privet'
$t = TargetText
Check ($t -eq 'привет ') "набрано «$t»"
Check (Focused) "фокус по-прежнему в поле ввода"
ShotKb '03-after-privet-kb'

Write-Host "3) ещё слова: «как дела»"
Swipe 'как'; Swipe 'дела'
$t = TargetText
Check ($t -eq 'привет как дела ') "набрано «$t»"

Write-Host "4) подсказки: замена последнего слова"
$cands = LastCandidates $cfgA
Write-Host "  кандидаты: $($cands -join ', ')"
if ($cands.Count -ge 2) {
  TapKey 'SLOT0'
  $t = TargetText
  Check ($t -eq "привет как $($cands[1]) ") "подсказка заменила слово: «$t»"
  ShotKb '04-suggestion-kb'
  TapKey 'SLOT0'   # вернуть «дела» (выбранное ранее слово стало подсказкой)
  $t = TargetText
  Check ($t -eq "привет как дела ") "и обратно: «$t»"
} else { Check $false "у свайпа есть альтернативы" }

Write-Host "5) ⌫ стирает слово целиком, затем по букве"
TapKey 'BACKSPACE'
$t = TargetText; Check ($t -eq 'привет как ') "⌫ после свайпа: «$t»"
TapKey 'BACKSPACE'
$t = TargetText; Check ($t -eq 'привет как') "второй ⌫ — один символ: «$t»"

Write-Host "6) пунктуация и буквы по одной"
TapKey ','
$t = TargetText; Check ($t -eq 'привет как, ') "запятая с пробелом: «$t»"
TapKey 'д'; TapKey 'а'; TapKey '!'
$t = TargetText; Check ($t -eq 'привет как, да! ') "буквы и «!»: «$t»"
Swipe 'хорошо'
$t = TargetText; Check ($t -eq 'привет как, да! Хорошо ') "заглавная после «!»: «$t»"

Write-Host "7) English"
TapKey 'LANGUAGE'; LoadKeys $cfgA
Check ($script:keys['LAYOUT'] -eq 'en') "раскладка QWERTY"
Swipe 'hello' '05-swipe-hello'
Swipe 'world'
$t = TargetText; Check ($t.EndsWith('hello world ')) "набрано «$t»"
Check (Focused) "фокус в поле ввода"

Write-Host "8) цифры и Enter"
TapKey 'TOSYMBOLS'; LoadKeys $cfgA
Check ($script:keys['LAYOUT'] -eq 'sym1') "раскладка цифр"
ShotKb '06-symbols-kb'
TapKey '2'; TapKey '0'; TapKey '2'; TapKey '6'
TapKey 'ENTER'
TapKey 'TOLETTERS'; LoadKeys $cfgA
$t = TargetText; Check ($t.EndsWith("hello world 2026`r`n") -or $t.EndsWith("hello world 2026`n")) "цифры и перевод строки: «$($t.Replace("`r",'\r').Replace("`n",'\n'))»"
TapKey 'LANGUAGE'; LoadKeys $cfgA

Write-Host "9) щелчок мимо клавиатуры сбрасывает «⌫ стирает слово»"
Swipe 'мир'
[In]::Glide([System.Windows.Forms.Cursor]::Position.X, [System.Windows.Forms.Cursor]::Position.Y, 300, 150, 10, 5); [In]::Click(); Start-Sleep -Milliseconds 300
[In]::CtrlEnd() # курсор в конец текста
Start-Sleep -Milliseconds 200
$before = TargetText
TapKey 'BACKSPACE'
$t = TargetText; Check ($t.Length -eq $before.Length - 1) "после щелчка в поле ⌫ стёр один символ"

Write-Host "10) горячая клавиша Ctrl+Alt+K"
[In]::Hotkey(0x4B); Start-Sleep -Milliseconds 600
Check ([In]::Visible($app.Id).Count -eq 0) "клавиатура скрыта"
[In]::Hotkey(0x4B); Start-Sleep -Milliseconds 600
Check ([In]::Visible($app.Id).Count -eq 1) "клавиатура снова видна"
Check (Focused) "фокус остался в поле ввода"

Write-Host "11) перемещение и размер"
LoadKeys $cfgA
$w0 = $script:keys['WINDOW']
$g = K 'GRIP'
[In]::Glide([System.Windows.Forms.Cursor]::Position.X, [System.Windows.Forms.Cursor]::Position.Y, $g.X, $g.Y, 8, 5); [In]::Down(); Start-Sleep -Milliseconds 60; [In]::Glide($g.X, $g.Y, $g.X - 60, $g.Y - 40, 10, 10); Start-Sleep -Milliseconds 100; [In]::Up(); Start-Sleep -Milliseconds 400
LoadKeys $cfgA; $w1 = $script:keys['WINDOW']
Check (($w1.X -eq $w0.X - 60) -and ($w1.Y -eq $w0.Y - 40)) "окно передвинуто: $($w0.X),$($w0.Y) → $($w1.X),$($w1.Y)"
$r = K 'RESIZE'
[In]::Glide([System.Windows.Forms.Cursor]::Position.X, [System.Windows.Forms.Cursor]::Position.Y, $r.X, $r.Y, 8, 5); [In]::Down(); Start-Sleep -Milliseconds 60; [In]::Glide($r.X, $r.Y, $r.X + 50, $r.Y + 30, 10, 10); Start-Sleep -Milliseconds 100; [In]::Up(); Start-Sleep -Milliseconds 400
LoadKeys $cfgA; $w2 = $script:keys['WINDOW']
Check (($w2.W -eq $w1.W + 50) -and ($w2.H -eq $w1.H + 30)) "размер изменён: $($w1.W)x$($w1.H) → $($w2.W)x$($w2.H)"
Check (Focused) "фокус остался в поле ввода"
Swipe 'спасибо'
$t = TargetText; Check ($t.EndsWith('спасибо ')) "после изменения размера свайп работает: «$t»"
Start-Sleep -Seconds 2
$ini = Get-Content (Join-Path $cfgA 'settings.ini') -Raw -Encoding UTF8
Check ($ini -match "X=$($w2.X)" -and $ini -match "W=$($w2.W)") "положение и размер сохранены"

Write-Host "12) режим «без зажатия» (для тачпада)"
TapKey 'MODEBTN'
Start-Sleep -Milliseconds 300
ShotKb '07-click-mode-kb'
Swipe 'работа' '08-click-mode-swipe' -ClickMode
$t = TargetText; Check ($t.EndsWith('работа ')) "щелчок–ведение–щелчок: «$t»"
Start-Sleep -Seconds 2
Check ((Get-Content (Join-Path $cfgA 'settings.ini') -Raw) -match 'Mode=click') "режим запомнен"
TapKey 'MODEBTN'

Write-Host "13) нагрузка в покое"
Start-Sleep -Milliseconds 500
$app.Refresh(); $cpu0 = $app.TotalProcessorTime.TotalMilliseconds
Start-Sleep -Seconds 5
$app.Refresh(); $cpu1 = $app.TotalProcessorTime.TotalMilliseconds
$pct = ($cpu1 - $cpu0) / 5000 * 100
Check ($pct -lt 2) ("в покое 5 с — загрузка процессора {0:N2} %" -f $pct)
$app.Refresh(); Write-Host ("  память: {0:N1} МБ" -f ($app.WorkingSet64 / 1MB))

Write-Host "14) окно настроек"
$p2 = Start-Process $exe -ArgumentList @('--config-dir', "`"$cfgA`"", '--settings') -PassThru
$p2.WaitForExit(5000) | Out-Null
Check ($p2.HasExited) "второй экземпляр передал команду первому"
Start-Sleep -Seconds 2
$sw = @([In]::Visible($app.Id) | Where-Object { [In]::Title($_) -like '*настройки*' })
Check ($sw.Count -eq 1) "окно настроек открылось"
Shot '09-settings'
[In]::Key(0x1B); Start-Sleep -Milliseconds 700
Check (@([In]::Visible($app.Id) | Where-Object { [In]::Title($_) -like '*настройки*' }).Count -eq 0) "Esc закрыл настройки"

Write-Host "15) Блокнот"
$np = Start-Process notepad -PassThru
Start-Sleep -Seconds 3
$npw = (Get-Process -Id $np.Id -ErrorAction SilentlyContinue).MainWindowHandle
$fgPid = [In]::ForegroundPid()
Write-Host "  активно окно процесса $fgPid ($((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName))"
LoadKeys $cfgA
Swipe 'тест'
Swipe 'работает'
Start-Sleep -Milliseconds 400
$npText = [In]::FocusedText()
Write-Host "  в Блокноте: «$npText»"
Check ($npText -ne $null -and $npText.StartsWith('тест ')) "в Блокнот набрано «$npText»"
Shot '10-notepad'
$fgName = (Get-Process -Id ([In]::ForegroundPid()) -ErrorAction SilentlyContinue).ProcessName
Check ($fgName -like '*otepad*') "фокус остался в Блокноте ($fgName)"
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
[In]::SetForegroundWindow($target.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400

Write-Host "16) выход и повторный запуск: положение запомнено"
Check (Quit $app) "--quit закрыл программу"
$app = StartApp $cfgA; LoadKeys $cfgA; Start-Sleep -Milliseconds 500
$w3 = $script:keys['WINDOW']
Check (($w3.X -eq $w2.X) -and ($w3.Y -eq $w2.Y) -and ($w3.W -eq $w2.W)) "окно на прежнем месте: $($w3.X),$($w3.Y) $($w3.W)x$($w3.H)"
$uw = Join-Path $cfgA 'words-ru.txt'
Check ((Test-Path $uw) -and ((Get-Content $uw -Raw -Encoding UTF8) -match 'привет')) "выученные слова сохранены (words-ru.txt)"
Check (Quit $app) "выход"

Write-Host "17) светлая тема и прозрачность"
$cfgB = Join-Path $root 'cfgB'; New-Item -ItemType Directory -Force $cfgB | Out-Null
[IO.File]::WriteAllText((Join-Path $cfgB 'settings.ini'), "Theme=light`nOpacity=85`nLanguage=en`n", [Text.UTF8Encoding]::new($false))
$app = StartApp $cfgB; LoadKeys $cfgB; Start-Sleep -Milliseconds 800
Swipe 'keyboard' '11-light-theme'
$t = TargetText; Check ($t.EndsWith('keyboard ')) "светлая тема, English: «$t»"
Check (Quit $app) "выход"

Write-Host "18) испорченные настройки и неверные параметры"
$cfgC = Join-Path $root 'cfgC'; New-Item -ItemType Directory -Force $cfgC | Out-Null
[IO.File]::WriteAllBytes((Join-Path $cfgC 'settings.ini'), [byte[]](0, 0, 71, 0))
$app = StartApp $cfgC; LoadKeys $cfgC; Start-Sleep -Milliseconds 800
Check (-not $app.HasExited) "запустилась с испорченными настройками"
Check (Test-Path (Join-Path $cfgC 'settings.ini.broken')) "копия испорченного файла сохранена"
Check (Quit $app) "выход"
$p = Start-Process $exe -ArgumentList @('--bogus') -Wait -PassThru
Check ($p.ExitCode -eq 2) "неизвестный параметр → код 2"

foreach ($d in @($cfgA, $cfgB, $cfgC)) {
  $l = Join-Path $d 'log.txt'
  if (Test-Path $l) { Write-Host "---- журнал $([IO.Path]::GetFileName($d)) ----"; Get-Content $l -Encoding UTF8 | Write-Host }
}
try { $target.Kill() } catch { }
Copy-Item $shots -Destination (Join-Path ($env:GITHUB_WORKSPACE ?? '.') 'ui-shots') -Recurse -Force
if ($failures.Count -gt 0) { Write-Host "ПРОВАЛЕНО: $($failures.Count)" -ForegroundColor Red; $failures | ForEach-Object { Write-Host " - $_" }; exit 1 }
Write-Host "Все проверки пройдены" -ForegroundColor Green
