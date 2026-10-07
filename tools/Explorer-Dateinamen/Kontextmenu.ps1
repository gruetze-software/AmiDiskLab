param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$keys = @(
    'HKCU:\Software\Classes\Directory\shell\RetroDiskCleanNames',
    'HKCU:\Software\Classes\Directory\Background\shell\RetroDiskCleanNames'
)
if ($Remove) {
    foreach ($key in $keys) {
        if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }
    }
    Write-Host 'Kontextmenue entfernt.'
    exit
}
$script = Join-Path $PSScriptRoot 'Bereinigen.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw 'Bereinigen.ps1 fehlt.' }
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($key in $keys) {
    $argument = if ($key -like '*\Background\*') { '%V' } else { '%1' }
    New-Item -Path "$key\command" -Force | Out-Null
    Set-Item -LiteralPath $key -Value 'Dateinamen bereinigen (Disk behalten)'
    # A trailing slash plus dot keeps drive-root paths safe in quoted arguments.
    Set-Item -LiteralPath "$key\command" -Value ('"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}" -Folder "{2}\."' -f $powershell, $script, $argument)
}
Write-Host 'Kontextmenue installiert. Diesen Script-Ordner bitte nicht verschieben.'
