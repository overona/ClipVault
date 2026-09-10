# Builds the shareable single-file ClipVault.exe into .\dist
# Usage:  .\build.ps1            (Release, win-x64, self-contained, single file)
#         .\build.ps1 -Zip       (also produce dist\ClipVault-<version>.zip for sharing)
param([switch]$Zip)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

Get-Process ClipVault -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dist*" } | Stop-Process -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $root 'ClipVault.csproj') -c Release -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $dist 'ClipVault.exe'
$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "`nBuilt $exe ($size MB)" -ForegroundColor Green

if ($Zip) {
    $version = (Select-Xml -Path (Join-Path $root 'ClipVault.csproj') -XPath '//Version').Node.InnerText
    $zipPath = Join-Path $dist "ClipVault-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path $exe, (Join-Path $root 'README.md') -DestinationPath $zipPath
    Write-Host "Zipped to $zipPath" -ForegroundColor Green
}
