# Veröffentlichung auf GitHub

## Vor dem ersten öffentlichen Push

1. **Sichtbarkeit festlegen.** Ein privates Repository eignet sich für den
   unveränderten Arbeitsstand. Für ein öffentliches Repository müssen alle
   eingebundenen Test- und Bilddateien weitergegeben werden dürfen.
2. **Testdaten.** Die Tests erzeugen benötigte LHA-Archive zur Laufzeit. Reale
   Amiga-Software, ROMs und Diskettenabbilder gehören nicht in das Repository.
3. **Lizenz.** Der eigene AmiDiskLab-Quellcode wird unter der MIT-Lizenz
   veröffentlicht. Fremde Amiga-Software, ROMs, Diskettenabbilder sowie online
   abgerufene Metadaten und Medien sind davon nicht umfasst und dürfen nicht in
   das Repository aufgenommen werden.
4. **Zugangsdaten kontrollieren.** Die ScreenScraper-Entwicklerdaten liegen nur als
   verschlüsselte Cloudflare-Worker-Secrets vor. Optionale persönliche Zugangsdaten
   speichert die Anwendung außerhalb des Repositorys unter `%APPDATA%\AmiDiskLab`.
   Keine Geheimnisse, Dateien aus diesem Verzeichnis oder Diagnoseprotokolle zum
   Repository hinzufügen. Die Einrichtung des Workers steht unter
   `server/AmiDiskLab.ScreenScraperProxy/README.md`.

## Repository über die GitHub-Webseite anlegen

1. Auf GitHub **New repository** wählen.
2. Als Namen beispielsweise `AmiDiskLab` eintragen.
3. Für die öffentliche Veröffentlichung **Public** wählen.
4. README, `.gitignore` und Lizenz auf GitHub nicht automatisch erzeugen lassen;
   README und Ignore-Datei sind lokal bereits vorhanden.
5. Das leere Repository erstellen und dessen HTTPS-Adresse kopieren.

## Lokales Repository verbinden und hochladen

Im Projektordner in PowerShell ausführen. Die Beispieladresse muss durch die auf
GitHub angezeigte Adresse ersetzt werden:

```powershell
git remote add origin https://github.com/DEIN-NAME/AmiDiskLab.git
git push -u origin github-public:main
```

Falls `origin` bereits existiert:

```powershell
git remote set-url origin https://github.com/DEIN-NAME/AmiDiskLab.git
git push -u origin github-public:main
```

Der lokale Zweig `github-public` enthält den bereinigten Veröffentlichungsstand.
Der ältere lokale Zweig `main` bleibt nur als Sicherung erhalten und darf wegen
des früher darin enthaltenen Testarchivs nicht zu GitHub gepusht werden. Daher
kein `git push --all` verwenden. Weitere öffentliche Änderungen werden auf
`github-public` committed; sie lassen sich anschließend mit `git push` übertragen.

GitHub fragt bei HTTPS gegebenenfalls nach der Anmeldung im Browser oder einem
Personal Access Token. Ein GitHub-Passwort kann nicht als Git-Passwort verwendet
werden.

## Nach dem Push

- Unter **Actions** prüfen, ob „Build and test“ auf Windows und Linux erfolgreich
  durchläuft.
- Unter **Settings → General** die gewünschten Merge- und Release-Einstellungen
  wählen.
- Bei einem öffentlichen Repository Topics wie `amiga`, `adf`, `avalonia` und
  `dotnet` ergänzen.
- Der Workflow **Create release** baut automatisch vier eigenständige Pakete:
  Windows x64, Linux x64, macOS Intel und macOS Apple Silicon. Er kann unter
  **Actions → Create release → Run workflow** zunächst probeweise ausgeführt
  werden; die Pakete erscheinen dann als Workflow-Artefakte.
- Eine öffentliche Veröffentlichung wird durch einen Versions-Tag gestartet:

```powershell
git tag -a v1.1 -m "AmiDiskLab 1.1"
git push origin v1.1
```

Nach erfolgreichem Test erstellt GitHub automatisch das Release **AmiDiskLab 1.1**
und hängt alle vier Archive an. Die Pakete enthalten die .NET-Laufzeit. Die
macOS-Pakete sind nicht mit einem Apple-Entwicklerzertifikat signiert; macOS kann
daher beim ersten Start eine Sicherheitsbestätigung verlangen.
