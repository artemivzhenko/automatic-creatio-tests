using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Session;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreatioAutoTests.Tests;

/// <summary>
/// The backend tier: 'start', 'check-result' and 'check-record' are validated against the
/// processes directory without an environment, and executed against a fake session.
/// </summary>
[TestFixture]
public sealed class ProcessCaseTests
{
    private static readonly ProcessDefinition Order = new()
    {
        Name = "UsrOrder_Process",
        Parameters =
        {
            new ProcessParameter { Code = "Amount", Type = "Money", Direction = "in" },
            new ProcessParameter { Code = "Contact", Type = "Lookup", Direction = "in" },
            new ProcessParameter { Code = "Verdict", Type = "Text", Direction = "out" },
            new ProcessParameter { Code = "Total", Type = "Integer", Direction = "variable" },
        },
    };

    private static ValidationContext Context(params ProcessDefinition[] processes) => new(
        CatalogBuilder.Default(),
        new Dictionary<string, string> { ["BigAmount"] = "500" },
        new[] { "Supervisor" },
        new Dictionary<string, ObjectDefinition>())
    {
        Processes = processes.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase),
    };

    private static ParsedCommand Line(string text)
    {
        return new ScriptParser().Parse(new[] { text }).Single();
    }

    [Test]
    public void Start_is_checked_against_the_process_and_its_input_parameters()
    {
        var validator = new ProcessStartValidator(new ValueCompatibilityPolicy());
        var context = Context(Order);

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(Line("start UsrOrder_Process"), context).NextProcess, Is.EqualTo("UsrOrder_Process"));
            Assert.That(validator.Validate(Line("start UsrOrder_Process with Amount=10;Total=3"), context).IsValid, Is.True);
            Assert.That(validator.Validate(Line("start UsrOrder_Process with Amount=BigAmount"), context).IsValid, Is.True, "a static value");
            Assert.That(validator.Validate(Line("start Nope"), context).Error, Does.Contain("not configured").And.Contain("UsrOrder_Process"));
            Assert.That(validator.Validate(Line("start UsrOrder_Process with Verdict=x"), context).Error, Does.Contain("output parameter"));
            Assert.That(validator.Validate(Line("start UsrOrder_Process with Amount=ten"), context).Error, Does.Contain("rejected the value"));
            Assert.That(validator.Validate(Line("start UsrOrder_Process with Nope=1"), context).Error, Does.Contain("no parameter 'Nope'"));
            Assert.That(validator.Validate(Line("start UsrOrder_Process Amount=1"), context).Error, Does.Contain("'with'"));
        });
    }

    [Test]
    public void Check_result_needs_a_started_process_and_an_output_of_the_right_kind()
    {
        var validator = new ResultCheckValidator(new ValueCompatibilityPolicy());
        var context = Context(Order);

        Assert.That(validator.Validate(Line("check-result Verdict eq \"big\""), context).Error, Does.Contain("use 'start"));

        context.CurrentProcess = "UsrOrder_Process";
        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(Line("check-result Verdict eq \"big\""), context).IsValid, Is.True);
            Assert.That(validator.Validate(Line("check-result Total gt \"3\""), context).IsValid, Is.True, "a variable is an output too");
            Assert.That(validator.Validate(Line("check-result Amount eq \"1\""), context).Error, Does.Contain("no output parameter 'Amount'"));
            Assert.That(validator.Validate(Line("check-result Verdict gt \"a\""), context).Error, Does.Contain("does not apply to Text"));
            Assert.That(validator.Validate(Line("check-result Total eq \"many\""), context).Error, Does.Contain("cannot compare"));
            Assert.That(validator.Validate(Line("check-result Verdict eq big"), context).Error, Does.Contain("must be quoted"));
        });
    }

    [Test]
    public void Check_record_reads_its_filter_and_its_expectation()
    {
        var validator = new RecordCheckValidator();
        var context = Context();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(Line("check-record UsrOrder UsrName=Order 1;UsrAmount=10 exists"), context).IsValid, Is.True);
            Assert.That(validator.Validate(Line("check-record UsrOrder UsrName=Order 1 not exists"), context).IsValid, Is.True);
            Assert.That(validator.Validate(Line("check-record UsrOrder UsrName=Order 1"), context).Error, Does.Contain("'exists' or 'not exists'"));
            Assert.That(validator.Validate(Line("check-record UsrOrder exists"), context).Error, Does.Contain("takes an object, a filter"));
            Assert.That(validator.Validate(Line("check-record UsrOrder not exists"), context).Error, Does.Contain("needs a filter"));
            Assert.That(validator.Validate(Line("check-record UsrOrder UsrName exists"), context).Error, Does.Contain("cannot be read"));
        });

        var statics = new Dictionary<string, string> { ["Owner"] = "410006e1-ca4e-4502-a9ec-e54d922d2c00" };
        RecordAssignments.TryParse("UsrName=O'Brien;UsrAmount=10.5;UsrActive=true;UsrContact=Owner;UsrOwnerId=Owner", out var assignments, out _);
        Assert.That(CheckRecordCommand.BuildFilter(assignments, statics), Is.EqualTo(
            "UsrName eq 'O''Brien' and UsrAmount eq 10.5 and UsrActive eq true and " +
            "UsrContactId eq 410006e1-ca4e-4502-a9ec-e54d922d2c00 and UsrOwnerId eq 410006e1-ca4e-4502-a9ec-e54d922d2c00"));
    }

    [Test]
    public void The_engine_response_is_read_into_text_values()
    {
        var ok = ProcessRunResult.Parse("P", "{\"processId\":\"abc\",\"processStatus\":2,\"resultParameterValues\":{\"Verdict\":\"big\",\"Counter\":42,\"Flag\":true,\"Empty\":null},\"success\":true,\"errorInfo\":null}");
        var failed = ProcessRunResult.Parse("P", "{\"processId\":\"00000000-0000-0000-0000-000000000000\",\"resultParameterValues\":null,\"success\":false,\"errorInfo\":{\"errorCode\":\"ItemNotFoundException\",\"message\":\"Item process schema \\\"P\\\" not found.\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(ok.Success, Is.True);
            Assert.That(ok.Results["Verdict"], Is.EqualTo("big"));
            Assert.That(ok.Results["Counter"], Is.EqualTo("42"));
            Assert.That(ok.Results["Flag"], Is.EqualTo("true"));
            Assert.That(ok.Results["Empty"], Is.EqualTo(string.Empty));
            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Error, Does.Contain("not found"));
        });
    }

    [Test]
    public async Task Start_and_check_result_run_against_the_session_without_a_page()
    {
        var session = new FakeExecutionSession
        {
            Processes = new Dictionary<string, ProcessDefinition> { [Order.Name] = Order },
            OnStart = (name, values) => new ProcessRunResult(name, "id", true, null,
                new Dictionary<string, string> { ["Verdict"] = values["Amount"] == "500" ? "big" : "small", ["Total"] = "7" }),
            OnRecordExists = (entity, filter) => filter == "UsrName eq 'Order 1'",
        };
        var registry = CommandRegistry.Discover();

        Assert.That(session.LastProcess, Is.Null);
        await Run("start UsrOrder_Process with Amount=500", session, registry);
        Assert.That(session.LastProcess!.Results["Verdict"], Is.EqualTo("big"));

        await Run("check-result Verdict eq \"big\"", session, registry);
        await Run("check-result Total ge \"5\"", session, registry);
        await Run("check-record UsrOrder UsrName=Order 1 exists", session, registry);
        await Run("check-record UsrOrder UsrName=Order 2 not exists", session, registry);

        var wrong = Assert.ThrowsAsync<CommandFailedException>(() => Run("check-result Verdict eq \"small\"", session, registry));
        Assert.That(wrong!.Message, Does.Contain("\"big\"").And.Contain("small"));

        var missing = Assert.ThrowsAsync<CommandFailedException>(() => Run("check-record UsrOrder UsrName=Order 2 exists", session, registry));
        Assert.That(missing!.Message, Does.Contain("has no record"));

        var failedStart = new FakeExecutionSession
        {
            Processes = session.Processes,
            OnStart = (name, _) => new ProcessRunResult(name, Guid.Empty.ToString(), false, "Publish the process first", new Dictionary<string, string>()),
        };
        var notStarted = Assert.ThrowsAsync<CommandFailedException>(() => Run("start UsrOrder_Process", failedStart, registry));
        Assert.That(notStarted!.Message, Does.Contain("did not start").And.Contain("Publish"));
    }

    private static Task Run(string text, FakeExecutionSession session, CommandRegistry registry)
    {
        var command = Line(text);
        registry.TryResolve(command.Keyword, out var registration);
        var step = new ExecutionStep(command, registration, Array.Empty<ResolvedTarget>());
        var context = new CommandContext(step, Array.Empty<IUiElement>(), session, NullLogger.Instance);
        var instance = (ICommand)Activator.CreateInstance(registration.ImplementationType)!;
        return instance.ExecuteAsync(context);
    }
}
