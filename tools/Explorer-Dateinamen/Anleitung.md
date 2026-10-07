# Dateinamen im Explorer bereinigen

1. Diesen gesamten Ordner an einem dauerhaften Ort speichern.
2. `Installieren.cmd` doppelt anklicken. Keine Administratorrechte erforderlich.
3. Im Explorer einen Ordner oder den freien Hintergrund eines offenen Ordners mit der rechten Maustaste anklicken. Unter Windows 11 gegebenenfalls **Weitere Optionen anzeigen** aufrufen.
4. **Dateinamen bereinigen (Disk behalten)** anklicken.
5. Vorschau lesen und mit **J**, dann Enter, umbenennen. Jede andere Eingabe bricht ab.

Beispiel:

`Monkey Island 2 - LeChuck's Revenge v1.0 (1992-04-27)(Softgold)(DE)(Disk 01 of 11)[cp code wheel].adf`

wird zu:

`Monkey Island 2 - LeChuck's Revenge (Disk 01 of 11).adf`

Das Script bearbeitet alle Dateien direkt in diesem Ordner, auch versteckte Dateien, ohne Unterordner zu durchsuchen. Dateiendungen bleiben erhalten. Runde Klammergruppen werden entfernt, ausser Angaben im Format `(Disk 01 of 11)`. Eckige Klammergruppen werden entfernt. Versionsangaben am Ende des Titels wie `v1.0`, `v2`, `v1.2.3` oder `v1.0a` werden entfernt; sie duerfen auch fehlen. Mehrfache Leerzeichen werden zusammengefasst.

Auch Klammergruppen, die zum eigentlichen Titel gehoeren, werden entfernt. Deshalb die Vorschau pruefen. Wenn zwei Dateien denselben Zielnamen bekommen oder ein Ziel schon existiert, bricht das Script vor dem Umbenennen ab. Dateien werden nicht ueberschrieben.

Mit `Deinstallieren.cmd` laesst sich der Kontextmenue-Eintrag wieder entfernen. Der Installer registriert nur fuer das aktuelle Windows-Benutzerkonto. Die Scripts muessen danach an ihrem Installationsort bleiben.
