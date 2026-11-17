using System.Globalization;
using System.Xml.Linq;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Settings;

/// <summary>
/// Resource parity across every supported language and the static localization bridges, which must
/// resolve against the compiled resource base names and fall back to English literals.
/// </summary>
[Trait("Stage", "Settings")]
public class LocalizationTests
{
    private static string ResourcesDir => Path.Combine(RepositoryPaths.ProjectDirectory, "Resources");

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    public static TheoryData<string> NeutralResources()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(RepositoryPaths.ProjectDirectory, "Resources"), "*.resx", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileNameWithoutExtension(f).Split('.').Length == 1))
            data.Add(Path.GetRelativePath(Path.Combine(RepositoryPaths.ProjectDirectory, "Resources"), f));
        return data;
    }

    private static IEnumerable<string> Cultures => SupportedLanguage.All.Select(l => l.Code).Where(c => c != SupportedLanguage.DefaultCode);

    [Theory]
    [MemberData(nameof(NeutralResources))]
    public void Every_language_has_every_key(string neutral)
    {
        var baseKeys = Read(Path.Combine(ResourcesDir, neutral)).Keys.ToHashSet();
        var problems = new List<string>();

        foreach (var culture in Cultures)
        {
            var path = Path.Combine(ResourcesDir, Path.ChangeExtension(neutral, $".{culture}.resx"));
            if (!File.Exists(path)) continue;

            var missing = baseKeys.Except(Read(path).Keys).ToList();
            if (missing.Count > 0) problems.Add($"{culture}: missing {string.Join(", ", missing)}");
        }

        Assert.True(problems.Count == 0, $"{neutral}:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(NeutralResources))]
    public void Every_language_file_exists_without_orphan_keys(string neutral)
    {
        var baseKeys = Read(Path.Combine(ResourcesDir, neutral)).Keys.ToHashSet();
        var problems = new List<string>();

        foreach (var culture in Cultures)
        {
            var path = Path.Combine(ResourcesDir, Path.ChangeExtension(neutral, $".{culture}.resx"));
            if (!File.Exists(path)) { problems.Add($"{culture}: file missing"); continue; }

            var extra = Read(path).Keys.Except(baseKeys).ToList();
            if (extra.Count > 0) problems.Add($"{culture}: orphan {string.Join(", ", extra)}");
        }

        Assert.True(problems.Count == 0, $"{neutral}:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(NeutralResources))]
    public void Translations_keep_format_placeholders(string neutral)
    {
        var baseValues = Read(Path.Combine(ResourcesDir, neutral));
        var problems = new List<string>();

        foreach (var culture in Cultures)
        {
            var path = Path.Combine(ResourcesDir, Path.ChangeExtension(neutral, $".{culture}.resx"));
            if (!File.Exists(path)) continue;
            foreach (var (key, value) in Read(path))
            {
                if (!baseValues.TryGetValue(key, out var english)) continue;
                var expected = Placeholders(english);
                var actual = Placeholders(value);
                if (!expected.SetEquals(actual))
                    problems.Add($"{culture}/{key}: expected {{{string.Join(",", expected)}}} got {{{string.Join(",", actual)}}}");
            }
        }

        Assert.True(problems.Count == 0, $"{neutral}:\n" + string.Join("\n", problems.Take(25)));
    }

    private static HashSet<string> Placeholders(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"(?<!\{)\{(\d+)(?:[:,][^}]*)?\}(?!\})")
            .Select(m => m.Groups[1].Value).ToHashSet();

    [Theory]
    [MemberData(nameof(NeutralResources))]
    public void Resx_files_are_well_formed_without_duplicate_keys(string neutral)
    {
        foreach (var file in new[] { neutral }.Concat(Cultures.Select(c => Path.ChangeExtension(neutral, $".{c}.resx"))))
        {
            var path = Path.Combine(ResourcesDir, file);
            if (!File.Exists(path)) continue;
            var names = XDocument.Load(path).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToList();
            var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, $"{file}: duplicate keys {string.Join(", ", dupes)}");
        }
    }

    // ---------------------------------------------------------------- bridges

    private static T InCulture<T>(string culture, Func<T> f)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try { return f(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static (string Key, string English, string French) Sample(string resource, string prefix)
    {
        var en = Read(Path.Combine(ResourcesDir, resource + ".resx"));
        var fr = Read(Path.Combine(ResourcesDir, resource + ".fr.resx"));
        var key = en.Keys.First(k => k.StartsWith(prefix, StringComparison.Ordinal)
                                     && fr.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && v != en[k]);
        return (key, en[key], fr[key]);
    }

    [Fact]
    public void Kpi_localizer_resolves_compiled_resources_per_culture()
    {
        KpiLocalizer.Initialize(null!);
        var (key, english, french) = Sample("KpiResources", "");

        Assert.Equal(french, InCulture("fr", () => KpiLocalizer.Localize(key, "fallback")));
        Assert.Equal(english, InCulture("en", () => KpiLocalizer.Localize(key, "fallback")));
    }

    [Fact]
    public void Assessment_localizer_resolves_compiled_resources_per_culture()
    {
        AssessmentLocalizer.Initialize(null!);
        var (key, _, french) = Sample("AssessmentResources", "RuleTitle_");
        var ruleId = key["RuleTitle_".Length..];

        Assert.Equal(french, InCulture("fr", () => AssessmentLocalizer.Title(ruleId, "fallback")));
    }

    [Fact]
    public void Mitre_localizer_resolves_compiled_resources_per_culture()
    {
        MitreLocalizer.Initialize(null!);
        var (key, _, french) = Sample("MitreResources", "Tech_");
        var parts = key.Split('_');
        if (parts[^1] != "Name") return;

        Assert.Equal(french, InCulture("fr", () => MitreLocalizer.TechniqueName(parts[1], "fallback")));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("ja")]
    public void Unknown_keys_fall_back_to_the_literal(string culture)
    {
        KpiLocalizer.Initialize(null!);
        AssessmentLocalizer.Initialize(null!);
        MitreLocalizer.Initialize(null!);

        Assert.Equal("lit", InCulture(culture, () => KpiLocalizer.Localize("No_Such_Key_123", "lit")));
        Assert.Equal("lit", InCulture(culture, () => AssessmentLocalizer.Title("NO-SUCH-RULE", "lit")));
        Assert.Equal("lit", InCulture(culture, () => MitreLocalizer.TechniqueName("T0000", "lit")));
    }

    [Fact]
    public void Empty_key_returns_fallback()
    {
        KpiLocalizer.Initialize(null!);

        Assert.Equal("lit", KpiLocalizer.Localize("", "lit"));
        Assert.Equal("lit", AssessmentLocalizer.Localize("", "lit"));
    }

    [Theory]
    [InlineData("CAF B2 Identity & Access Control")]
    [InlineData("Unknown Category !!")]
    public void Category_falls_back_to_original_name(string category)
    {
        AssessmentLocalizer.Initialize(null!);

        var result = InCulture("en", () => AssessmentLocalizer.Category(category));

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
