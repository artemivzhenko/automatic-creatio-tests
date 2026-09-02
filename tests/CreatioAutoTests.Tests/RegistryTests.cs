using CreatioAutoTests.Commands;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Controls;
using CreatioAutoTests.Frontend.Fields;
using CreatioAutoTests.Frontend.Registry;

namespace CreatioAutoTests.Tests;

[TestFixture]
public class UiElementRegistryTests
{
    private readonly UiElementRegistry _registry = UiElementRegistry.Discover();

    [Test]
    public void Element_types_are_found_by_their_configuration_name()
    {
        Assert.That(_registry.Resolve("Lookup").ImplementationType, Is.EqualTo(typeof(LookupField)));
        Assert.That(_registry.Resolve("Button").ImplementationType, Is.EqualTo(typeof(Button)));
    }

    [Test]
    public void The_configuration_name_is_matched_case_insensitively()
    {
        Assert.That(_registry.TryResolve("lookup", out _), Is.True);
        Assert.That(_registry.TryResolve("LOOKUP", out _), Is.True);
    }

    [Test]
    public void An_unknown_type_names_the_types_that_do_exist()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _registry.Resolve("Slider"))!;

        Assert.That(error.Message, Does.Contain("Slider"));
        Assert.That(error.Message, Does.Contain("Lookup"));
    }

    [Test]
    public void Value_kind_is_available_without_creating_the_element()
    {
        Assert.That(_registry.Resolve("Lookup").ValueKind, Is.EqualTo(ValueKind.Lookup));
        Assert.That(_registry.Resolve("DateTime").ValueKind, Is.EqualTo(ValueKind.DateTime));
        Assert.That(_registry.Resolve("Button").ValueKind, Is.EqualTo(ValueKind.None));
    }

    [Test]
    public void Two_types_claiming_the_same_name_is_rejected()
    {
        var duplicate = new[]
        {
            new UiElementRegistration("Text", typeof(TextField), ValueKind.Text, "crt-input", null),
            new UiElementRegistration("Text", typeof(RichTextField), ValueKind.Text, "crt-rich-text-editor", null)
        };

        Assert.Throws<InvalidOperationException>(() => _ = new UiElementRegistry(duplicate));
    }
}

[TestFixture]
public class CommandRegistryTests
{
    private readonly CommandRegistry _registry = CommandRegistry.Discover();

    [Test]
    public void Commands_are_found_by_their_keyword()
    {
        Assert.That(_registry.TryResolve("set", out var set), Is.True);
        Assert.That(set.ImplementationType, Is.EqualTo(typeof(SetCommand)));
    }

    [Test]
    public void Every_discovered_command_declares_a_validator()
    {
        Assert.That(_registry.Registrations.All(r => r.ValidatorType is not null), Is.True);
    }

    [Test]
    public void Click_accepts_anything_clickable_rather_than_a_fixed_class()
    {
        _registry.TryResolve("click", out var click);

        Assert.That(click.SupportedTargets, Is.EqualTo(new[] { typeof(IClickable) }));
        Assert.That(click.Accepts(typeof(Button)), Is.True);
        Assert.That(click.Accepts(typeof(TextField)), Is.False);
    }

    [Test]
    public void A_command_without_targets_accepts_nothing_in_particular()
    {
        _registry.TryResolve("use", out var use);

        Assert.That(use.IsTargetless, Is.True);
    }
}
