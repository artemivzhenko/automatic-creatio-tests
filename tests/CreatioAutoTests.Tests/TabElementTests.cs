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
public sealed class TabElementTests
{
    private readonly UiElementRegistry _elements = UiElementRegistry.Discover();
    private readonly CommandRegistry _commands = CommandRegistry.Discover();

    [Test]
    public void The_tab_type_is_discovered_from_its_attribute_alone()
    {
        Assert.That(_elements.Resolve("Tab").ImplementationType, Is.EqualTo(typeof(Tab)));
    }

    [Test]
    public void A_tab_carries_no_value()
    {
        Assert.That(_elements.Resolve("Tab").ValueKind, Is.EqualTo(ValueKind.None));
    }

    [Test]
    public void A_tab_offers_exactly_the_capabilities_the_two_commands_need()
    {
        var registration = _elements.Resolve("Tab");

        Assert.Multiple(() =>
        {
            Assert.That(registration.Capabilities, Does.Contain(typeof(IClickable)));
            Assert.That(registration.Capabilities, Does.Contain(typeof(IExistable)));
            Assert.That(registration.Capabilities, Does.Contain(typeof(ILabeled)));
            Assert.That(registration.Capabilities, Does.Not.Contain(typeof(IValueElement)));
        });
    }

    /// <summary>
    /// The point of the capability design: neither command was touched to gain tab support.
    /// </summary>
    [Test]
    public void Click_and_check_accept_a_tab_without_naming_it()
    {
        _commands.TryResolve("click", out var click);
        _commands.TryResolve("check", out var check);

        Assert.Multiple(() =>
        {
            Assert.That(click.Accepts(typeof(Tab)), Is.True);
            Assert.That(check.Accepts(typeof(Tab)), Is.True);
            Assert.That(click.SupportedTargets, Does.Not.Contain(typeof(Tab)));
            Assert.That(check.SupportedTargets, Does.Not.Contain(typeof(Tab)));
        });
    }

    [Test]
    public void A_tab_cannot_be_used_where_a_value_is_expected()
    {
        _commands.TryResolve("set", out var set);
        _commands.TryResolve("check-value", out var checkValue);

        Assert.Multiple(() =>
        {
            Assert.That(set.Accepts(typeof(Tab)), Is.False);
            Assert.That(checkValue.Accepts(typeof(Tab)), Is.False);
        });
    }
}

[TestFixture]
public sealed class TabValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        _catalog = CatalogBuilder.Build(
            CatalogBuilder.Element("GeneralInfoTab", "Tab", label: "Contact info"),
            CatalogBuilder.Element("TimelineTab", "Tab", label: "Timeline"),
            CatalogBuilder.Element("Name", "Text"),
            CatalogBuilder.Element("SaveButton", "Button", label: "Save"));

        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private ExecutionPlan Validate(params string[] lines)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string>(),
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate("tabs", "tabs.act", _parser.Parse(lines), context);
    }

    [Test]
    public void Clicking_a_tab_validates()
    {
        var plan = Validate("use tester", "open TestPage", "click GeneralInfoTab");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
        Assert.That(plan.Steps[2].Targets[0].ElementType, Is.EqualTo(typeof(Tab)));
    }

    [Test]
    public void Asserting_that_a_tab_exists_validates()
    {
        var plan = Validate("use tester", "open TestPage", "check TimelineTab exists");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void Asserting_that_a_tab_is_absent_validates()
    {
        var plan = Validate("use tester", "open TestPage", "check TimelineTab not exists");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void A_tab_that_is_not_configured_is_reported_with_the_tabs_that_are()
    {
        var plan = Validate("use tester", "open TestPage", "click MarketingTab");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("MarketingTab"));
        Assert.That(plan.Issues[0].Message, Does.Contain("GeneralInfoTab"));
    }

    [Test]
    public void Required_and_readonly_do_not_apply_to_a_tab()
    {
        var plan = Validate("use tester", "open TestPage", "check GeneralInfoTab is required");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("does not expose required/readonly"));
    }

    [Test]
    public void Setting_a_value_on_a_tab_is_rejected()
    {
        var plan = Validate("use tester", "open TestPage", "set GeneralInfoTab \"x\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("does not hold a value"));
    }
}
