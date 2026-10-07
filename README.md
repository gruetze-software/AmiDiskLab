# AmiDiskLab

Avalonia-Anwendung für Amiga-Software-Sammlungen. Der aktuelle Stand erkennt ADF,
HFE, DMS, IPF und LHA anhand ihrer Dateiendung, markiert bytegleiche Dateien mit
SHA-256 und erstellt aus LHA-Archiven ein 880-KiB-FFS-Datenimage (DOS\1).
Für einen Amiga 500 mit Kickstart 1.3 gibt es außerdem einen eigenen Bootexport
mit OFS (DOS\0) für geeignete AmigaDOS-Programme.

> **Projektstatus:** Version 1.0. Vor dem Einsatz mit wichtigen Images
> Sicherungskopien verwenden und erzeugte ADFs zunächst im Emulator oder auf einer
> separaten Kopie testen.

Hinweise zum erstmaligen Anlegen und Hochladen eines GitHub-Repositorys stehen in
[docs/GITHUB_PUBLISHING.md](docs/GITHUB_PUBLISHING.md).

GitHub Releases enthalten eigenständige Downloads für Windows x64, Linux x64
sowie macOS auf Intel- und Apple-Silicon-Prozessoren. Eine separate .NET-Installation
ist für diese Pakete nicht erforderlich.

## Lizenz

AmiDiskLab wird unter der [MIT-Lizenz](LICENSE) veröffentlicht. Fremde Amiga-
Software, ROMs, Diskettenabbilder und online abgerufene Metadaten oder Medien
sind nicht Bestandteil dieser Lizenz und gehören nicht zum Repository.

## Entwickeln

Voraussetzung: .NET SDK 10.0 (Ziel aller Projekte: net10.0).

```powershell
dotnet restore AmiDiskLab.slnx
dotnet build AmiDiskLab.slnx
dotnet test AmiDiskLab.slnx --blame-hang-timeout 60s
dotnet run --project RetroDisk.App/AmiDiskLab.App.csproj
```

Die Tests verwenden ausschließlich eindeutig benannte temporäre Verzeichnisse.
Es werden keine festen Dateien auf dem Desktop erzeugt oder überschrieben.
Der Workflow unter `.github/workflows/build.yml` ist für Windows und Linux
vorbereitet; er wird erst nach Bereitstellung des Repositorys auf GitHub ausgeführt.

## Aufbau

- **Core:** Modelle und Schnittstellen für Scan, Extraktion, Writer und Konvertierung.
- **Infrastructure:** Dateisystemzugriffe, LHA-Adapter, Hst.Amiga-Writer und Ablauf
  „Archiv zu ADF“. Der Ablauf besitzt und bereinigt seinen temporären Quellordner.
- **App:** Dialoge und darstellbarer Zustand. Die Abhängigkeiten werden in
  `App.axaml.cs` zusammengesetzt und dem ViewModel übergeben.
- **Tests:** Inhaltsvergleiche nach erneutem Mounten, Grenzgrößen, Pfadschutz,
  Fehler-/Abbruchbehandlung, Dubletten und ViewModel-Zustände.

## A500 mit Kickstart 1.3 und Gotek

1. Über die Zielsystem-Schaltfläche Kickstart (1.3, 2.0 oder 3.1) und RAM
   (1, 2, 4 oder 8 MiB) des A500 einstellen. Die Auswahl wird im Benutzerprofil
   gespeichert. Der Prozessor bleibt für dieses A500-Profil ein Motorola 68000.
2. Einen Ordner mit einem LHA-Archiv auswählen und das Archiv in der Liste markieren.
3. **Create A500 Boot ADF** wählen und einen Volume-Namen sowie den Speicherort
   für das neue ADF festlegen.
4. Das erstellte ADF auf den USB-Stick des Gotek übertragen und dort auswählen.

