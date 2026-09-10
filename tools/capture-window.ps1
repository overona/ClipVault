# Opens the ClipVault popup (via the single-instance signal) and saves a screenshot of it.
#   .\tools\capture-window.ps1 [-Exe path\to\ClipVault.exe] [-Out tools\popup.png]
# The popup hides when it loses focus, so the capture happens inside this same script run.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\Debug\net9.0-windows\win-x64\ClipVault.exe'),
    [string]$Out = (Join-Path $PSScriptRoot 'popup.png')
)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Cap {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr FindVisible(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var t = new StringBuilder(64); GetWindowText(h, t, 64);
      if (p == pid && IsWindowVisible(h) && t.ToString() == "ClipVault") { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
}
"@
[Cap]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing

$proc = Get-Process ClipVault -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Start-Process (Resolve-Path $Exe).Path; Start-Sleep 3; $proc = Get-Process ClipVault | Select-Object -First 1 }
Start-Process (Resolve-Path $Exe).Path   # second instance = "show popup" signal
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 30 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $h = [Cap]::FindVisible($proc.Id) }
if ($h -eq [IntPtr]::Zero) { throw "ClipVault popup did not appear" }
Start-Sleep -Milliseconds 400
$r = New-Object Cap+RECT; [Cap]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
$bmp.Save($Out); $bmp.Dispose()
Write-Host "Saved $Out ($($r.R - $r.L) x $($r.B - $r.T))"
