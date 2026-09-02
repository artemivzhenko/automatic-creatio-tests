using System.Text.Json;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Composite;
using CreatioAutoTests.Frontend.Controls;
using CreatioAutoTests.Tests.Support;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ElementCatalogTests
{
    [Test]
    public void Codes_are_unique_per_page_but_can_recur_on_another_page()
    {
        var catalog = CatalogBuilder.Build(CatalogBuilder.Element("Name", "Text", "First"), CatalogBuilder.Element("Name", "Text", "Second"));

        Assert.That(catalog.TryGetElement("First", "Name", out _), Is.True);
        Assert.That(catalog.TryGetElement("Second", "Name", out _), Is.True);
        Assert.That(catalog.PageNames, Is.EquivalentTo(new[] { "First", "Second" }));
        Assert.That(catalog.CodesOnPage("First"), Is.EqualTo(new[] { "Name" }));
    }

    [Test]
    public void Duplicate_codes_unknown_types_and_other_faults_are_reported_together()
    {
        var page = new PageDefinition { Name = "Page", Url = "/page", SourceFile = "page.json" };
        page.Elements.Add(CatalogBuilder.Element("Duplicate", "Text"));
        page.Elements.Add(CatalogBuilder.Element("Duplicate", "Text"));
        page.Elements.Add(CatalogBuilder.Element("Unknown", "Slider"));
        var missingUrl = new PageDefinition { Name = "Broken", SourceFile = "broken.json" };

        var error = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.BuildPages(page, missingUrl))!;

        Assert.That(error.Message, Does.Contain("declared twice"));
        Assert.That(error.Message, Does.Contain("Slider"));
        Assert.That(error.Message, Does.Contain("Known types"));
        Assert.That(error.Message, Does.Contain("has no url"));
    }

    [Test]
    public void Options_are_validated_bound_and_capabilities_are_preserved()
    {
        var bar = CatalogBuilder.Element("Stages", "CasesBar");
        bar.Extra = ParseExtra("{\"values\":[\"A\",\"B\"],\"subvalues\":{\"A\":[\"A1\"]}}");
        var button = CatalogBuilder.Element("Save", "Button");
        button.Extra = ParseExtra("{\"isSave\":true,\"doubleClick\":true}");
        var catalog = CatalogBuilder.Build(bar, button);
        catalog.TryGetElement("TestPage", "Stages", out var barEntry);
        catalog.TryGetElement("TestPage", "Save", out var buttonEntry);

        var barOptions = barEntry.Definition.BindOptions<CasesBarOptions>();
        var buttonOptions = buttonEntry.Definition.BindOptions<ButtonOptions>();
        Assert.That(barOptions.Values, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(barOptions.Subvalues!["A"], Is.EqualTo(new[] { "A1" }));
        Assert.That(buttonOptions.IsSave && buttonOptions.DoubleClick, Is.True);
        Assert.That(buttonEntry.Supports(typeof(IClickable)), Is.True);
        Assert.That(buttonEntry.Supports(typeof(IValueElement)), Is.False);
    }

    [TestCase("Button", "{\"unexpected\":true}")]
    [TestCase("CasesBar", "{\"subvalue\":{}}")]
    public void Invalid_extra_members_are_rejected(string type, string json)
    {
        var element = CatalogBuilder.Element("Element", type);
        element.Extra = ParseExtra(json);

        var error = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.Build(element))!;

        Assert.That(error.Message, Does.Contain("Element"));
        Assert.That(error.Message, Does.Contain(type));
    }

    private static Dictionary<string, JsonElement> ParseExtra(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}