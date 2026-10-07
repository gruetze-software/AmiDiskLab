param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [switch]$PreviewOnly
)

$ErrorActionPreference = 'Stop'

function Get-CleanName([string]$Name) {
    $extension = [IO.Path]::GetExtension($Name)
    $stem = [IO.Path]::GetFileNameWithoutExtension($Name)
    # Preserve disk labels exactly; remove the other metadata groups.
    $stem = [regex]::Replace($stem, '\[[^\]]*\]', '')
    $stem = [regex]::Replace($stem, '\([^)]*\)', {
        param($match)
        if ($match.Value -match '^\(Disk\s+\d+\s+of\s+\d+\)$') {
            return ' ' + $match.Value
        }
        return ''
    }, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $stem = [regex]::Replace($stem, '(?i)\s+v\d+(?:[._]\d+)*[a-z]?(?=\s*(?:\(Disk\s|$))', '')
    $stem = [regex]::Replace($stem, '\s+', ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($stem)) { return $Name }
    return $stem + $extension
}

try {
    $directory = Get-Item -LiteralPath $Folder
    if (-not $directory.PSIsContainer) { throw 'Bitte einen Ordner angeben.' }
    $files = @(Get-ChildItem -LiteralPath $directory.FullName -File -Force)
    $plan = @($files | ForEach-Object {
        $newName = Get-CleanName $_.Name
        if ($newName -cne $_.Name) {
            [pscustomobject]@{ File = $_; Old = $_.Name; New = $newName }
        }
    })
    if ($plan.Count -eq 0) {
        Write-Host 'Keine Dateinamen zu bereinigen.'
    } else {
        $targets = @{}
        foreach ($item in $plan) {
            if ($targets.ContainsKey($item.New)) {
                throw "Mehrere Dateien ergeben denselben Namen: $($item.New). Es wurde nichts umbenannt."
            }
            $targets[$item.New] = $true
            $target = Join-Path $directory.FullName $item.New
            if (($item.Old -ine $item.New) -and (Test-Path -LiteralPath $target)) {
                throw "Ziel existiert bereits: $($item.New). Es wurde nichts umbenannt."
            }
            Write-Host "`nALT: $($item.Old)"
            Write-Host "NEU: $($item.New)" -ForegroundColor Cyan
        }
        if (-not $PreviewOnly) {
            $answer = Read-Host "`n$($plan.Count) Datei(en) umbenennen? J eingeben"
            if ($answer -ieq 'J') {
                foreach ($item in $plan) {
                    Rename-Item -LiteralPath $item.File.FullName -NewName $item.New -ErrorAction Stop
                }
                Write-Host 'Fertig.' -ForegroundColor Green
            } else { Write-Host 'Abgebrochen.' }
        }
    }
} catch {
    Write-Host "Fehler: $($_.Exception.Message)" -ForegroundColor Red
    if ($PreviewOnly) { throw }
    Write-Host 'Bei einem Fehler waehrend des Umbenennens koennen einzelne Dateien bereits umbenannt sein.'
} finally {
    if (-not $PreviewOnly) { [void](Read-Host 'Enter zum Schliessen') }
}
