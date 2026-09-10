# Captures the two promo screenshots used on veronasolutions.net/clipvault (light and dark).
#   .\tools\promo-shots.ps1 [-Exe <ClipVault.exe>] [-OutDir <folder>]
# Restarts the given ClipVault, seeds the clipboard with representative items (text with a link, a code
# snippet, an image, a file list), shoots the popup, flips the theme through settings.json, shoots again,
# then restores the user's theme. Needs an unlocked desktop.
param(
    [string]$Exe = "$env:LOCALAPPDATA\Programs\ClipVault\ClipVault.exe",
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\VS\assets\promo\clipvault'),
    [string]$Keys = '{DOWN}'   # skip past any pinned item so the seeded text (with its link) is previewed
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$settingsPath = "$env:LOCALAPPDATA\ClipVault\settings.json"
$capture = Join-Path $PSScriptRoot 'capture-window.ps1'
$OutDir = (Resolve-Path $OutDir).Path

function Restart-ClipVault([string]$theme) {
    Stop-Process -Name ClipVault -Force -ErrorAction SilentlyContinue; Start-Sleep 1
    $s = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($null -eq $s.PSObject.Properties['Theme']) { $s | Add-Member -NotePropertyName Theme -NotePropertyValue $theme } else { $s.Theme = $theme }
    $s | ConvertTo-Json -Depth 5 | Set-Content $settingsPath -Encoding utf8
    Start-Process $Exe -ArgumentList '--portable'; Start-Sleep 3
}

$original = (Get-Content $settingsPath -Raw | ConvertFrom-Json).Theme
if (-not $original) { $original = 'System' }

# Light theme first, then seed the history (newest ends up on top and selected).
Restart-ClipVault 'Light'
Set-Clipboard -Path (Join-Path $PSScriptRoot '..\README.md'), (Join-Path $PSScriptRoot '..\LICENSE'); Start-Sleep 0.8
$bmp = New-Object System.Drawing.Bitmap 640, 400
$g = [System.Drawing.Graphics]::FromImage($bmp)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)), ([System.Drawing.Point]::new(640,400)), ([System.Drawing.Color]::FromArgb(255,30,64,175)), ([System.Drawing.Color]::FromArgb(255,147,197,253))
$g.FillRectangle($brush, 0, 0, 640, 400); $g.Dispose()
[System.Windows.Forms.Clipboard]::SetImage($bmp); Start-Sleep 0.8
Set-Clipboard -Value @'
public static bool LooksLikeCode(string? text)
{
    if (string.IsNullOrWhiteSpace(text)) return false;
    var lines = text.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
    return lines.Length > 1;
}
'@; Start-Sleep 0.8
Set-Clipboard -Value "Meeting moved to Thursday 10:00. Agenda and slides: https://veronasolutions.net/clipvault/ - reply to info@veronasolutions.net if you can't make it."; Start-Sleep 0.8

& $capture -Exe $Exe -Out (Join-Path $OutDir 'screen-light.png') -Keys $Keys
Start-Sleep 0.5

Restart-ClipVault 'Dark'
& $capture -Exe $Exe -Out (Join-Path $OutDir 'screen-dark.png') -Keys $Keys

Restart-ClipVault $original
Write-Host "Theme restored to $original. Shots in $OutDir" -ForegroundColor Green
