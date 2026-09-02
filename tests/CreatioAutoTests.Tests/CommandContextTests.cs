using CreatioAutoTests.Commands;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class CommandContextTests
{
    [TestCase("\"DefaultName\"", "DefaultName")]
    [TestCase("DefaultName", "Contoso")]
    [TestCase("literal", "literal")]
    public void ResolveValue_honours_quoting_and_static_values(string argument, string expected)
    {
        var context = CreateContext("set Name " + argument, Array.Empty<IUiElement>());
        Assert.That(context.ResolveValue(1), Is.EqualTo(expected));
    }

    [Test]
    public void Target_and_kind_report_validated_target_state()
    {
        var context = CreateContext("click SaveButton", new IUiElement[] { new FakeUiElement("SaveButton") });
        Assert.That(context.KindOf(), Is.EqualTo(ValueKind.None));
        Assert.That(context.KindOf(1), Is.EqualTo(ValueKind.None));

        var error = Assert.Throws<InvalidOperationException>(() => context.Target<IClickable>())!;
        Assert.That(error.Message, Does.Contain("SaveButton"));
        Assert.That(error.Message, Does.Contain("IClickable"));
        Assert.That(Assert.Throws<InvalidOperationException>(() => context.Target<IUiElement>(1))!.Message, Does.Contain("1"));
    }

    [Test]
    public void KindOf_returns_the_value_kind_recorded_by_validation()
    {
        var catalog = CatalogBuilder.Default();
        catalog.TryGetElement("TestPage", "Account", out var entry);
        var command = new ScriptParser().Parse(new[] { "set Account Name" }).Single();
        var registration = new CommandRegistration("set", typeof(SucceedingTestCommand), typeof(NoArgumentsValidator), Array.Empty<Type>(), null);
        var context = new CommandContext(
            new ExecutionStep(command, registration, new[] { new ResolvedTarget("Account", entry) }),
            new IUiElement[] { new FakeUiElement("Account") },
            new FakeExecutionSession(),
            NullLogger.Instance);

        Assert.That(context.KindOf(), Is.EqualTo(ValueKind.Lookup));
    }

    private static CommandContext CreateContext(string line, IReadOnlyList<IUiElement> elements)
    {
        var command = new ScriptParser().Parse(new[] { line }).Single();
        var registration = new CommandRegistration(command.Keyword, typeof(SucceedingTestCommand), typeof(NoArgumentsValidator), Array.Empty<Type>(), null);
        var step = new ExecutionStep(command, registration, Array.Empty<ResolvedTarget>());
        return new CommandContext(step, elements, new FakeExecutionSession
        {
            StaticValues = new Dictionary<string, string> { ["DefaultName"] = "Contoso" }
        }, NullLogger.Instance);
    }
}