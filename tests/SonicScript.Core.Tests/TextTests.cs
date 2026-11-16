using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Settings;
using SonicScript.Core.Text;

namespace SonicScript.Core.Tests;

public class HallucinationFilterTests
{
    [Theory]
    [InlineData("Untertitel im Auftrag des ZDF, 2018")]
    [InlineData("Vielen Dank fürs Zuschauen!")]
    [InlineData("  Thanks for watching!  ")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("(Musik)")]
    [InlineData("♪ ♪")]
    [InlineData("Copyright WDR 2021")]
    public void RemovesPureHallucinations(string input) => Assert.Equal("", HallucinationFilter.Clean(input));

    [Fact]
    public void StripsTrailingCredit()
    {
        Assert.Equal("Wir treffen uns morgen.",
            HallucinationFilter.Clean("Wir treffen uns morgen. Untertitel im Auftrag des ZDF, 2020"));
    }

    [Fact]
    public void KeepsRealSpeech()
    {
        const string text = "Danke für die schnelle Antwort, das hilft mir sehr.";
        Assert.Equal(text, HallucinationFilter.Clean(text));
    }

    [Fact]
    public void RemovesInlineMarkers()
    {
        Assert.Equal("Hallo zusammen", HallucinationFilter.Clean("Hallo [Musik] zusammen"));
    }
}

public class SpokenTimeTests
{
    [Theory]
    [InlineData("sechzehn Uhr fünfundvierzig bis siebzehn Uhr fünf", "16:45 bis 17:05 Uhr")]
    [InlineData("Treffen um acht Uhr.", "Treffen um 8 Uhr.")]
    [InlineData("um 10 Uhr 30 im Büro", "um 10:30 Uhr im Büro")]
    [InlineData("Von 9 Uhr bis 17 Uhr 30 geöffnet", "Von 9 bis 17:30 Uhr geöffnet")]
    [InlineData("zwölf Uhr dreißig", "12:30 Uhr")]
    [InlineData("um zehn Uhr eine Besprechung", "um 10 Uhr eine Besprechung")]
    [InlineData("zehn Uhr dreißig Minuten", "10 Uhr dreißig Minuten")]
    public void Normalizes(string input, string expected) => Assert.Equal(expected, SpokenTimeNormalizer.Normalize(input));

    [Theory]
    [InlineData("Die Uhr ist kaputt.")]
    [InlineData("Ich habe 30 Uhren.")]
    [InlineData("fünfundzwanzig Uhr")]   // keine gültige Stunde
    [InlineData("Achtung Uhr")]
    public void LeavesOtherTextAlone(string input) => Assert.Equal(input, SpokenTimeNormalizer.Normalize(input));

    [Theory]
    [InlineData("einundzwanzig", 21)]
    [InlineData("neunundfünfzig", 59)]
    [InlineData("dreißig", 30)]
    [InlineData("zwölf", 12)]
    [InlineData("07", 7)]
    public void ParsesGermanNumbers(string word, int value) => Assert.Equal(value, GermanNumbers.Parse(word));
}

public class DictionaryApplierTests
{
    [Fact]
    public void AppliesReplacementsAndTermCasing()
    {
        var d = new DictionaryApplier(new DictionarySettings
        {
            Terms = ["OpenRouter", "SonicScript"],
            Replacements = [new Replacement { From = "n acht n", To = "n8n" }, new Replacement { From = "Preis", To = "$5" }],
        });
        Assert.Equal("Baue das in n8n mit OpenRouter für $5 in SonicScript",
            d.Apply("Baue das in N  acht n mit openrouter für Preis in sonicscript"));
    }

    [Fact]
    public void RespectsWordBoundaries()
    {
        var d = new DictionaryApplier(new DictionarySettings { Replacements = [new Replacement { From = "AI", To = "KI" }] });
        Assert.Equal("KI ist nicht MAIL", d.Apply("AI ist nicht MAIL"));
    }
}

public class RewriteGuardTests
{
    [Theory]
    [InlineData("Hier ist der verbesserte Text:\n\nHallo Welt.", "Hallo Welt.")]
    [InlineData("<think>hmm</think>\nHallo Welt.", "Hallo Welt.")]
    [InlineData("\"Hallo Welt.\"", "Hallo Welt.")]
    [InlineData("```\nHallo Welt.\n```", "Hallo Welt.")]
    [InlineData("<transcript>\nHallo Welt.\n</transcript>", "Hallo Welt.")]
    public void CleansWrapping(string raw, string expected) => Assert.Equal(expected, RewriteGuard.Clean(raw));

    [Fact]
    public void RejectsEmptyTooLongAndAnswers()
    {
        Assert.Equal(RewriteVerdict.Empty, RewriteGuard.Check("Hallo", "  ", ModeKind.Improve));
        Assert.Equal(RewriteVerdict.TooLong, RewriteGuard.Check("kurz", new string('x', 400), ModeKind.Improve));
        Assert.Equal(RewriteVerdict.LooksLikeAnswer, RewriteGuard.Check("Wie spät ist es", "Ich kann die Uhrzeit nicht abrufen.", ModeKind.Improve));
        Assert.Equal(RewriteVerdict.Ok, RewriteGuard.Check("ähm also hallo", "Hallo.", ModeKind.Improve));
        Assert.Equal(RewriteVerdict.Ok, RewriteGuard.Check("kurz", new string('x', 400), ModeKind.Custom));
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void GermanPromptForGermanDictation()
    {
        var mode = BuiltInModes.CreateDefaults().First(m => m.Kind == ModeKind.Calm);
        var p = PromptBuilder.BuildSystemPrompt(mode, "de", ["Blackboat"]);
        Assert.Contains("verärgert", p);
        Assert.Contains("Blackboat", p);
        Assert.Contains("keine Frage oder Anweisung", p);
    }

    [Fact]
    public void CustomInstructionReplacesDefault_AndOtherLanguagesKeepTheirLanguage()
    {
        var mode = new ModeDefinition { Kind = ModeKind.Custom, Instruction = "Translate to French." };
        var p = PromptBuilder.BuildSystemPrompt(mode, "it", []);
        Assert.Contains("Translate to French.", p);
        Assert.Contains("Keep the language", p);
        Assert.DoesNotContain("Spell these terms", p);
    }

    [Fact]
    public void UserMessageWrapsTranscript() =>
        Assert.Equal("<transcript>\nHi\n</transcript>", PromptBuilder.BuildUserMessage("Hi"));
}