Der Bootexport funktioniert für Archive mit genau einem erkennbaren AmigaDOS-Hunk-
Programm, das sich mit dem Programmpfad aus `S/Startup-Sequence` starten lässt.
Er schreibt den Standard-DOS-Bootblock, OFS (`DOS\0`) und ein Startskript.
Falls bereits ein Startskript vorhanden ist, wird es nicht überschrieben.
Das Programm selbst muss mit Kickstart 1.3, 68000 und dem vorhandenen RAM
kompatibel sein. Der Export kann diese Laufzeitvoraussetzungen nicht beweisen.
Eine Prüfung auf echter Hardware oder einem passend konfigurierten Emulator steht
noch aus.

Ein Archiv mit einer `.Slave`-Datei ist eine WHDLoad-Installation. WHDLoad
benötigt mindestens Kickstart 2.0. Auch mit entsprechend umgeschaltetem A500
entsteht aus einem solchen Paket durch Hinzufügen eines Bootblocks noch keine
startbare Gotek-Diskette: Der Slave benötigt eine WHDLoad-Umgebung und kann
nicht direkt aus der AmigaDOS-Startup-Sequence gestartet werden. Dafür wird ein
originales, bereits bootfähiges ADF der Diskettenversion benötigt; dieses kann direkt auf dem
Gotek verwendet werden. AmiDiskLab erzeugt es nicht aus der WHDLoad-Installation.
Andere NDOS-Spielarchive ohne AmigaDOS-Programm können ebenfalls nicht
automatisch in einen DOS-Bootdatenträger umgewandelt werden.

Für WHDLoad-Archive mit eingebetteten Diskettenabbildern gibt es **Extract disk
ADFs**. Die Funktion übernimmt Dateien mit Diskettennamen (`Disk.1`, `Disk.2`
usw. oder `.adf`) und exakt 901.120 Byte unverändert als separate ADFs in
einen gewählten Ordner. Vorhandene Dateien werden nicht überschrieben. Dies ist
ein Export der vorhandenen Bytes, keine Rekonstruktion oder Startgarantie:
WHDLoad-Diskettenabbilder können verändert sein, und Mehrdiskettenspiele
benötigen alle zugehörigen Images. Die Kompatibilität mit dem Zielsystem muss
anschließend am Gotek oder in einem passenden Emulator geprüft werden.

Die historischen Verzeichnisnamen `RetroDisk.*` bleiben vorerst erhalten.
Assemblynamen und Namespaces lauten `AmiDiskLab.*`.

AmiDiskLab merkt sich den zuletzt erfolgreich gewählten Ordner unter
`%APPDATA%\AmiDiskLab\last-folder.json` und scannt ihn beim nächsten Start
automatisch, sofern er noch vorhanden ist. Die Liste ist nach dem Scan
alphabetisch nach angezeigtem Namen sortiert. Ein Klick auf einen Spaltenkopf
sortiert nach dieser Spalte; erneutes Klicken kehrt die Richtung um.

## Software-Metadaten

Mehrteilige Disketten-Sets mit gleichem Spielnamen und Kennungen wie `(A)` bis
`(F)` oder `(Disk 1 of 3)` bis `(Disk 3 of 3)` im selben Ordner erscheinen als
ein Eintrag mit
Diskettenanzahl. Die Dateien bleiben einzeln auf dem Datenträger; Metadaten
werden für alle Disketten des Sets gespeichert. Ein Doppelklick auf den Eintrag
öffnet den Metadatendialog. Das Rechtsklickmenü der Liste enthält Metadaten-
und ADF-Aktionen sowie **Show Cover**, **Show Screenshot** und **Open Path**.
Die Bildaktionen öffnen lokal gespeicherte Bilder mit der registrierten App;
**Open Path** markiert bei einem einzelnen Eintrag die Datei im Windows-Explorer;
bei einem Disketten-Set öffnet es den gemeinsamen Ordner. Ein Doppelklick auf Cover oder
Screenshot im Metadatendialog öffnet die Bilddatei ebenfalls mit der
registrierten App.

