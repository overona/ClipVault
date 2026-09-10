# End-to-end smoke test for ClipVault. Run from a normal (desktop) PowerShell window.
#   .\tools\e2e-test.ps1                 tests the Debug build
#   .\tools\e2e-test.ps1 -Exe .\dist\ClipVault.exe
# What it does:
#   1. Restarts ClipVault, pushes text / image / file items onto the clipboard.
#   2. Opens a small form with a text box (stands in for "the app you were working in").
#   3. Launches ClipVault a second time (single-instance signal) so the popup opens,
#      types a search term, presses Enter.
#   4. Asserts the matching item was pasted into the text box, saves tools\popup.png.
#   5. Repeats the popup with Shift+Enter (paste as plain text) and checks the same text arrives
#      and the item's UseCount reached 2.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\Debug\net9.0-windows\win-x64\ClipVault.exe'),
    [string]$Screenshot = (Join-Path $PSScriptRoot 'popup.png')
)

Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class E2E {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string FgTitle() { var t = new StringBuilder(256); GetWindowText(GetForegroundWindow(), t, 256); return t.ToString(); }
}
"@
[E2E]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

$Exe = (Resolve-Path $Exe).Path
$expected = "ClipVault e2e text`nline two`nline three"
$log = New-Object System.Collections.Generic.List[string]

function Snap($h, $path) {
    $r = New-Object E2E+RECT; [E2E]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -le 0) { return "screenshot skipped (no window rect)" }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save($path); $bmp.Dispose()
    return "screenshot saved: $path ($w x $hh)"
}

# 1. Fresh instance + seed the clipboard
Stop-Process -Name ClipVault -Force -ErrorAction SilentlyContinue; Start-Sleep 1
Start-Process $Exe -ArgumentList "--portable"; Start-Sleep 3
Set-Clipboard -Value "first seed item"; Start-Sleep 0.8
Set-Clipboard -Value $expected; Start-Sleep 0.8
$bmp = New-Object System.Drawing.Bitmap 320, 200
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::SteelBlue); $g.Dispose()
[System.Windows.Forms.Clipboard]::SetImage($bmp); Start-Sleep 0.8
Set-Clipboard -Path (Join-Path $PSScriptRoot '..\ClipVault.csproj'); Start-Sleep 0.8
Set-Clipboard -Value "first seed item"; Start-Sleep 0.8   # duplicate: must move up, not add

# 2. Target form
$form = New-Object System.Windows.Forms.Form
$form.Text = "ClipVault E2E target"; $form.Width = 600; $form.Height = 300
$form.StartPosition = "Manual"; $form.Left = 40; $form.Top = 40; $form.TopMost = $true
$box = New-Object System.Windows.Forms.TextBox; $box.Multiline = $true; $box.Dock = "Fill"; $form.Controls.Add($box)

$step = 0
$timer = New-Object System.Windows.Forms.Timer; $timer.Interval = 900
$timer.Add_Tick({
    $script:step++
    switch ($script:step) {
        1 { $box.Focus(); Start-Process $Exe -ArgumentList "--portable" }                                   # 3. open popup
        3 { $log.Add("popup foreground: '" + [E2E]::FgTitle() + "'"); $log.Add((Snap ([E2E]::GetForegroundWindow()) $Screenshot)) }
        4 { [System.Windows.Forms.SendKeys]::SendWait("e2e text") }
        5 { [System.Windows.Forms.SendKeys]::SendWait("{ENTER}") }
        7 { $log.Add("after paste foreground: '" + [E2E]::FgTitle() + "'"); $script:pasted = $box.Text }
        8 { $box.Clear(); $box.Focus(); Start-Process $Exe -ArgumentList "--portable" }                    # 5. plain-text round
        10 { [System.Windows.Forms.SendKeys]::SendWait("e2e text") }
        11 { [System.Windows.Forms.SendKeys]::SendWait("+{ENTER}") }
        13 { $log.Add("after plain paste foreground: '" + [E2E]::FgTitle() + "'"); $script:pastedPlain = $box.Text; $timer.Stop(); $form.Close() }
    }
})
$form.Add_Shown({ $timer.Start() })
[System.Windows.Forms.Application]::Run($form)

# 4. Report
$log | ForEach-Object { Write-Host $_ }
$history = Get-Content "$env:LOCALAPPDATA\ClipVault\history.json" -Raw | ConvertFrom-Json
Write-Host "history (top first):"
foreach ($i in $history) { Write-Host ("  {0,-6} use={1} {2}" -f $i.Kind, $i.UseCount, (($i.Text, $i.ImageFile, ($i.Files -join ';')) -ne $null | Select-Object -First 1)) }

$ok = $true
if (($pasted -replace "`r`n", "`n") -ne $expected) { Write-Host "FAIL: pasted text was [$pasted]" -ForegroundColor Red; $ok = $false }
if (($pastedPlain -replace "`r`n", "`n") -ne $expected) { Write-Host "FAIL: plain-text paste (Shift+Enter) was [$pastedPlain]" -ForegroundColor Red; $ok = $false }
$dups = @($history | Where-Object { $_.Text -eq "first seed item" }).Count
if ($dups -ne 1) { Write-Host "FAIL: expected 1 'first seed item', found $dups" -ForegroundColor Red; $ok = $false }
if ($history[0].Text -ne $expected -or $history[0].UseCount -lt 2) { Write-Host "FAIL: reused item is not at the top with UseCount>=2" -ForegroundColor Red; $ok = $false }
$logFile = "$env:LOCALAPPDATA\ClipVault\clipvault.log"
if (Test-Path $logFile) { Write-Host "clipvault.log:"; Get-Content $logFile | ForEach-Object { Write-Host "  $_" } }
if ($ok) { Write-Host "PASS" -ForegroundColor Green } else { exit 1 }
