using System.Text.Json;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Controls;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ToggleTabRegistrationTests
{
    private readonly UiElementRegistry _elements = UiElementRegistry.Discover();
    private readonly CommandRegistry _commands = CommandRegistry.Discover();

    [Test]
    public void The_toggle_group_is_discovered_from_its_attribute()
    {
        var registration = _elements.Resolve("ToggleTab");

        Assert.Multiple(() =>
        {
            Assert.That(registration.ImplementationType, Is.EqualTo(typeof(ToggleTab)));
            Assert.That(registration.HostTag, Is.EqualTo("crt-button-toggle-group"));
            Assert.That(registration.ValueKind, Is.EqualTo(ValueKind.Tab));
            Assert.That(registration.OptionsType, Is.EqualTo(typeof(ToggleTabOptions)));
        });
    }

    /// <summary>
    /// The two kinds of tab are mirror images: a tab panel gives every tab its own element-name and
    /// is clicked, a toggle group has one name for the whole group and carries the open tab as its
    /// value. Neither should be addressable the other way round.
    /// </summary>
    [Test]
    public void A_normal_tab_is_clicked_and_a_toggle_group_holds_a_value()
    {
        var tab = _elements.Resolve("Tab");
        var toggle = _elements.Resolve("ToggleTab");

        Assert.Multiple(() =>
        {
            Assert.That(tab.Capabilities, Does.Contain(typeof(IClickable)));
            Assert.That(tab.Capabilities, Does.Not.Contain(typeof(IValueElement)));
            Assert.That(tab.HostTag, Is.Not.EqualTo(toggle.HostTag));

            Assert.That(toggle.Capabilities, Does.Contain(typeof(IValueElement)));
            Assert.That(toggle.Capabilities, Does.Not.Contain(typeof(IClickable)));
        });
    }

    [Test]
    public void The_commands_that_accept_each_kind_follow_from_its_capabilities()
    {
        _commands.TryResolve("set", out var set);
        _commands.TryResolve("check-value", out var checkValue);
        _commands.TryResolve("click", out var click);

        Assert.Multiple(() =>
        {
            Assert.That(set.Accepts(typeof(ToggleTab)), Is.True);
            Assert.That(checkValue.Accepts(typeof(ToggleTab)), Is.True);
            Assert.That(click.Accepts(typeof(ToggleTab)), Is.False);
            Assert.That(click.Accepts(typeof(Tab)), Is.True);
        });
    }
}

[TestFixture]
public sealed class ToggleTabValueRulesTests
{
    private readonly ValueCompatibilityPolicy _policy = new();

    [Test]
    public void An_open_tab_needs_a_caption()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_policy.IsLiteralAssignable("Tab 2", ValueKind.Tab, out _), Is.True);
            Assert.That(_policy.IsLiteralAssignable(string.Empty, ValueKind.Tab, out var error), Is.False);
            Assert.That(error, Does.Contain("caption of the tab"));
        });
    }

    /// <summary>A tab caption is text, so it copies into a text field; a number is not a caption.</summary>
    [Test]
    public void A_tab_caption_copies_only_where_it_makes_sense()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_policy.CanCopy(ValueKind.Tab, ValueKind.Text, out _), Is.True);
            Assert.That(_policy.CanCopy(ValueKind.Text, ValueKind.Tab, out _), Is.True);
            Assert.That(_policy.CanCopy(ValueKind.Number, ValueKind.Tab, out _), Is.False);
            Assert.That(_policy.CanCopy(ValueKind.Tab, ValueKind.Number, out _), Is.False);
        });
    }
}

[TestFixture]
public sealed class ToggleTabValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        var group = CatalogBuilder.Element("ViewToggle", "ToggleTab", label: string.Empty);
        group.Extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{\"tabs\":[\"Tab 1\",\"Tab 2\"]}")!;

        _catalog = CatalogBuilder.Build(
            group,
            CatalogBuilder.Element("GeneralInfoTab", "Tab"),
            CatalogBuilder.Element("Name", "Text"),
            CatalogBuilder.Element("Count", "Integer"));

        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private ExecutionPlan Validate(string line)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string>(),
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate(
            "toggles",
            "toggles.act",
            _parser.Parse(new[] { "use tester", "open TestPage", line }),
            context);
    }

    [TestCase("check ViewToggle exists")]
    [TestCase("set ViewToggle \"Tab 2\"")]
    [TestCase("check-value ViewToggle eq \"Tab 2\"")]
    [TestCase("check-value ViewToggle contains \"Tab\"")]
    [TestCase("set Name ViewToggle")]
    public void Every_supported_form_validates(string line)
    {
        var plan = Validate(line);

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    /// <summary>A toggle group is switched by naming the tab, not by clicking the group.</summary>
    [Test]
    public void Click_is_rejected_for_a_toggle_group()
    {
        var plan = Validate("click ViewToggle");

        Assert.That(plan.IsValid, Is.False);
    }

    [Test]
    public void An_empty_caption_is_rejected()
    {
        var plan = Validate("set ViewToggle \"\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("caption of the tab"));
    }

    [Test]
    public void A_tab_caption_cannot_be_copied_into_a_number()
    {
        var plan = Validate("set Count ViewToggle");

        Assert.That(plan.IsValid, Is.False);
    }
}
