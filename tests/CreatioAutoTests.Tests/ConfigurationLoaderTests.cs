using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Configuration;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ConfigurationLoaderTests
{
    private string _fixtureDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _fixtureDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDirectory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_fixtureDirectory, recursive: true);

    [Test]
    public void A_missing_required_file_throws_with_the_full_path_in_the_message()
    {
        var loader = new ConfigurationLoader(_fixtureDirectory);
        var error = Assert.Throws<FileNotFoundException>(() => loader.Load<Fixture>("missing.json"))!;

        Assert.That(error.Message, Does.Contain(loader.Resolve("missing.json")));
    }

    [Test]
    public void Malformed_JSON_names_the_file_and_expected_type()
    {
        File.WriteAllText(Path.Combine(_fixtureDirectory, "broken.json"), "{");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationLoader(_fixtureDirectory).Load<Fixture>("broken.json"))!;

        Assert.That(error.Message, Does.Contain("broken.json"));
        Assert.That(error.Message, Does.Contain(nameof(Fixture)));
    }

    [Test]
    public void Optional_files_default_and_permissive_JSON_is_accepted()
    {
        var loader = new ConfigurationLoader(_fixtureDirectory);
        Assert.That(loader.LoadOrDefault<Fixture>("absent.json").Name, Is.EqualTo(string.Empty));

        File.WriteAllText(Path.Combine(_fixtureDirectory, "fixture.json"), "{ // accepted\n \"name\": \"value\", }");
        Assert.That(loader.Load<Fixture>("fixture.json").Name, Is.EqualTo("value"));
    }

    [Test]
    public void Static_values_flatten_all_supported_scalar_types()
    {
        File.WriteAllText(Path.Combine(_fixtureDirectory, "values.json"),
            "{\"text\":\"x\",\"integer\":2,\"float\":2.5,\"yes\":true,\"no\":false,\"nothing\":null}");

        var values = new ConfigurationLoader(_fixtureDirectory).LoadStaticValues("values.json");

        Assert.That(values, Is.EqualTo(new Dictionary<string, string>
        {
            ["text"] = "x", ["integer"] = "2", ["float"] = "2.5", ["yes"] = "true", ["no"] = "false", ["nothing"] = ""
        }));
    }

    private sealed class Fixture
    {
        public string Name { get; set; } = string.Empty;
    }
}
[TestFixture]
public sealed class PageDirectoryLoaderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "creatio-pages-" + Guid.NewGuid().ToString("N"));
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

    private void WritePage(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_root, "pages", fileName), json);

    private IReadOnlyList<PageDefinition> Load() =>
        new ConfigurationLoader(_root).LoadPages("pages");

    [Test]
    public void Each_file_contributes_one_page()
    {
        WritePage("a.json", """{ "name": "A", "url": "/a", "elements": [] }""");
        WritePage("b.json", """{ "name": "B", "url": "/b", "elements": [] }""");

        Assert.That(Load().Select(p => p.Name), Is.EquivalentTo(new[] { "A", "B" }));
    }

    [Test]
    public void Elements_inherit_the_page_they_are_declared_under()
    {
        WritePage("a.json",
            """{ "name": "A", "url": "/a", "elements": [ { "code": "Name", "label": "Name", "type": "Text" } ] }""");

        var page = Load().Single();

        Assert.That(page.Elements.Single().Page, Is.EqualTo("A"));
    }

    [Test]
    public void The_originating_file_is_remembered_so_errors_can_point_at_it()
    {
        WritePage("contact-form.json", """{ "name": "A", "url": "/a", "elements": [] }""");

        Assert.That(Load().Single().SourceFile, Is.EqualTo("contact-form.json"));
    }

    [Test]
    public void A_page_declared_in_two_files_is_rejected_and_both_files_are_named()
    {
        WritePage("first.json", """{ "name": "Same", "url": "/a", "elements": [] }""");
        WritePage("second.json", """{ "name": "Same", "url": "/b", "elements": [] }""");

        var error = Assert.Throws<InvalidOperationException>(
            () => ElementCatalog.Build(Load(), CatalogBuilder.Registry))!;

        Assert.That(error.Message, Does.Contain("first.json"));
        Assert.That(error.Message, Does.Contain("second.json"));
    }

    [Test]
    public void A_malformed_file_is_reported_with_its_path()
    {
        WritePage("broken.json", "{ this is not json }");

        var error = Assert.Throws<InvalidOperationException>(() => Load())!;

        Assert.That(error.Message, Does.Contain("broken.json"));
    }

    [Test]
    public void A_missing_directory_says_what_it_expected_to_find()
    {
        var error = Assert.Throws<DirectoryNotFoundException>(
            () => new ConfigurationLoader(_root).LoadPages("nowhere"))!;

        Assert.That(error.Message, Does.Contain("own .json file"));
    }

    [Test]
    public void An_empty_directory_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Load());
    }
}
