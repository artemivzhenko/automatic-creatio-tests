using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ValidatorTests
{
    private readonly ScriptParser _parser = new();

    [TestCase("wait 5 second", 5_000)]
    [TestCase("wait 2 minute", 120_000)]
    public void Valid_wait_durations_compute_milliseconds(string line, int expectedMilliseconds)
    {
        var command = Parse(line);
        var result = new DurationValidator().Validate(command, CreateContext());

        Assert.That(result.IsValid, Is.True, result.Error);
        Assert.That(DurationValidator.ToMilliseconds(command), Is.EqualTo(expectedMilliseconds));
    }

    [TestCase("wait 0 second")]
    [TestCase("wait -1 second")]
    [TestCase("wait five second")]
    [TestCase("wait 5 sec")]
    [TestCase("wait 5 seconds")]
    [TestCase("wait 5")]
    [TestCase("wait")]
    public void Invalid_wait_durations_explain_the_expected_syntax(string line)
    {
        var result = new DurationValidator().Validate(Parse(line), CreateContext());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("expect"));
    }

    [Test]
    public void End_accepts_no_arguments()
    {
        var result = new NoArgumentsValidator().Validate(Parse("end"), CreateContext());

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void End_rejects_arguments()
    {
        var result = new NoArgumentsValidator().Validate(Parse("end now"), CreateContext());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("end"));
    }

    [Test]
    public void Clear_on_a_button_is_rejected_by_its_capability()
    {
        var plan = Validate("use tester", "open TestPage", "clear SaveButton");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("IClearable"));
    }

    [Test]
    public void Goto_on_a_text_field_is_rejected_by_its_capability()
    {
        var plan = Validate("use tester", "open TestPage", "goto Name");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("INavigableToRecord"));
    }

    [Test]
    public void Goto_on_a_lookup_validates()
    {
        var plan = Validate("use tester", "open TestPage", "goto Account");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void All_dsl_keywords_are_discovered()
    {
        var keywords = CommandRegistry.Discover().Keywords;

        Assert.That(keywords, Is.EquivalentTo(new[]
        {
            "use", "open", "check", "check-value", "set", "click", "goto", "clear", "wait", "create", "end",
            "analyse", "add-record", "start", "check-result", "check-record", "check-row", "select-row", "delete-row", "on",
            "insert", "update", "delete", "delete-record", "remember", "remember-record", "check-count", "check-distinct"
        }));
    }

    private ParsedCommand Parse(string line) => _parser.Parse(new[] { line }).Single();

    private static ValidationContext CreateContext() =>
        new(CatalogBuilder.Default(), new Dictionary<string, string>(), new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

    private static ExecutionPlan Validate(params string[] lines)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        var validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
        return validator.Validate("case", "case.act", new ScriptParser().Parse(lines), CreateContext());
    }
}