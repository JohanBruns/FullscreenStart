# Fullscreen Start

Ein Vollbild-StartmenÃ¼ fÃ¼r Windows 11 im Stil des Windows-10-Startbildschirms
(â€žVollbild-StartmenÃ¼ verwendenâ€œ). C# / WPF auf .NET 8, lÃ¤uft dauerhaft im Infobereich
und blendet das MenÃ¼ nur ein und aus.

## Bedienung

| Aktion | Wie |
| --- | --- |
| MenÃ¼ Ã¶ffnen / schlieÃŸen | **Strg+Umschalt+Leertaste**, Klick auf das Tray-Icon, oder `FullscreenStart.exe` erneut starten |
| SchlieÃŸen | Esc, Klick ins Leere, Fokuswechsel, oder automatisch nach dem Starten einer App |
| Suchen | Einfach lostippen; â†‘/â†“ wÃ¤hlen, Enter startet den Treffer |
| App starten | Klick auf Kachel oder Listeneintrag |
| Ordner Ã¶ffnen | Klick auf eine Ordner-Kachel (klappt unter der Kachel auf) |
| Kachel verschieben | Ziehen und loslassen â€“ auf eine andere Kachel (Mitte) = Ordner bilden, an den Rand = davor/danach einfÃ¼gen |
| Neue Gruppe | Kachel in einen leeren Bereich unter einer Spalte oder rechts neben die letzte Spalte ziehen |
| Gruppe umbenennen | Klick auf die GruppenÃ¼berschrift |
| Gruppe verschieben | Ãœberschrift ziehen |
| Weitere Aktionen | Rechtsklick: anheften/lÃ¶sen, GrÃ¶ÃŸe (klein/mittel/breit/groÃŸ), Ordner auflÃ¶sen, Als Administrator ausfÃ¼hren, Dateispeicherort Ã¶ffnen |

## Daten

Alles liegt in `%AppData%\FullscreenStart`:

- `layout.json` â€“ Spalten, Gruppen, Kacheln und Ordner
- `settings.json` â€“ Hotkey, Hintergrund (`backdropBlurRadius`, `backdropTintOpacity`),
  Namen auf App-Kacheln (`showTileLabels`, Standard aus: das Icon fÃ¼llt die Kachel), Startansicht
- `log.txt` â€“ Start- und Fehlerprotokoll

Beim ersten Start wird das Layout nach dem alten Windows-10-Startbildschirm angelegt;
nicht installierte Apps werden dabei Ã¼bersprungen. Alles ist danach frei Ã¤nderbar.
LÃ¶scht man `layout.json`, wird es neu erzeugt.

## Technik

- **Apps:** `shell:AppsFolder` (Desktop- und Store-Apps, dieselbe Liste wie im Windows-StartmenÃ¼),
  abgeglichen mit den VerknÃ¼pfungen in `%ProgramData%` und `%AppData%\Microsoft\Windows\Start Menu\Programs`.
  Gestartet wird wie im Explorer per `ShellExecuteEx` auf die Item-ID-Liste.
- **Hintergrund:** Das Desktop-Hintergrundbild wird ohne die offenen Fenster darÃ¼ber nachgebildet.
  Live-Wallpaper (z. B. Lively) werden per `PrintWindow` aus ihrem Fenster hinter den Desktop-Symbolen
  gelesen, sonst wird das normale Hintergrundbild geladen; danach verkleinert, weichgezeichnet und getÃ¶nt.
  Klappt beides nicht, bleibt das Acrylic von Windows sichtbar.
- **Taskleiste:** Bei automatisch ausgeblendeter Taskleiste bleibt ihr Streifen frei, damit sie weiter ausfahren kann.

## Entwicklung

```bash
dotnet build
dotnet test
```

`tools/snapshot.ps1` baut die App und rendert die Ansichten als PNG (ohne das Fenster anzuzeigen).
`tools/make-icon.ps1` erzeugt `Assets/app.ico` neu.
