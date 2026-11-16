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
| `Ctrl+Win` halten | Diktat. Der Modus kommt aus der App-Regel, sonst wortgetreu |
| `+Shift` dazu (`Ctrl+Shift+Win`) | **SonicScript+**: sauberer Schrifttext |
| `+Alt` dazu (`Ctrl+Alt+Win`) | **$%&!**: Frust wird zu einer ruhigen, wirksamen Nachricht |
| `Alt+Win` halten | **:)**: Text mit passenden Emojis |
| `Alt+Shift+Win` halten | **EN**: Diktat wird ins Englische übersetzt |
| `Ctrl+Alt+Space` halten | **Blitznotiz**: wird nicht eingefügt, sondern als Markdown abgelegt |
| `Ctrl+Alt+L` | Notiz-Bibliothek ein/aus |
| `Esc` | Aufnahme abbrechen |

Jede Taste ist frei belegbar. Pro Taste lässt sich wählen, ob man sie hält oder einmal zum Starten und einmal
zum Stoppen drückt. Wer während eines Diktats eine Modifikatortaste ergänzt, wechselt den Modus, ohne neu
anzufangen. Eigene Modi bekommen eine eigene Anweisung und eine eigene Taste, etwa für Übersetzungen oder
Antwortentwürfe.

## Funktionen

- **Lokale Spracherkennung:** whisper.cpp über Whisper.net. Die Einrichtung schlägt ein Modell passend zur
  Hardware vor und misst danach die Geschwindigkeit. Cloud-Erkennung ist optional, über OpenRouter oder eine
  OpenAI-kompatible Adresse.
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
- **Blitznotizen:**
  - Ablage als `Ordner/2026/2026-11/2026-11-16.md`, eine Zeile pro Notiz: `- [ ] 09:14 Text`.
  - Bibliothek am Bildschirmrand angedockt. Klick kopiert die Notiz und markiert sie grün.
- **Nachbearbeitung:**
  - Filter für Whisper-Halluzinationen („Untertitel im Auftrag des ZDF“).
  - Gesprochene Uhrzeiten („sechzehn Uhr fünfundvierzig bis siebzehn Uhr fünf“ → „16:45 bis 17:05 Uhr“).
  - Wörterbuch mit Begriffen und festen Ersetzungen („n acht n“ → „n8n“).
- **Nichts geht verloren:** Schlägt die Transkription fehl, bleibt die Aufnahme 24 Stunden erhalten. Sie lässt
  sich über das Tray-Menü neu transkribieren. Im Tray-Menü gibt es außerdem „Letzten Text kopieren“ und
  „Wortgetreu statt KI-Text“.
- **Datenschutz:**
  - API-Schlüssel und Verlauf werden mit DPAPI verschlüsselt.
  - Ein Schlüssel wird nur über https oder an lokale/private Adressen geschickt.
  - Keine Telemetrie.
- **Oberfläche:** Deutsch und Englisch.

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

- Passwortfelder werden nur bei klassischen Win32-Eingabefeldern erkannt, nicht in Browsern oder
  XAML-Apps. Dafür ist UI Automation geplant.
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
