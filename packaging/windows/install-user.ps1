param()

$ErrorActionPreference = "Stop"

$sourceDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\AmiDiskLab"
$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$appShortcutPath = Join-Path $startMenuDirectory "AmiDiskLab.lnk"
$uninstallShortcutPath = Join-Path $startMenuDirectory "Uninstall AmiDiskLab.lnk"

New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $installDirectory -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$appShortcut = $shell.CreateShortcut($appShortcutPath)
$appShortcut.TargetPath = Join-Path $installDirectory "AmiDiskLab.exe"
$appShortcut.WorkingDirectory = $installDirectory
$appShortcut.IconLocation = (Join-Path $installDirectory "AmiDiskLab.exe") + ",0"
$appShortcut.Save()

$uninstallShortcut = $shell.CreateShortcut($uninstallShortcutPath)
$uninstallShortcut.TargetPath = "powershell.exe"
$uninstallShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' +
    (Join-Path $installDirectory "uninstall-user.ps1") + '"'
$uninstallShortcut.WorkingDirectory = $installDirectory
$uninstallShortcut.Save()

Write-Host "AmiDiskLab was installed for the current user."
Write-Host "Use the Start menu shortcut to launch it."