Eine Datei in der Liste auswählen und im Kontextmenü **Edit metadata** öffnen. Für LHA-Archive
wird dabei die enthaltene `ReadMe` gelesen und, soweit eindeutig, ein Titel,
ein Studio/Publisher und die Kategorie Game, Demo oder Program vorgeschlagen.
Die Vorschläge müssen vor dem Speichern geprüft werden: Eine WHDLoad-ReadMe
kann auch den Autor des Installers nennen. Für ADFs nutzt AmiDiskLab zuerst
eine passende `.rp9`-Begleitdatei (Titel, Typ und Publisher/Developer); ohne
RP9 werden Titel, Studio und Demo/Game-Kategorie vorsichtig aus strukturierten
Dateinamen vorgeschlagen. Der Dialog zeigt die Quelle an. Weitere Kategorien
sowie Titel und Studio lassen sich manuell setzen. Für HFE, DMS und IPF erfolgt
die Eingabe derzeit manuell.

Die Angaben werden unter `%APPDATA%\AmiDiskLab\metadata.json` anhand des
vollständigen Dateipfads gespeichert. Die ROMs und ihre Ordner bleiben
unverändert. Nach Verschieben oder Umbenennen einer Datei muss ihre Zuordnung
derzeit neu erfasst werden.

Für einen ausgewählten ADF-Eintrag kann **Find game metadata (ScreenScraper.fr)** einen gezielten
Online-Abgleich ausführen. AmiDiskLab verwendet dafür den von Grütze-Software
betriebenen ScreenScraper-Proxy. Unter **Settings → ScreenScraper** können optional
die persönliche ScreenScraper-Benutzer-ID und das Benutzerpasswort eingegeben werden. AmiDiskLab
speichert diese beiden Angaben mit Windows-DPAPI für das aktuelle Benutzerkonto
verschlüsselt unter `%APPDATA%\AmiDiskLab\screenscraper-credentials.dat` und
lädt sie beim nächsten Start automatisch. Werden beide Felder geleert und
gespeichert, entfernt AmiDiskLab diese Datei. Eine persönliche ScreenScraper-Anmeldung
ist für die Suche nicht erforderlich; sie ordnet Anfragen dem eigenen Konto und dessen
Limits beziehungsweise Mitgliedsvorteilen zu. Unter Linux und macOS werden persönliche
ScreenScraper-Daten nur für die laufende Sitzung verwendet und nicht gespeichert. Der
Aufruf sendet CRC32-, MD5- und SHA-1-Hash, Dateigröße, Dateinamen und gegebenenfalls
die persönlichen Zugangsdaten über den Proxy an die ScreenScraper-API; die ADF-Datei
selbst wird nicht hochgeladen. Die ScreenScraper-Entwicklerdaten liegen ausschließlich
als verschlüsselte Geheimnisse beim Proxy und werden nicht mit AmiDiskLab ausgeliefert. Zusätzlich
werden über den bereinigten Dateinamen alternative Spiele gesucht. Bei einem
erfolglosen Hash-Abgleich entfernt die Namenssuche Klammerzusätze wie
Jahr, Hersteller und Diskettennummer; beispielsweise wird
`Puggsy (1994)(Psygnosis)(Disk 4 of 4)` als `Puggsy` gesucht. Bleibt ein
vollständiger Titel mit einem durch ` - ` getrennten Untertitel ohne Ergebnis,
wird zusätzlich der Haupttitel versucht. Eine abschließende Zahl wie bei
`Rainbow Islands - The Story of Bubble Bobble 2` bleibt dabei erhalten. Die Treffer
erscheinen in einer Auswahlliste: Ein SHA-1- und Größen-Treffer steht zuerst;
Namensfunde sind ausdrücklich als nicht hashverifiziert markiert. Erst nach
Auswahl, Prüfung und **Save** werden Daten in den lokalen Katalog übernommen.
Ohne Online-Treffer bleiben RP9-, ReadMe- und Dateinamen-Vorschläge verfügbar.
Online-Abfragen erfolgen nur auf ausdrücklichen Klick, nicht beim Ordnerscan.
Bei einem Treffer werden regionale Titel, Veröffentlichungsdatum, Entwickler,
Publisher, Publisher-Logo, Genre, Beschreibung sowie Cover und Spiel-Screenshot übernommen, soweit
vorhanden. Beide Bilder werden im Dialog angezeigt und unter
`%APPDATA%\AmiDiskLab\covers` lokal gespeichert. Der Proxy ersetzt API-Medienlinks
durch kurzlebige, verschlüsselte Download-Links; Entwicklerdaten gelangen dadurch
nicht in den Metadatenkatalog. Der Metadatendialog öffnet sofort nach der Trefferauswahl
und zeigt den Download mit Wartemauszeiger an. Anschließend werden die Links in
lokale Dateipfade umgewandelt.
Beim Speichern prüft die App,
dass alle eingetragenen Bilder heruntergeladen wurden; danach sind sie auch
offline im Metadatendialog verfügbar. Fehlt der Titel oder das Datum in der API-Antwort, wird
dafür ein klar gekennzeichneter Vorschlag aus dem Dateinamen verwendet.

