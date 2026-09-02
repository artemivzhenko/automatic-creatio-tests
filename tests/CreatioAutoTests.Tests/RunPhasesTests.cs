using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Runner;

namespace CreatioAutoTests.Tests;

/// <summary>
/// The analysis phase is opt-in and reloads the catalog. These cover the parts that can be checked
/// without a browser: the settings, the discovery split, and the reload itself.
/// </summary>
[TestFixture]
public sealed class RunPhasesTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "creatio-phases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "pages"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WritePage(string name, params string[] codes)
    {
        var elements = string.Join(",", codes.Select(c =>
            $$"""{ "code": "{{c}}", "label": "{{c}}", "type": "Text" }"""));

        File.WriteAllText(
            Path.Combine(_root, "pages", name + ".json"),
            $$"""{ "name": "{{name}}", "url": "/{{name}}", "elements": [{{elements}}] }""");
    }

    private ElementCatalog Load() =>
        ElementCatalog.Build(new ConfigurationLoader(_root).LoadPages("pages"), CatalogBuilderRegistry);

    private static readonly UiElementRegistry CatalogBuilderRegistry = UiElementRegistry.Discover();

    [Test]
    public void Analysis_is_off_unless_it_is_asked_for()
    {
        Assert.That(new RunSettings().RunAnalysis, Is.False);
    }

    [Test]
    public void No_analysis_folders_are_configured_by_default()
    {
        Assert.That(new EnvironmentConfig().AnalysisFolders, Is.Empty);
    }

    [Test]
    public void Analysis_and_case_folders_are_separate_settings()
    {
        var environment = new EnvironmentConfig
        {
            CasesFolders = { "./Cases" },
            AnalysisFolders = { "./Analysis" },
        };

        Assert.That(environment.CasesFolders, Is.EqualTo(new[] { "./Cases" }));
        Assert.That(environment.AnalysisFolders, Is.EqualTo(new[] { "./Analysis" }));
    }

    /// <summary>
    /// The behaviour the two phases rest on: what analysis writes has to be visible to the
    /// validation of the test cases that follow it in the same run.
    /// </summary>
    [Test]
    public void Reloading_the_catalog_picks_up_a_page_written_after_startup()
    {
        WritePage("First", "Alpha");
        var catalog = Load();

        Assert.That(catalog.PageExists("Second"), Is.False);

        WritePage("Second", "Beta");
        var reloaded = Load();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PageExists("Second"), Is.True);
            Assert.That(reloaded.TryGetElement("Second", "Beta", out _), Is.True);
        });
    }

    [Test]
    public void Reloading_picks_up_elements_added_to_an_existing_page()
    {
        WritePage("First", "Alpha");
        Assert.That(Load().TryGetElement("First", "Gamma", out _), Is.False);

        WritePage("First", "Alpha", "Gamma");

        Assert.That(Load().TryGetElement("First", "Gamma", out _), Is.True);
    }
}
