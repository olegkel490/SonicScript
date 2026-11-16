using System.Text.RegularExpressions;
using SonicScript.Core.Localization;

namespace SonicScript.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void GermanAndEnglishHaveTheSameKeys()
    {
        Assert.Empty(Strings.German.Keys.Except(Strings.English.Keys));
        Assert.Empty(Strings.English.Keys.Except(Strings.German.Keys));
    }

    [Fact]
    public void PlaceholdersMatch()
    {
        foreach (var (key, de) in Strings.German)
        {
            var en = Strings.English[key];
            Assert.True(Placeholders(de).SetEquals(Placeholders(en)), $"Platzhalter in '{key}' unterschiedlich");
        }
    }

    [Fact]
    public void AllKeysUsedInTheAppExist()
    {
        var root = FindRepoRoot();
        var app = Path.Combine(root, "src", "SonicScript.App");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{l:Tr ([a-z0-9_.]+)\}")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Strings\.(?:Get|Format)\(""([a-z0-9_.]*[a-z0-9_])""")) used.Add(m.Groups[1].Value);
        }
        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, k => !Strings.German.ContainsKey(k));
    }

    [Fact]
    public void EnumValuesHaveTexts()
    {
        foreach (var t in new[] { typeof(Hotkeys.TriggerMode), typeof(Settings.InsertionMethod), typeof(Settings.TranscriptionEngine), typeof(Settings.ProviderPreset), typeof(Settings.DockSide) })
            foreach (Enum v in Enum.GetValues(t))
                Assert.True(Strings.German.ContainsKey($"enum.{t.Name}.{v}"), $"enum.{t.Name}.{v}");
    }

    [Fact]
    public void ModelsHaveTexts() =>
        Assert.All(Transcription.WhisperModelCatalog.All, m => Assert.True(Strings.German.ContainsKey("model." + m.Id), m.Id));

    [Fact]
    public void FallsBackToKey() => Assert.Equal("gibt.es.nicht", Strings.Get("gibt.es.nicht"));

    private static HashSet<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SonicScript.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden");
    }
}
