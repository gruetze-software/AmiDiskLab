param()

$ErrorActionPreference = "Stop"

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\AmiDiskLab"
$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

Remove-Item -LiteralPath (Join-Path $startMenuDirectory "AmiDiskLab.lnk") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $startMenuDirectory "Uninstall AmiDiskLab.lnk") -Force -ErrorAction SilentlyContinue

Set-Location $env:TEMP
Remove-Item -LiteralPath $installDirectory -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "AmiDiskLab was removed from the current user profile."
