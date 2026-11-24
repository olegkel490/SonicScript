namespace SonicScript.Core.Modes;

public sealed record ModeTemplate(string Key, string NameDe, string NameEn, string Symbol, string InstructionDe, string InstructionEn, double Temperature = 0.3)
{
    public ModeDefinition Create(bool german) => new()
    {
        Kind = ModeKind.Custom,
        Name = german ? NameDe : NameEn,
        Symbol = Symbol,
        Instruction = german ? InstructionDe : InstructionEn,
        Temperature = Temperature,
    };
}

/// <summary>Fertige Vorlagen für eigene Modi (Einstellungen → Modi → "Aus Vorlage").</summary>
public static class ModeTemplates
{
    private const string LangDe = " Inhalt nicht erfinden; Sprache wie im Diktat.";
    private const string LangEn = " Do not invent content; same language as the dictation.";

    public static readonly IReadOnlyList<ModeTemplate> All =
    [
        new("linkedin", "LinkedIn-Post", "LinkedIn post", "in",
            "Mache aus dem Diktat einen LinkedIn-Post: ein starker erster Satz als Aufhänger, kurze Absätze (1–3 Sätze, durch Leerzeilen getrennt), persönlich und professionell, am Ende eine Frage oder Einladung zur Diskussion und in der letzten Zeile 3–5 passende Hashtags. Höchstens zwei Emojis." + LangDe,
            "Turn the dictation into a LinkedIn post: a strong first line as a hook, short paragraphs (1–3 sentences, separated by blank lines), personal and professional, a question or invitation to discuss at the end and 3–5 fitting hashtags on the last line. At most two emojis." + LangEn, 0.5),
        new("minutes", "Protokoll", "Meeting notes", "≡",
            "Mache aus dem Diktat ein knappes Besprechungsprotokoll mit den Abschnitten „Themen“, „Entscheidungen“ und „Aufgaben“, jeweils als Liste mit „- “. Aufgaben mit Verantwortlichem und Termin, falls genannt. Leere Abschnitte weglassen." + LangDe,
            "Turn the dictation into concise meeting notes with the sections \"Topics\", \"Decisions\" and \"Action items\", each as a list with \"- \". Action items with owner and due date if mentioned. Omit empty sections." + LangEn, 0.2),
        new("ticket", "Ticket", "Ticket", "#",
            "Formuliere aus dem Diktat ein Ticket (z. B. Jira oder GitHub): erste Zeile ein kurzer Titel, dann die Abschnitte „Beschreibung“, bei Fehlern „Schritte zum Nachstellen“, „Erwartet“ und „Tatsächlich“, zuletzt „Akzeptanzkriterien“ als Liste mit „- “. Leere Abschnitte weglassen." + LangDe,
            "Turn the dictation into a ticket (e.g. Jira or GitHub): first line a short title, then the sections \"Description\", for bugs \"Steps to reproduce\", \"Expected\" and \"Actual\", finally \"Acceptance criteria\" as a list with \"- \". Omit empty sections." + LangEn, 0.2),
        new("customer", "Kundenantwort", "Customer reply", "☎",
            "Formuliere aus dem Diktat eine freundliche, professionelle Antwort an einen Kunden: kurzer Dank für die Nachricht, klare Antwort bzw. nächster Schritt, positiver Abschluss. Höflich (Sie, außer im Diktat steht Du), lösungsorientiert, keine Zusagen, die nicht diktiert wurden. Kurze Absätze." + LangDe,
            "Turn the dictation into a friendly, professional reply to a customer: brief thanks for the message, a clear answer or next step, a positive close. Polite, solution-oriented, no commitments that were not dictated. Short paragraphs." + LangEn),
        new("summary", "Zusammenfassung", "Summary", "Σ",
            "Fasse das Diktat knapp zusammen: ein Satz mit der Kernaussage, dann die wichtigsten Punkte als Liste mit „- “ (höchstens fünf). Nichts hinzufügen." + LangDe,
            "Summarize the dictation briefly: one sentence with the key message, then the most important points as a list with \"- \" (at most five). Add nothing." + LangEn, 0.2),
        new("bullets", "Stichpunkte", "Bullet points", "•",
            "Wandle das Diktat in eine übersichtliche Liste mit „- “ um: ein Gedanke pro Punkt, kurz und prägnant, Reihenfolge beibehalten, Füllwörter weglassen." + LangDe,
            "Turn the dictation into a clear list with \"- \": one idea per point, short and crisp, keep the order, drop filler words." + LangEn, 0.2),
        new("chat", "Chat-Nachricht", "Chat message", "✉",
            "Formuliere aus dem Diktat eine kurze, klare Chat-Nachricht (Teams, Slack, WhatsApp): locker, aber korrekt, ohne Anrede-Floskeln und Grußformel, das Wichtigste zuerst, höchstens drei kurze Absätze." + LangDe,
            "Turn the dictation into a short, clear chat message (Teams, Slack, WhatsApp): casual but correct, no formal greeting or sign-off, most important point first, at most three short paragraphs." + LangEn, 0.4),
        new("formal", "Formell (Sie)", "Formal", "§",
            "Formuliere den Text höflich-formell um: Sie-Form, sachlich, vollständige Sätze, Inhalt unverändert." + LangDe,
            "Rewrite the text in a polite, formal style: factual, complete sentences, content unchanged." + LangEn, 0.3),
        new("casual", "Locker (Du)", "Casual", "~",
            "Formuliere den Text locker und freundlich um: Du-Form, natürlich, kurze Sätze, Inhalt unverändert." + LangDe,
            "Rewrite the text in a casual, friendly style: natural, short sentences, content unchanged." + LangEn, 0.5),
    ];
}
