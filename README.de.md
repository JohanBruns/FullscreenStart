# Fullscreen Start

[English](README.md) · **Deutsch**

Der Vollbild-Startbildschirm von Windows 10, nachgebaut für Windows 11. C# / WPF auf .NET 8;
läuft im Infobereich und öffnet sofort.

## Funktionen

- Kachelgruppen in Spalten, frei platzierbar, mit Ordnern und vier Kachelgrößen
- „Alle Apps“-Liste und Suche durch Lostippen (Enter startet den ersten Treffer)
- Weichgezeichnetes Hintergrundbild (auch mit Lively Wallpaper), folgt Hell/Dunkel-Modus und Akzentfarbe
- Öffnet mit der **Windows-Taste**, dem **Start-Knopf der Taskleiste** oder **Strg+Umschalt+Leertaste**; Win+…-Kürzel funktionieren weiter

## Installation

```bash
powershell -ExecutionPolicy Bypass -File tools/install.ps1
```

Installiert nach `%LocalAppData%\Programs\FullscreenStart` und startet mit Windows (`-NoAutostart` lässt das weg).
`tools/uninstall.ps1` entfernt alles wieder (`-RemoveData` löscht auch Layout und Einstellungen).

## Bedienung

| Aktion | Wie |
| --- | --- |
| Schließen | Esc, Klick ins Leere oder eine App starten |
| Kachel verschieben | Ziehen: auf die Mitte einer Kachel = Ordner, an ihren Rand = davor/danach einfügen |
| Neue Gruppe | Kachel in einen leeren Bereich ziehen; die Gruppe entsteht genau dort |
| Gruppe verschieben | Überschrift ziehen: auf eine Gruppe = davor/danach, in einen leeren Bereich = frei platzieren (am unteren Rand halten scrollt weiter) |
| Gruppe umbenennen | Klick auf die Überschrift |
| Mehr | Rechtsklick: anheften/lösen, Größe, Ordner auflösen, als Administrator ausführen, Dateispeicherort öffnen, Lücke über einer Gruppe schließen |

Im Tray-Menü lassen sich Windows-Taste, Start-Knopf und Autostart umschalten.

## Einstellungen und Daten

Alles liegt in `%AppData%\FullscreenStart`:

- `layout.json`: Spalten, Gruppen, Kacheln und Ordner
- `settings.json`: Hotkey, `theme` (`system`/`light`/`dark`), Weichzeichnung und Tönung des Hintergrunds, Kachelnamen, Startansicht
- `log.txt`: Start- und Fehlerprotokoll

Wer `layout.json` löscht, bekommt das Standardlayout zurück.

## Einschränkungen

- Ist ein Fenster mit Administratorrechten im Vordergrund (z. B. Task-Manager), gibt Windows die Windows-Taste
  nicht an die App weiter, und das eigene Startmenü öffnet sich.
- Der Autostart läuft bewusst ohne Administratorrechte, sonst liefe jede aus dem Menü gestartete App ebenfalls als Administrator.

## Entwicklung

```bash
dotnet build
dotnet test
```

`tools/snapshot.ps1` rendert die Ansichten als PNG, ohne das Fenster anzuzeigen.
`FullscreenStart.exe --selftest <Datei>` spielt Klicks und Drag & Drop am echten Layout durch.