Für Szene-Demos gibt es im Rechtsklickmenü **Find scene metadata (Demozoo)**.
Die Suche schlägt einen aus dem Dateinamen bereinigten Titel vor; der Suchname
kann vor dem Abgleich geändert werden. Alternativ kann man die numerische
Demozoo-Produktions-ID oder einen Demozoo-Produktionslink eingeben. Demozoo liefert nur exakte Titeltreffer,
die auf Amiga-Plattformen gefiltert und bei mehreren Ergebnissen zur Auswahl
angezeigt werden. Titel, Scene-Gruppe, Produktionstyp, Erscheinungsdatum und
vorhandene Szenebilder werden als überprüfbarer Vorschlag übernommen. Das erste
Bild dient als Cover-Ersatz, ein zweites als Screenshot. Gibt es nur ein Bild,
wird es an beiden Stellen angezeigt. Die Bilder werden für die Offline-Nutzung
lokal gespeichert. Bei bereits gespeicherten Demozoo-Einträgen kann ein
vorhandener Screenshot beim nächsten Öffnen des Metadatendialogs ebenfalls als
Cover-Ersatz übernommen werden.
Ein Dateihash wird hierbei nicht verifiziert; die Originaldatei bleibt unverändert.

Die ADF-Funktionen liegen im Rechtsklick-Untermenü **ADF actions**. Kickstart/RAM und
ScreenScraper-Zugang werden gemeinsam unter **Settings** mit getrennten Reitern
bearbeitet. Dort lassen sich auch die bevorzugte Sprache für Beschreibungen
und die Region für Titel, Erscheinungsdaten und Cover wählen. Falls ein Eintrag
in der gewählten Sprache oder Region fehlt, wird ein verfügbarer Ersatz
verwendet. Zielplattform und Metadatenpräferenzen werden dauerhaft gespeichert;
API-Zugangsdaten werden für das aktuelle Windows-Benutzerkonto verschlüsselt
gespeichert.

Das Genre kann im Metadatendialog manuell bearbeitet werden und erscheint als
eigene sortierbare Spalte in der Hauptliste.
Der Publisher wird ebenfalls in einer sortierbaren Spalte angezeigt. Liefert
ScreenScraper ein farbiges oder monochromes Publisher-Logo, wird es offline
gespeichert und im Metadatendialog sowie in der Publisher-Spalte angezeigt.
Enthält die Spielantwort nur die ScreenScraper-Firmen-ID, lädt AmiDiskLab das
farbige Logo über `mediaCompagnie.php` nach und verwendet nur bei fehlender
Farbversion das monochrome Logo. Ein heller Hintergrund hält transparente,
dunkle Logos auch im Dark Theme lesbar.
Duplikate besitzen keine eigene Spalte mehr; ihre komplette Zeile erscheint rot.

## Sicherheits- und Verhaltensregeln

