# Generates Assets\ClipVault.ico (16..256 px, 32-bit BMP entries) using System.Drawing.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'ClipVault.ico'

function RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $board = [Drawing.Color]::FromArgb(255, 37, 99, 235)    # blue
    $clip  = [Drawing.Color]::FromArgb(255, 30, 58, 138)    # navy
    $paper = [Drawing.Color]::FromArgb(255, 255, 255, 255)
    $bb = New-Object Drawing.SolidBrush $board
    $cb = New-Object Drawing.SolidBrush $clip
    $pb = New-Object Drawing.SolidBrush $paper
    $r = [Math]::Max(1, $s * 0.10)
    $g.FillPath($bb, (RoundedPath ($s*0.10) ($s*0.12) ($s*0.80) ($s*0.84) $r))
    $g.FillPath($pb, (RoundedPath ($s*0.22) ($s*0.30) ($s*0.56) ($s*0.58) ([Math]::Max(1,$s*0.05))))
    $g.FillPath($cb, (RoundedPath ($s*0.34) ($s*0.03) ($s*0.32) ($s*0.20) ([Math]::Max(1,$s*0.05))))
    $lb = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 148, 163, 184))
    foreach ($y in 0.42, 0.55, 0.68) {
        $w = if ($y -eq 0.68) { 0.26 } else { 0.40 }
        $g.FillPath($lb, (RoundedPath ($s*0.30) ($s*$y) ($s*$w) ($s*0.07) ([Math]::Max(0.5,$s*0.03))))
    }
    $g.Dispose()
    return $bmp
}

function BmpEntry([Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $s)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $maskStride = [int](([Math]::Ceiling($s / 32.0)) * 4)
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([int32]40); $bw.Write([int32]$s); $bw.Write([int32]($s*2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int32]0); $bw.Write([int32]($s*$s*4 + $maskStride*$s)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $s * 4) }
    $bw.Write((New-Object byte[] ($maskStride * $s)))
    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$entries = @()
foreach ($sz in $sizes) { $b = Draw $sz; $entries += ,(@{ Size = $sz; Bytes = (BmpEntry $b) }); $b.Dispose() }

$fs = [IO.File]::Create($out)
$w = New-Object IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int32]$e.Bytes.Length); $w.Write([int32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e.Bytes) }
$w.Flush(); $fs.Close()
Write-Host "Wrote $out ($((Get-Item $out).Length) bytes)"
