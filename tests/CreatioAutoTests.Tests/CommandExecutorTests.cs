using CreatioAutoTests.Commands;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Runner;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class CommandExecutorTests
{
    [SetUp]
    public void SetUp()
    {
        SucceedingTestCommand.Calls = 0;
        FailedTestCommand.Calls = 0;
    }

    [Test]
    public async Task Failures_stop_or_continue_as_configured_and_are_recorded()
    {
        var stop = await Execute(false, typeof(FailedTestCommand), typeof(SucceedingTestCommand));
        Assert.That(stop.Outcomes, Has.Count.EqualTo(1));
        Assert.That(SucceedingTestCommand.Calls, Is.EqualTo(0));
        Assert.That(stop.Success, Is.False);
        Assert.That(stop.FailureSummary, Does.Contain("expected command failure"));

        SucceedingTestCommand.Calls = 0;
        var keepGoing = await Execute(true, typeof(FailedTestCommand), typeof(SucceedingTestCommand));
        Assert.That(keepGoing.Outcomes, Has.Count.EqualTo(2));
        Assert.That(keepGoing.Outcomes.Count(outcome => !outcome.Success), Is.EqualTo(1));
        Assert.That(SucceedingTestCommand.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Generic_failures_and_invalid_plans_have_the_expected_results()
    {
        var generic = await Execute(false, typeof(BrokenTestCommand));
        Assert.That(generic.Outcomes[0].Message, Does.Contain("InvalidOperationException"));

        var executor = CreateExecutor();
        var invalid = ExecutionPlan.Invalid("case", "case.act", new[] { new ValidationIssue(1, "bad", "bad input") });
        var result = await executor.ExecuteAsync(invalid, new FakeExecutionSession());
        Assert.That(result.Outcomes, Is.Empty);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureSummary, Is.EqualTo("1 validation error(s)"));

        var passed = new TestCaseResult { Name = "case", FilePath = "case.act" };
        Assert.That(passed.Success, Is.True);
        Assert.That(passed.FailureSummary, Is.Null);
        var runError = new TestCaseResult { Name = "broken", FilePath = "broken.act", RunError = "runner broke" };
        Assert.That(runError.Success, Is.False);
        Assert.That(runError.FailureSummary, Is.EqualTo("runner broke"));
        var analysisFailure = new TestCaseResult { Name = "analysis", FilePath = "analysis.act", RunError = "failed" };
        Assert.That(new RunResult { Cases = new[] { passed }, AnalysisScripts = new[] { analysisFailure } }.Success, Is.False);
    }

    private static async Task<TestCaseResult> Execute(bool continueOnFailure, params Type[] commandTypes)
    {
        var plan = ExecutionPlan.Valid("case", "case.act", commandTypes.Select((type, index) => Step(type, index + 1)).ToArray());
        return await CreateExecutor().ExecuteAsync(plan, new FakeExecutionSession
        {
            Settings = new Configuration.RunSettings { ContinueOnFailure = continueOnFailure }
        });
    }

    private static CommandExecutor CreateExecutor() =>
        new(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);

    private static ExecutionStep Step(Type type, int line) => new(
        new ParsedCommand(line, "test", "test", Array.Empty<CommandToken>()),
        new CommandRegistration("test" + line, type, typeof(NoArgumentsValidator), Array.Empty<Type>(), null),
        Array.Empty<ResolvedTarget>());
}