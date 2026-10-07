# Validierung der Stabilisierung – 20.09.2026

## Ergänzung: A500-Bootexport – 21.09.2026

Der neue Export nutzt den Standard-Bootblock aus Hst.Amiga und OFS (`DOS\0`).
Tests prüfen die Bootblock-Prüfsumme mit Carry, Programm und Startskript nach
erneutem Mounten sowie den kompletten Weg über ein eigens erstelltes LHA.
Ein synthetisches WHDLoad-Paket wird für Kickstart 1.3 abgelehnt;
eine vorhandene Zielausgabe bleibt dabei erhalten. Eine tatsächliche Ausführung
auf einem A500 oder unter A500-Emulation steht noch aus.
Der Release-Testlauf am 21.09.2026 bestand mit 60 Tests, ohne Fehlschlag oder
Überspringen. Die unten genannten 54 Tests dokumentieren den vorherigen
Stabilisierungsstand vom 20.09.2026.

## Ausgangsstand

Lokaler Git-Baseline-Commit: `7b01f3b`.
Nach Entschärfung der festen Desktop-Testpfade bestanden alle 7 ursprünglichen Tests.
Die Änderungen nach diesem Commit bleiben als Arbeitskopie zur Durchsicht erhalten.

## Ergebnis

- Windows, .NET SDK 10.0.401 / Runtime 10.0.12.
- Release-Build: 0 Warnungen, 0 Fehler.
- Release-Tests: 54 bestanden, 0 fehlgeschlagen, 0 übersprungen.
- `git diff --check`: keine Whitespace-Fehler.
- Coverlet: Infrastructure 94,0 % Zeilen / 83,8 % Zweige;
  Core 68,4 % / 16,7 %; App 19,8 % / 15,5 %.
  Die App-Zahl enthält generierten Code. Die Tests decken ViewModel-Zustände ab,
  aber keine grafischen Dialoginteraktionen oder nativen Dateiauswahldialoge.

Verwendeter abschließender Testaufruf:

```powershell
dotnet test AmiDiskLab.slnx --no-restore --configuration Release -p:UsedAvaloniaProducts= --collect:'XPlat Code Coverage' --blame-hang-timeout 30s --verbosity minimal
```

Die MSBuild-Eigenschaft unterdrückte ausschließlich den Avalonia-Buildstatistik-Task,
der in der eingeschränkten Ausführungsumgebung auf ein gesperrtes Benutzerverzeichnis
zugreifen wollte. Der Anwendungscode wurde regulär gebaut. Der NuGet-Restore
verwendete die lokal vorhandenen Pakete; ein Vulnerability-Audit war nicht Teil der Prüfung.

## Inhaltlich geprüft

- Leeres Image: Größe, DOS-Typ, Root-/Bitmap-Checksummen und erneutes Mounten.
- Dateigrößen 0, 1, 512, 513, 1024, 1025, 36864, 36865 und 400000 Bytes.
- Verschachtelte Geschwisterverzeichnisse und leere Verzeichnisse.
- Ein zur Laufzeit erzeugtes synthetisches Archiv nach Wiederöffnen bytegenau erhalten.
- Produktiver Konvertierungsservice einschließlich Extraktion und temporärer Ordner.
- Zu große Eingaben sowie zusätzliche Bibliotheksgrenze bei exakt voller Diskette.
- Bestehende Ziele bei Validierungsfehler, Abbruch nach Dateistream-Schließen,
  Überfüllung und fehlgeschlagenem abschließendem Verschieben erhalten.
- Archivpfad-Traversierung, absolute Pfade und leere oder unsichere Pfade nach dem ersten Nullzeichen,
  Plattform-Pfadprobleme, vorhandene Ziele und kollidierende Verzeichnisschreibweisen.
- Dublettenerkennung anhand des Inhalts und untergeordnete Suchverzeichnisse.
- Scanfehler, Konvertierungsfehler, Parallelbedienung, Abbruch und verspäteter Fortschritt.

## Weiterhin offen

- Der konkrete frühere AztecChallenge-Hänger: Archiv nicht im Testbestand.
- Prüfung der erzeugten Images mit unabhängigem Reader, Emulator oder echter Hardware.
- Grafischer Bedienungstest, Linux-Lauf und Ausführung der vorbereiteten GitHub-CI.
- Metadatenerhalt sowie harte Ressourcenlimits des LHA-Decoders.
- Neue Features: noch gemeinsam festzulegen.
