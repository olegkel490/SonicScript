# Third-Party Notices

SonicScript steht unter der MIT-Lizenz (siehe `LICENSE`). Es enthält oder nutzt Teile folgender Projekte.

## Inspiration und adaptierter Code

### Blitznotiz
- Quelle: https://github.com/danielstefanmueller/Blitznotiz
- Lizenz: MIT, Copyright (c) 2026 Daniel Müller
- Verwendet: Muster für Win32-Zwischenablage, `SendInput` (Strg+V) und den Low-Level-Tastatur-Hook
  (`src/SonicScript.Platform.Windows/NativeMethods.cs`, `ClipboardService.cs`, `KeyboardHook.cs`, `TextInserter.cs`),
  die Idee der Blitznotiz-Ablage (Markdown pro Tag mit `- [ ]`/`- [x]`) und der Bibliothek,
  die Beschränkung von Schlüsseln auf https/private Adressen sowie das Vorwärmen von Verbindungen.

### Blitztext
- Quelle: https://github.com/cmagnussen/blitztext-app
- Lizenz: MIT, Copyright (c) 2026 Blitztext contributors
- Verwendet: Konzepte (vier Modi, Modus pro App/Webseite, Wörterbuch, gesprochene Uhrzeiten,
  Halluzinationsfilter, Prüfung der KI-Antwort, Wiederherstellen der Zwischenablage, Aufbewahren
  fehlgeschlagener Aufnahmen). Die Umsetzung und die Prompts sind neu geschrieben.
- Name, Logo und Icon von Blitztext werden nicht verwendet (siehe dessen `TRADEMARKS.md`).

MIT-Lizenztext (gilt für beide oben genannten Projekte mit dem jeweiligen Copyright-Vermerk):

```

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Bibliotheken

| Paket | Lizenz | Projekt |
|---|---|---|
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Whisper.net | MIT | https://github.com/sandrohanea/whisper.net |
| whisper.cpp / ggml (in Whisper.net.Runtime) | MIT | https://github.com/ggml-org/whisper.cpp |
| NAudio | MIT | https://github.com/naudio/NAudio |
| System.Security.Cryptography.ProtectedData | MIT | https://github.com/dotnet/runtime |
| Inter (Schrift, über Avalonia.Fonts.Inter) | SIL OFL 1.1 | https://github.com/rsms/inter |

Die Whisper-Modelle (GGML-Konvertierungen der OpenAI-Whisper-Gewichte) werden nicht mitgeliefert, sondern bei Bedarf
von Hugging Face (`ggerganov/whisper.cpp`) geladen. OpenAI Whisper steht unter MIT-Lizenz.
