# Тестовое окно с полем ввода: всё, что в него набрано, сразу записывается в файл.
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$f = New-Object System.Windows.Forms.Form
$f.Text = 'SK-Target'
$f.StartPosition = 'Manual'
$f.Location = New-Object System.Drawing.Point($wa.X, $wa.Y)
$f.Size = New-Object System.Drawing.Size($wa.Width, [int]($wa.Height * 0.42))
$tb = New-Object System.Windows.Forms.TextBox
$tb.Multiline = $true
$tb.Dock = 'Fill'
$tb.Font = New-Object System.Drawing.Font('Segoe UI', 16)
$tb.Add_TextChanged({ [IO.File]::WriteAllText($Out, $tb.Text, [Text.UTF8Encoding]::new($false)) })
$f.Controls.Add($tb)
$f.Add_Shown({ $f.Activate(); $tb.Focus() })
[System.Windows.Forms.Application]::Run($f)
