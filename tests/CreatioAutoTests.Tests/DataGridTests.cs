using System.Text.Json;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Composite;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class DataGridElementTests
{
    private readonly UiElementRegistry _elements = UiElementRegistry.Discover();
    private readonly CommandRegistry _commands = CommandRegistry.Discover();

    [Test]
    public void The_grid_type_is_discovered_from_its_attribute()
    {
        var registration = _elements.Resolve("DataGrid");

        Assert.Multiple(() =>
        {
            Assert.That(registration.ImplementationType, Is.EqualTo(typeof(DataGrid)));
            Assert.That(registration.HostTag, Is.EqualTo("crt-data-grid"));
            Assert.That(registration.ValueKind, Is.EqualTo(ValueKind.None));
        });
    }

    [Test]
    public void A_grid_reports_editability_but_holds_no_value()
    {
        var registration = _elements.Resolve("DataGrid");

        Assert.Multiple(() =>
        {
            Assert.That(registration.Capabilities, Does.Contain(typeof(IEditableAware)));
            Assert.That(registration.Capabilities, Does.Contain(typeof(IColumned)));
            Assert.That(registration.Capabilities, Does.Contain(typeof(IExistable)));
            Assert.That(registration.Capabilities, Does.Not.Contain(typeof(IValueElement)));
            Assert.That(registration.Capabilities, Does.Not.Contain(typeof(IStateAware)));
        });
    }

    /// <summary>
    /// A grid borrows its panel's caption; the buttons sitting in the same panel must not, or every
    /// grid toolbar would be recorded under the grid's own name.
    /// </summary>
    [Test]
    public void Only_the_grid_is_named_by_the_panel_it_sits_in()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_elements.Resolve("DataGrid").LabeledByPanel, Is.True);
            Assert.That(_elements.Resolve("Button").LabeledByPanel, Is.False);
            Assert.That(_elements.Resolve("Text").LabeledByPanel, Is.False);
        });
    }

    [Test]
    public void Check_accepts_a_grid_without_naming_it()
    {
        _commands.TryResolve("check", out var check);

        Assert.That(check.Accepts(typeof(DataGrid)), Is.True);
        Assert.That(check.SupportedTargets, Does.Not.Contain(typeof(DataGrid)));
    }

    [Test]
    public void A_grid_cannot_be_used_where_a_value_is_expected()
    {
        _commands.TryResolve("set", out var set);
        _commands.TryResolve("check-value", out var checkValue);

        Assert.Multiple(() =>
        {
            Assert.That(set.Accepts(typeof(DataGrid)), Is.False);
            Assert.That(checkValue.Accepts(typeof(DataGrid)), Is.False);
        });
    }
}

[TestFixture]
public sealed class EditableValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        var withColumns = CatalogBuilder.Element("AddressList", "DataGrid");
        withColumns.Extra = Extra("{\"columns\":[\"Address type\",\"Full address\"]}");

        _catalog = CatalogBuilder.Build(
            withColumns,
            CatalogBuilder.Element("Notes", "DataGrid"),
            CatalogBuilder.Element("Name", "Text"),
            CatalogBuilder.Element("SaveButton", "Button", label: "Save"));

        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private static Dictionary<string, JsonElement> Extra(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private ExecutionPlan Validate(params string[] lines)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string>(),
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate("grids", "grids.act", _parser.Parse(lines), context);
    }

    [TestCase("check AddressList exists")]
    [TestCase("check AddressList not exists")]
    [TestCase("check AddressList is editable")]
    [TestCase("check AddressList is not editable")]
    [TestCase("check AddressList columns")]
    public void Every_supported_form_validates(string line)
    {
        var plan = Validate("use tester", "open TestPage", line);

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    /// <summary>Editability belongs to a grid; a text field has no such state to assert.</summary>
    [Test]
    public void Editable_is_rejected_for_an_element_that_has_no_such_state()
    {
        var plan = Validate("use tester", "open TestPage", "check Name is editable");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("does not expose an editable state"));
    }

    /// <summary>The mirror image: required and read-only describe a field's value, not a grid.</summary>
    [Test]
    public void Required_and_readonly_are_rejected_for_a_grid()
    {
        foreach (var property in new[] { "required", "readonly" })
        {
            var plan = Validate("use tester", "open TestPage", "check AddressList is " + property);

            Assert.That(plan.IsValid, Is.False, property);
            Assert.That(plan.Issues[0].Message, Does.Contain("required/readonly state"));
        }
    }

    /// <summary>A column check with nothing configured could never fail, so it is not a valid line.</summary>
    [Test]
    public void Columns_are_rejected_when_the_page_file_declares_none()
    {
        var plan = Validate("use tester", "open TestPage", "check Notes columns");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("declares no 'columns'"));
    }

    [Test]
    public void Columns_are_rejected_for_an_element_that_has_none()
    {
        var plan = Validate("use tester", "open TestPage", "check Name columns");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("has no columns to check"));
    }

    [Test]
    public void An_unknown_property_lists_the_ones_that_exist()
    {
        var plan = Validate("use tester", "open TestPage", "check AddressList is sortable");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("'required', 'readonly' and 'editable'"));
    }
}