- Alle drei Writer-Einstiegspunkte verwenden Hst.Amiga 0.6.238.
- Das Image wird zunächst in einem festen 901.120-Byte-Puffer aufgebaut.
  Erst nach erfolgreichem Schreiben, Flush und Schließen wird eine temporäre
  Datei neben dem Ziel geschrieben und anschließend an die Zielposition verschoben.
  Abbruch oder Schreibfehler vor dem Ersetzen erhalten die vorhandene Zieldatei.
  Dies ist keine Garantie gegen Stromausfall oder konkurrierende externe Dateizugriffe.
- Der Writer lehnt Ziele innerhalb des Quellbaums sowie symbolische Links und
  Junctions in den verwendeten Quell-/Zielpfaden ab. Die Linkprüfung ist kein
  Schutz gegen einen gleichzeitig durch einen anderen Prozess ausgetauschten Pfad.
- Datei- und Volume-Namen: 1–30 Latin-1-Zeichen, ohne Steuerzeichen, `/`, `\` oder `:`.
  DOS\1-Namenskollisionen werden vorab abgelehnt.
- Die Kapazitätsprüfung berücksichtigt Daten-, Header-, Verzeichnis- und
  Erweiterungsblöcke. Sie ist eine Vorprüfung; Hst.Amiga kann am Kapazitätsrand
  zusätzlichen Arbeitsraum benötigen. Auch solche Fehler erhalten das alte Ziel.
- Die Extraktion prüft das gesamte Manifest vor dem Schreiben und überschreibt
  keine bestehenden Dateien. Absolute Pfade, Traversierung und problematische
  Windows-Pfadnamen werden plattformübergreifend abgelehnt. C-String-Endnullen
  bleiben für bestehende LHA-Dateien unterstützt.
- Scans ignorieren Verzeichnislinks. Nicht lesbare Dateien oder Ordner führen zu
  einem sichtbaren Scanfehler; die vorherige Ergebnisliste bleibt erhalten.
- Es läuft höchstens ein Scan oder eine Konvertierung gleichzeitig. „Cancel“
  fordert einen kooperativen Abbruch an. Das Schließen während eines Vorgangs
  fordert ebenfalls Abbruch an; nach dessen Ende kann das Fenster geschlossen werden.

## Diagnose eines Hängers

Optional vor dem Start einen beschreibbaren Logpfad setzen, zum Beispiel:

```powershell
$env:AMIDISKLAB_LOG_PATH = Join-Path $env:TEMP 'AmiDiskLab-diagnostic.log'
dotnet run --project RetroDisk.App/AmiDiskLab.App.csproj
```

Das Log erfasst Öffnen und Schließen von Dateien, Verzeichniswechsel,
Volume-Flush und Volume-Schließen auch im Release-Build. „Finished“ erscheint
nach dem Dateistream-Schließen. Das Log wird angehängt und kann lokale Pfade
enthalten. Nach der Diagnose die Umgebungsvariable wieder entfernen.

## Bewusste Grenzen und nächste Schritte

- Der LHA-Decoder lädt synchron und kann während dieses Bibliotheksaufrufs nicht
  abgebrochen werden. Er läuft außerhalb des UI-Threads. Es gibt derzeit keine
  harte Speicher-/Dekompressionsquote für nicht vertrauenswürdige Archive.
- Amiga-Schutzbits, Kommentare und Originalzeitstempel werden noch nicht erhalten.
- Die automatische Prüfung nutzt Hst.Amiga auch zum Wiederöffnen. Eine unabhängige
  Prüfung im Emulator oder auf echter Hardware steht weiterhin aus.
- Archiv-, Writer- und WHDLoad-Prüfungen verwenden ausschließlich zur Laufzeit
  erzeugte synthetische Testarchive. Reale Softwarearchive gehören nicht zum Repository.
- Das früher auffällige AztecChallenge-Archiv liegt nicht als Testdatei vor; seine
  konkrete Hängerursache ist damit noch nicht abschließend reproduziert.
- Neue Funktionen werden separat mit Nutzen, Umfang und Abnahmekriterien festgelegt.
  Metadatenerhalt, weitere Imageformate und Startumgebungen sind keine impliziten
  Zusagen dieses Stabilisierungsschritts.
