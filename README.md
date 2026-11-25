<p align="center">
  <img src="src/SonicScript.App/Assets/icon.png" width="96" height="96" alt="SonicScript">
</p>

<h1 align="center">SonicScript</h1>

<p align="center"><b>Taste halten, sprechen, loslassen – der Text steht am Cursor.</b><br>
Diktieren für Windows. Spracherkennung lokal auf deinem PC.</p>

---

SonicScript ist eine Tray-App für Windows (.NET 10, Avalonia). Die Spracherkennung läuft lokal mit Whisper,
mit Grafikkarte über Vulkan, falls vorhanden. KI-Modi laufen standardmäßig über Ollama auf demselben PC.
OpenRouter ist optional.

Inspiriert von [Blitztext](https://github.com/cmagnussen/blitztext-app) (macOS) und
[Blitznotiz](https://github.com/danielstefanmueller/Blitznotiz) (Windows). Details in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Bedienung

| Taste (Standard) | Was passiert |
|---|---|
| `Strg+Alt+,` halten | Diktat im **ausgewählten Modus** |
| `Strg+Alt+.` | nächster Modus |
| `Strg+Alt+-` | vorheriger Modus |
| `Ctrl+Alt+L` | Notiz-Bibliothek ein/aus |
| `Strg+Alt+#` | Diktat fortsetzen (KI-Modi glätten alles zusammen neu) |
| `Strg+Alt+Z` | letztes Diktat rückgängig (1× wortgetreue Fassung, 2× entfernen) |
| `Esc` | Aufnahme abbrechen |

Modi im Wechsel: SonicScript (wortgetreu) → SonicScript+ (geglättet, mit Absätzen und Listen) → **Mail** (geglättet und als E-Mail formatiert: Anrede, Absätze, Aufzählungen, Gruß) → **Mail EN** (dasselbe, aber die E-Mail auf Englisch) → **Antwort** (Antwort auf eine E-Mail, siehe unten) → Ruhig → Emoji → EN → Prompt → **Bearbeiten**
(markierten Text per Sprache ändern) → **Notiz** (ablegen statt einfügen). Welche Modi dabei sind, legst du in den
Einstellungen fest; eigene Modi kommen automatisch dazu.

**Immer sichtbar:** Der aktuelle Modus steht als kleines Feld **in der Taskleiste**, links neben dem Infobereich.
Ein Klick schaltet weiter, ein Rechtsklick zurück, das Mausrad blättert. Bei Aufnahme leuchtet ein roter Punkt. Beim
Wechseln per Taste erscheint unten mittig kurz eine **Einblendung** mit Modus, Tasten und Position in der Liste.
Bei Vollbild-Apps (Video, Präsentation) blendet sich das Feld aus. Auch das Tray-Symbol zeigt das Modus-Kürzel.

Optional: Sprachbefehle am Anfang („Prompt: …“, „Notiz: …“) und App-Regeln, die den Modus wählen (z. B. Prompt in
ChatGPT) – beides in den Einstellungen einschaltbar.

**Kurz tippen statt halten** (unter 0,35 s) löst bei der Diktat-Taste die normale Funktion der Taste aus.
Die Tasten liegen auf deutschen Laptop- und externen Tastaturen an derselben Stelle neben der rechten Shift-Taste.

Jede Taste ist frei belegbar: in den Einstellungen ins Tastenfeld klicken und die Kombination drücken. Pro Taste lässt sich wählen, ob man sie hält oder einmal zum Starten und einmal
zum Stoppen drückt. Bei Kombinationen nur aus Modifikatoren (z. B. `Ctrl+Win`) wechselt eine zusätzlich gedrückte Modifikatortaste
den Modus, ohne neu anzufangen. Eigene Modi bekommen eine eigene Anweisung und eine eigene Taste, etwa für Übersetzungen oder
Antwortentwürfe.

## Funktionen

- **Lokale Spracherkennung:** whisper.cpp über Whisper.net. Die Einrichtung schlägt ein Modell passend zur
  Hardware vor und misst danach die Geschwindigkeit. Cloud-Erkennung ist optional, über OpenRouter oder eine
  OpenAI-kompatible Adresse.
- **Live-Vorschau und lange Diktate:** Bei lokaler Erkennung zeigt die Aufnahme-Anzeige schon beim Sprechen den
  erkannten Text. Lange Diktate werden an Sprechpausen in Stücke geteilt und schon während der Aufnahme erkannt –
  am Ende fehlt nur noch das letzte Stück.
- **Antwort auf E-Mails:** Im Modus *Antwort* sagst du nur, was du antworten willst („sag zu, Donnerstag passt“).
  Diktierst du im Antwortfenster, liest SonicScript die zitierte Mail unter dem Cursor und fügt die fertige Antwort
  ein – in der Sprache der Mail. Ist die Mail markiert (z. B. im Lesebereich), landet die Antwort in der Zwischenablage.
- **Rückgängig:** `Strg+Alt+Z` ersetzt den KI-Text durch die wortgetreue Fassung, ein zweiter Druck entfernt das Diktat.
- **Wörterbuch lernt mit:** Korrigierst du nach dem Einfügen ein falsch erkanntes Wort, schlägt SonicScript die
  Schreibweise fürs Wörterbuch vor.
- **Ausfallsicher:**
  - Antwortet das Sprachmodell nicht, wird einmal wiederholt und dann ein **Ersatzmodell** gefragt (einstellbar).
  - Fällt die Cloud-Erkennung aus, erkennt das **lokale Whisper-Modell** (falls installiert).
  - Wirkt eine KI-Antwort verdächtig, wird wortgetreu eingefügt – ein Klick setzt den KI-Text trotzdem ein.
  - Jeder Text im Verlauf lässt sich **neu verarbeiten** (anderer Modus, Ergebnis in der Zwischenablage).
  - Der Tastatur-Hook wird nach dem Ruhezustand automatisch neu angemeldet; Mikrofone werden über den Namen
    gefunden, beim Abziehen wird das bisher Aufgenommene verarbeitet.
  - Startet ein Update nicht sauber, kehrt SonicScript automatisch zur vorherigen Version zurück.
  - Abstürze werden erkannt, ins Log geschrieben, und Windows startet SonicScript neu.
- **Diagnose:** Einstellungen → Diagnose prüft Mikrofon, Tasten, Erkennung, Sprachmodell, Guthaben und Updates und
  speichert auf Wunsch einen Fehlerbericht (Log + Einstellungen ohne API-Schlüssel). Liegt die .exe in OneDrive,
  zieht ein Klick sie nach `%LOCALAPPDATA%\Programs\SonicScript` um (mit Verknüpfungen).
- **Statistik und Kosten:** Diktate, Wörter, Sprechzeit und gesparte Tippzeit pro Tag/Woche/Monat; bei OpenRouter
  der Verbrauch (Tag/Woche/Monat) mit optionaler Warnung ab einem Monatslimit.
- **KI-Modi:**
  - Sprachmodell: Ollama oder LM Studio lokal, OpenRouter, OpenAI oder eigene Adresse.
  - Das Transkript ist als Daten markiert. Eine Prüfung verwirft Antworten, die keine Umformulierung sind.
  - Fällt der KI-Schritt aus, wird der wortgetreue Text eingefügt.
- **Modus pro App oder Webseite:** Regeln nach Prozessname und Fenstertitel, z. B. Outlook → SonicScript+,
  Gmail → SonicScript+, Slack → Emoji. Die Einfügeart lässt sich pro App einstellen, z. B. für Terminals.
- **Einfügen:**
  - Standard ist Zwischenablage + Strg+V. Die vorherige Zwischenablage wird mit allen Formaten wiederhergestellt.
  - Ist die Zwischenablage gesperrt, wird getippt.
  - Hat das Fenster gewechselt oder ist ein Passwortfeld aktiv, landet der Text nur in der Zwischenablage.
- **Mikrofon schnell wechseln:** Tray-Menü → „Mikrofon“ oder eine frei belegbare Taste, die reihum wechselt.
- **Erinnerungen aus Notizen:** „Notiz: morgen 9 Uhr Angebot an Müller“ oder „in 2 Stunden …“ – SonicScript meldet
  sich zur Zeit (Erledigt / In 10 Min); verpasste kommen nach dem Start.
- **Vorlagen für eigene Modi:** LinkedIn-Post, Protokoll, Ticket, Kundenantwort, Zusammenfassung, Stichpunkte,
  Chat-Nachricht, Formell, Locker – unter Einstellungen → Modi → „Aus Vorlage …“.
- **Blitznotizen:**
  - Ablage als `Ordner/2026/2026-11/2026-11-16.md`, eine Zeile pro Notiz: `- [ ] 09:14 Text`.
  - Bibliothek am Bildschirmrand angedockt. Klick kopiert die Notiz und markiert sie grün.
- **Nachbearbeitung:**
  - Filter für Whisper-Halluzinationen („Untertitel im Auftrag des ZDF“).
  - Gesprochene Uhrzeiten („sechzehn Uhr fünfundvierzig bis siebzehn Uhr fünf“ → „16:45 bis 17:05 Uhr“).
  - Wörterbuch mit Begriffen und festen Ersetzungen („n acht n“ → „n8n“).
  - Stille abschneiden: Stille am Anfang/Ende fällt weg, lange Pausen werden gekürzt. Schneller und weniger
    erfundene Sätze; reine Stille wird gar nicht erst erkannt (abschaltbar).
- **Einstellungen sichern/übertragen:** Export und Import unter *Allgemein*. Modi, Tasten, App-Regeln, Wörterbuch
  und Anbieter wandern mit; API-Schlüssel, Mikrofon und lokales Modell bleiben am jeweiligen PC.
- **Nichts geht verloren:** Schlägt die Transkription fehl, bleibt die Aufnahme 24 Stunden erhalten. Sie lässt
  sich über das Tray-Menü neu transkribieren. Im Tray-Menü gibt es außerdem „Letzten Text kopieren“ und
  „Wortgetreu statt KI-Text“.
- **Datenschutz:**
  - API-Schlüssel und Verlauf werden mit DPAPI verschlüsselt.
  - Ein Schlüssel wird nur über https oder an lokale/private Adressen geschickt.
  - Keine Telemetrie.
- **Oberfläche:** Deutsch und Englisch.

- **Rückmeldung:** leiser Ton bei Start und Ende der Aufnahme. Musik und Videos werden während der Aufnahme
  leiser gestellt (einstellbar).
- **Passt sich an:** Leerzeichen und Groß-/Kleinschreibung richten sich nach dem Text vor dem Cursor
  (UI Automation, auch in Browsern und Office). Passwortfelder werden auch dort erkannt.
- **Verlauf mit Suche:** Ergebnisse oder den wortgetreuen Text erneut kopieren (Tray → Verlauf).
- **Updates in der App:** Tray → „Update installieren“ lädt die neue Version, prüft die SHA-256-Prüfsumme,
  tauscht die .exe aus und startet neu.

## Installation

1. `SonicScript.exe` von der [Releases-Seite](../../releases) laden und starten. Es ist eine einzelne Datei,
   ohne Installer und ohne separate .NET-Runtime.
2. Die Einrichtung lädt das empfohlene Whisper-Modell (190 MB bis 1,6 GB) und misst die Geschwindigkeit.
3. Optional für die KI-Modi: [Ollama](https://ollama.com) installieren, dann z. B. `ollama pull qwen3:4b`.

Ohne Code-Signatur zeigt Windows SmartScreen beim ersten Start eine Warnung („Weitere Informationen“ →
„Trotzdem ausführen“).

## Datenablage

| Was | Wo |
|---|---|
| Einstellungen | `%APPDATA%\SonicScript\settings.json` |
| API-Schlüssel, Verlauf | `%APPDATA%\SonicScript\` (DPAPI-verschlüsselt) |
| Modelle, Whisper-Runtime, Log | `%LOCALAPPDATA%\SonicScript\` |
| Fehlgeschlagene Aufnahmen (24 h) | `%LOCALAPPDATA%\SonicScript\pending\` |
| Notizen | frei wählbarer Ordner, Klartext-Markdown |

## Selbst bauen

Voraussetzung ist das .NET 10 SDK.

```powershell
dotnet test tests/SonicScript.Core.Tests
dotnet run --project src/SonicScript.App
```

So entsteht die einzelne .exe:

```powershell
dotnet publish src/SonicScript.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

Release erstellen: **Actions → CI → Run workflow** (Branch `main`), Version eingeben (z. B. `0.1.1`). Der Workflow baut, testet,
legt den Tag `v0.1.1` an und veröffentlicht das Release mit `SonicScript.exe` und SHA-256-Prüfsumme.
Ein von Hand gepushter Tag `v*` funktioniert genauso.

## Aufbau

```text
src/SonicScript.Core              plattformneutral: Hotkey-Logik, Pipeline, Textfilter, Prompts, Notizen,
                                  Verlauf, Einstellungen, OpenAI-kompatible Clients, Lokalisierung
src/SonicScript.LocalWhisper      lokale Transkription (Whisper.net, CPU/Vulkan), Benchmark
src/SonicScript.Platform.Windows  Tastatur-Hook, Aufnahme (NAudio), Zwischenablage, Einfügen, DPAPI, Autostart
src/SonicScript.App               Avalonia-Oberfläche: Tray, Aufnahme-Anzeige, Einstellungen, Bibliothek, Einrichtung
tests/SonicScript.Core.Tests      xUnit-Tests für den Kern
```

## Bekannte Grenzen

- Passwortfelder und Text vor dem Cursor kommen per UI Automation. Apps ohne UI-Automation-Unterstützung
  bekommen den Text unverändert.
- Läuft die Ziel-App mit Adminrechten, blockiert Windows das Einfügen. Der Text liegt dann in der
  Zwischenablage.
- Die Einstellung „Grafikkarte nutzen“ wirkt erst nach einem Neustart.
- Meetings mit Sprechertrennung sind für Version 2 geplant.

---

## English

SonicScript is a push-to-talk dictation tray app for Windows. Hold a key, speak, release, and the text appears
at your cursor. Speech recognition runs locally with Whisper (CPU or Vulkan GPU). The AI rewrite modes use
Ollama on the same PC by default, with OpenRouter as an option. A built-in mode translates dictation into English. There are per-app modes, quick voice notes
filed as daily Markdown, a dictionary and clipboard restore. The UI is available in German and English.
MIT licensed.
