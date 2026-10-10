# Fullscreen Start

Ein Vollbild-Startmenü für Windows 11 im Stil des Windows-10-Startbildschirms
(„Vollbild-Startmenü verwenden“). C# / WPF auf .NET 8, läuft dauerhaft im Infobereich
und blendet das Menü nur ein und aus.

## Bedienung

| Aktion | Wie |
| --- | --- |
| Menü öffnen / schließen | **Windows-Taste**, **Start-Knopf der Taskleiste**, **Strg+Umschalt+Leertaste**, Klick auf das Tray-Icon, oder `FullscreenStart.exe` erneut starten |
| Schließen | Esc, Klick ins Leere, Fokuswechsel, oder automatisch nach dem Starten einer App |
| Suchen | Einfach lostippen; ↑/↓ wählen, Enter startet den Treffer |
| App starten | Klick auf Kachel oder Listeneintrag |
| Ordner öffnen | Klick auf eine Ordner-Kachel (klappt unter der Kachel auf) |
| Kachel verschieben | Ziehen und loslassen – auf eine andere Kachel (Mitte) = Ordner bilden, an den Rand = davor/danach einfügen |
| Neue Gruppe | Kachel in einen leeren Bereich unter einer Spalte oder rechts neben die letzte Spalte ziehen |
| Gruppe umbenennen | Klick auf die Gruppenüberschrift |
| Gruppe verschieben | Überschrift ziehen |
| Weitere Aktionen | Rechtsklick: anheften/lösen, Größe (klein/mittel/breit/groß), Ordner auflösen, Als Administrator ausführen, Dateispeicherort öffnen |

## Installation

```bash
powershell -ExecutionPolicy Bypass -File tools/install.ps1
```

Installiert nach `%LocalAppData%\Programs\FullscreenStart` (ersetzt eine laufende Version), legt den
Startmenü-Eintrag „Fullscreen Start“ an und schaltet „Mit Windows starten“ ein (`-NoAutostart` lässt das weg).
Autostart und Windows-Taste lassen sich jederzeit im Tray-Menü umschalten.
`tools/uninstall.ps1` entfernt alles wieder; Layout und Einstellungen bleiben, außer mit `-RemoveData`.

Der Autostart läuft bewusst mit normalen Rechten: Mit Administratorrechten würde jede aus dem Menü
gestartete App ebenfalls als Administrator laufen.

## Daten

Alles liegt in `%AppData%\FullscreenStart`:

- `layout.json` – Spalten, Gruppen, Kacheln und Ordner
- `settings.json` – Hotkey, Design (`theme`: `system` folgt dem Windows-Modus, sonst `light`/`dark`),
  Hintergrund (`backdropBlurRadius`, `backdropTintOpacity`),
  Namen auf App-Kacheln (`showTileLabels`, aus = das Icon füllt die ganze Kachel), Startansicht
- `log.txt` – Start- und Fehlerprotokoll

Beim ersten Start wird das Layout nach dem alten Windows-10-Startbildschirm angelegt;
nicht installierte Apps werden dabei übersprungen. Alles ist danach frei änderbar.
Löscht man `layout.json`, wird es neu erzeugt.

## Technik

- **Apps:** `shell:AppsFolder` (Desktop- und Store-Apps, dieselbe Liste wie im Windows-Startmenü),
  abgeglichen mit den Verknüpfungen in `%ProgramData%` und `%AppData%\Microsoft\Windows\Start Menu\Programs`.
  Gestartet wird wie im Explorer per `ShellExecuteEx` auf die Item-ID-Liste.
- **Hintergrund:** Das Desktop-Hintergrundbild wird ohne die offenen Fenster darüber nachgebildet.
  Live-Wallpaper (z. B. Lively) werden per `PrintWindow` aus ihrem Fenster hinter den Desktop-Symbolen
  gelesen, sonst wird das normale Hintergrundbild geladen; danach verkleinert, weichgezeichnet und getönt.
  Klappt beides nicht, bleibt das Acrylic von Windows sichtbar.
- **Windows-Taste:** Ein Low-Level-Tastatur-Hook (eigener Thread) erkennt einen einzelnen Druck auf Win.
  Vor dem Loslassen wird eine unbelegte Taste (VK 0xE8) eingeschoben, damit Windows sein Startmenü nicht öffnet;
  Win+E, Win+R, Win+Umschalt+S usw. laufen unverändert durch. Abschaltbar im Tray-Menü
  („Windows-Taste öffnet dieses Menü“) bzw. mit `takeOverWinKey` in `settings.json`.
  Einschränkung: Ist ein Fenster mit Administratorrechten im Vordergrund (z. B. Task-Manager), sieht der Hook
  die Taste nicht (UIPI) und Windows öffnet sein eigenes Startmenü.
- **Start-Knopf:** Ein Low-Level-Maus-Hook fängt Linksklicks auf den Start-Knopf ab. Windows 11 hält das
  klassische (unsichtbare) Fenster der Klasse `Start` deckungsgleich über seinem Start-Knopf, daher genügt
  ein schneller Fenster-Lookup statt UI Automation. Rechtsklick (Win+X-Menü) bleibt unverändert.
  Abschaltbar im Tray-Menü bzw. mit `takeOverStartButton`. Ändert ein künftiges Windows-Update das, findet
  der Hook den Knopf nicht mehr und Windows öffnet wieder sein eigenes Startmenü – es geht nichts kaputt.
- **Design:** Hell/Dunkel und Akzentfarbe kommen von Windows und wechseln live mit
  (`WM_SETTINGCHANGE` „ImmersiveColorSet“, `WM_DWMCOLORIZATIONCOLORCHANGED`). Die Farben liegen in
  `Themes/Colors.Dark.xaml` und `Themes/Colors.Light.xaml`.
- **Taskleiste:** Bei automatisch ausgeblendeter Taskleiste bleibt nur ihr 2-px-Rand frei; fährt man mit der Maus
  dorthin, kommt die Taskleiste wie gewohnt heraus.

## Entwicklung

```bash
dotnet build
dotnet test
```

`tools/snapshot.ps1` baut die App und rendert die Ansichten als PNG (ohne das Fenster anzuzeigen).
`tools/make-icon.ps1` erzeugt `Assets/app.ico` neu.
