using System.Net;
using System.Text;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Runner;
using CreatioAutoTests.Session;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreatioAutoTests.Tests;

/// <summary>Cases that write: insert / update / delete over OData, variables, counts, '# writes', console, evidence.</summary>
[TestFixture]
public sealed class WritingCaseTests
{
    private static ExecutionPlan Validate(params string[] lines)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();
        var validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
        var context = new ValidationContext(
            CatalogBuilder.Default(),
            new Dictionary<string, string> { ["BigAmount"] = "500" },
            new[] { "Supervisor" },
            new Dictionary<string, ObjectDefinition>())
        {
            Directives = ScriptParser.Directives(lines),
        };

        return validator.Validate("case", "case.act", new ScriptParser().Parse(lines), context);
    }

    private static Task<TestCaseResult> Run(FakeExecutionSession session, params string[] lines)
    {
        var plan = Validate(lines);
        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
        return new CommandExecutor(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance).ExecuteAsync(plan, session);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string column, params string[] values) =>
        values.Select(v => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [column] = v }).ToArray();

    [Test]
    public void Variables_substitute_and_compute_integers()
    {
        var values = new Dictionary<string, string> { ["n"] = "41", ["Order"] = "abc" };
        Assert.Multiple(() =>
        {
            Assert.That(CaseVariables.Substitute("Id=$Order;Count=$n+1", values), Is.EqualTo("Id=abc;Count=42"));
            Assert.That(CaseVariables.Substitute("$n-1", values), Is.EqualTo("40"));
            Assert.That(CaseVariables.Substitute("no variables", values), Is.EqualTo("no variables"));
            Assert.That(() => CaseVariables.Substitute("$missing", values), Throws.TypeOf<CommandFailedException>().With.Message.Contains("has no value"));
            Assert.That(() => CaseVariables.Substitute("$Order+1", values), Throws.TypeOf<CommandFailedException>().With.Message.Contains("not an integer"));
        });
    }

    [Test]
    public void Directives_are_the_header_comments_before_the_first_command()
    {
        var directives = ScriptParser.Directives(new[] { "", "# writes", "# ac: AC-02", "# Saves two orders at once", "use Supervisor", "# late: x" });
        Assert.That(directives.Keys, Is.EquivalentTo(new[] { "writes", "ac" }));
        Assert.That(directives["ac"], Is.EqualTo("AC-02"));
        Assert.That(directives["writes"], Is.Empty);
    }

    [Test]
    public void Validation_checks_names_writes_and_shapes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Validate("use Supervisor", "check-record UsrOrder Id=$Order exists").DescribeIssues(),
                Does.Contain("'$Order' is used before a line defines it"));
            Assert.That(Validate("use Supervisor", "insert UsrOrder as Order set UsrName=Bed", "check-record UsrOrder Id=$Order exists").IsValid, Is.True);

            var writes = Validate("# writes", "use Supervisor", "insert UsrOrder as Order set UsrName=Bed");
            Assert.That(writes.DescribeIssues(), Does.Contain("'# writes'").And.Contain("'Order'").And.Contain("line 3"));
            var deleted = Validate("# writes", "# ac: AC-02", "use Supervisor", "insert UsrOrder as Order set UsrName=Bed", "delete UsrOrder $Order",
                "insert UsrOrder as Kept keep set UsrName=Bed", "insert UsrOrder as Other set UsrName=Bed", "delete-record UsrOrder Id=$Other");
            Assert.That(deleted.IsValid, Is.True, deleted.DescribeIssues());
            Assert.That(deleted.StopOnFailure, Is.True);
            Assert.That(deleted.AcceptanceCriterion, Is.EqualTo("AC-02"));
            Assert.That(Validate("use Supervisor", "insert UsrOrder as Order set UsrName=Bed").StopOnFailure, Is.False, "automatic cleanup without the directive");

            Assert.That(Validate("use Supervisor", "insert UsrOrder as Order set UsrName=Bed", "check-count UsrOrder Id=$Order eq $Order+1").DescribeIssues(),
                Does.Contain("only allowed as the value 'check-value' compares with"));
            Assert.That(Validate("use Supervisor", "open TestPage", "remember Name as $n", "check-value Name eq $n+1").IsValid, Is.True);
            Assert.That(Validate("use Supervisor", "open TestPage", "remember SaveButton as $n").DescribeIssues(), Does.Contain("no value to remember"));
            Assert.That(Validate("use Supervisor", "insert UsrOrder Order set UsrName=Bed").DescribeIssues(), Does.Contain("'insert' reads"));
            Assert.That(Validate("use Supervisor", "update UsrOrder Order set UsrName=Bed").DescribeIssues(), Does.Contain("'$alias' or a GUID"));
            Assert.That(Validate("use Supervisor", "check-count UsrOrder UsrName=Bed about 3").DescribeIssues(), Does.Contain("eq, ne, gt"));
            Assert.That(Validate("use Supervisor", "check-distinct UsrOrder UsrNumber UsrName=Bed").DescribeIssues(), Does.Contain("where"));
            Assert.That(Validate("use Supervisor", "remember-record UsrOrder UsrName=Bed as $o", "open TestPage for $o").IsValid, Is.True);
            Assert.That(Validate("use Supervisor", "check-record UsrOrder UsrName=Bed has UsrAmount like 3").DescribeIssues(), Does.Contain("compares with"));
            Assert.That(Validate("use Supervisor", "check console no errors except \"([\"").DescribeIssues(), Does.Contain("does not compile"));
            Assert.That(Validate("use Supervisor", "check console no errors except \"favicon\"").IsValid, Is.True);
            Assert.That(Validate("use Supervisor", "check console errors").DescribeIssues(), Does.Contain("'check console' reads"));
        });
    }

    [Test]
    public async Task Inserted_records_are_substituted_and_removed_at_the_end()
    {
        var session = new FakeExecutionSession
        {
            StaticValues = new Dictionary<string, string> { ["BigAmount"] = "500" },
            OnRecordExists = (_, filter) => filter == "UsrOrderId eq 00000001-0000-0000-0000-000000000000",
        };
        var result = await Run(session,
            "use Supervisor",
            "insert UsrOrder as Order set UsrName=Bed;UsrAmount=BigAmount",
            "insert UsrOrder as Kept keep set UsrName=Kept",
            "check-record UsrOrderLog UsrOrder=$Order exists",
            "update UsrOrder $Order set UsrAmount=5000",
            "insert UsrOrder as Gone set UsrName=Gone",
            "delete UsrOrder $Gone");

        Assert.That(result.Success, Is.True, result.FailureSummary);
        Assert.That(session.Writes[0], Does.StartWith("insert UsrOrder 00000001").And.EndWith("UsrName=Bed;UsrAmount=500"), "a static value is written as its value");
        Assert.That(session.Writes, Has.Some.EqualTo("update UsrOrder " + session.Variables["Order"] + " UsrAmount=5000"));
        Assert.That(session.Writes[^1], Is.EqualTo("delete UsrOrder " + session.Variables["Order"]), "cleanup deletes what is left, the kept one stays");
        Assert.That(session.Writes.Count(w => w.StartsWith("delete", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(session.Cleanups, Is.EqualTo(1));
    }

    [Test]
    public async Task A_writing_case_stops_at_its_first_failure_and_still_cleans_up()
    {
        var settings = new RunSettings { ContinueOnFailure = true };
        var lines = new[] { "use Supervisor", "insert UsrOrder as Order set UsrName=Bed", "check-record UsrOrder Id=$Order exists", "update UsrOrder $Order set UsrAmount=1", "delete UsrOrder $Order" };

        var plain = new FakeExecutionSession { Settings = settings };
        var going = await Run(plain, lines);
        Assert.That(going.Outcomes, Has.Count.EqualTo(5), "continueOnFailure goes on");

        var writing = new FakeExecutionSession { Settings = settings };
        var stopped = await Run(writing, new[] { "# writes" }.Concat(lines).ToArray());
        Assert.That(stopped.Outcomes, Has.Count.EqualTo(3), "'# writes' stops at the failed check");
        Assert.That(writing.Writes, Has.None.StartsWith("update"));
        Assert.That(writing.Writes[^1], Does.StartWith("delete UsrOrder"), "the insert is cleaned up after the stop");

        var refusing = new FakeExecutionSession { OnDelete = _ => "403 denied" };
        var leaked = await Run(refusing, "use Supervisor", "insert UsrOrder as Order set UsrName=Bed");
        Assert.That(leaked.Success, Is.False);
        Assert.That(leaked.RunError, Does.Contain("cleanup left 1 record(s)").And.Contain("403 denied"));
    }

    [Test]
    public async Task Record_reads_count_compare_and_refuse_wide_deletes()
    {
        var many = Enumerable.Range(1, 21).Select(i => new Guid(i, 0, 0, new byte[8]).ToString()).ToArray();
        var session = new FakeExecutionSession
        {
            OnQuery = (entity, select, filter, top) => (entity, select[0]) switch
            {
                ("UsrOrder", "UsrStatusId") => Rows("UsrStatusId", "0c48c818-08f0-4f70-9b58-be512227f3bf"),
                ("UsrOrder", "UsrAmount") => Rows("UsrAmount", "500.00"),
                ("UsrOrder", "UsrNumber") => Rows("UsrNumber", "ORD-1", "ORD-2", "ORD-1"),
                ("UsrOne", "Id") => Rows("Id", many[0]),
                ("UsrTwo", "Id") => Rows("Id", many[0], many[1]),
                ("UsrWide", "Id") => Rows("Id", many.Take(top).ToArray()),
                _ => Rows("Id"),
            },
            OnCount = (entity, filter) => entity == "UsrWide" ? 37 : 2,
        };

        var passed = await Run(session,
            "use Supervisor",
            "check-record UsrOrder UsrName=Bed has UsrStatus eq 0c48c818-08f0-4f70-9b58-be512227f3bf",
            "check-record UsrOrder UsrName=Bed has UsrAmount ge 500",
            "remember-record UsrOne UsrName=Bed as $one",
            "check-count UsrOrder UsrName=Bed eq 2",
            "delete-record UsrTwo UsrName=Bed");
        Assert.That(passed.Success, Is.True, passed.FailureSummary);
        Assert.That(session.Variables["one"], Is.EqualTo(many[0]));
        Assert.That(session.Writes, Is.EqualTo(new[] { "delete UsrTwo " + many[0], "delete UsrTwo " + many[1] }));

        async Task<string?> Fails(string line) => (await Run(session, "use Supervisor", line)).FailureSummary;
        Assert.That(await Fails("check-record UsrOrder UsrName=Bed has UsrAmount gt 500"), Does.Contain("UsrAmount = \"500.00\", which is not gt"));
        Assert.That(await Fails("check-record UsrNone UsrName=Bed has UsrAmount eq 1"), Does.Contain("no such record"));
        Assert.That(await Fails("remember-record UsrTwo UsrName=Bed as $x"), Does.Contain("more than one record"));
        Assert.That(await Fails("remember-record UsrNone UsrName=Bed as $x"), Does.Contain("no such record"));
        Assert.That(await Fails("check-count UsrOrder UsrName=Bed gt 2"), Does.Contain("has 2 record(s), which is not gt 2"));
        Assert.That(await Fails("check-distinct UsrOrder UsrNumber where UsrName=Bed"), Does.Contain("\"ORD-1\" x2"));
        Assert.That(await Fails("delete-record UsrWide UsrName=Bed"), Does.Contain("matches 37 records").And.Contain("at most 20"));
        Assert.That(await Fails("delete-record UsrNone UsrName=Bed"), Does.Contain("no record to delete"));
    }

    [Test]
    public async Task Console_errors_are_checked_and_reported_when_captured()
    {
        var session = new FakeExecutionSession { Settings = new RunSettings { CaptureConsole = true, ContinueOnFailure = true } };
        session.Console.Add("GET /favicon.ico 404");
        var ok = await Run(session, "use Supervisor", "check console no errors except \"favicon\"");
        Assert.That(ok.Success, Is.True, ok.FailureSummary);
        Assert.That(ok.ConsoleErrors, Is.EqualTo(new[] { "GET /favicon.ico 404" }));

        session.Console.Add("TypeError: x is undefined");
        var failed = await Run(session, "use Supervisor", "check console no errors except \"favicon\"");
        Assert.That(failed.FailureSummary, Does.Contain("1 console error(s): TypeError"));
    }

    [Test]
    public async Task The_page_probe_sees_one_element_or_runs_out_of_budget()
    {
        Assert.That(await PageProbe.AnyPresentAsync(Array.Empty<IExistable>(), 10, default), Is.True);
        Assert.That(await PageProbe.AnyPresentAsync(new IExistable[] { new Probe(false), new Probe(true) }, 10, default), Is.True);
        var absent = new Probe(false);
        Assert.That(await PageProbe.AnyPresentAsync(new IExistable[] { absent }, 10, default), Is.False);
        Assert.That(absent.Asked, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task The_OData_client_types_bodies_echoes_csrf_and_falls_back_on_count()
    {
        var stub = new StubHandler();
        using var client = new CreatioODataClient(
            new EnvironmentConfig { BaseUrl = "https://dev.example", Users = { new UserCredentials { Username = "Supervisor", Password = "x" } } },
            new SiteConfig(), NullLogger<CreatioODataClient>.Instance, stub);

        var id = await client.InsertAsync("UsrOrder", ODataValues.Body(new[]
        {
            new RecordAssignment("UsrName", "Bed"), new RecordAssignment("UsrAmount", "500"), new RecordAssignment("UsrIsBig", "true"),
            new RecordAssignment("UsrStatus", "0c48c818-08f0-4f70-9b58-be512227f3bf"),
        }));
        await client.UpdateAsync("UsrOrder", id, ODataValues.Body(new[] { new RecordAssignment("UsrAmount", "5000") }));
        var count = await client.CountAsync("UsrOrderLog", "UsrOrderId eq " + id);
        var refused = Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteAsync("UsrLocked", id));

        Assert.That(id, Is.EqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        Assert.That(count, Is.EqualTo(3));
        Assert.That(refused!.Message, Does.Contain("403").And.Contain("denied"));
        var insert = stub.Requests.Single(r => r.Method == "POST" && r.Path == "/0/odata/UsrOrder");
        Assert.That(insert.Body, Is.EqualTo("{\"UsrName\":\"Bed\",\"UsrAmount\":500,\"UsrIsBig\":true,\"UsrStatusId\":\"0c48c818-08f0-4f70-9b58-be512227f3bf\"}"));
        Assert.That(insert.Csrf, Is.EqualTo("csrf-1"));
        Assert.That(insert.ForceUseSession, Is.EqualTo("true"));
        Assert.That(stub.Requests.Single(r => r.Method == "PATCH").Path, Is.EqualTo("/0/odata/UsrOrder(" + id + ")"));
        Assert.That(stub.Requests.Where(r => r.Path.Contains("count", StringComparison.Ordinal)).Select(r => r.Path.Split('?')[0]),
            Is.EqualTo(new[] { "/0/odata/UsrOrderLog/$count", "/0/odata/UsrOrderLog" }));
    }

    [Test]
    public async Task Evidence_files_are_numbered_per_criterion()
    {
        var folder = Path.Combine(Path.GetTempPath(), "evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plan = Validate("# ac: AC-02", "use Supervisor", "check-record UsrOrder UsrName=Bed exists");
            var failed = await new CommandExecutor(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
                .ExecuteAsync(plan, new FakeExecutionSession());
            var named = new TestCaseResult { Name = "order saves", FilePath = "order saves.act" };
            var run = new RunResult { Cases = new[] { failed, named } };

            await new EvidenceWriter(folder, "T-7").WriteAsync(run);
            await new EvidenceWriter(folder, "T-7").WriteAsync(new RunResult { Cases = new[] { failed } });

            var files = Directory.GetFiles(Path.Combine(folder, "evidence")).Select(Path.GetFileName).OrderBy(f => f).ToArray();
            Assert.That(files, Is.EqualTo(new[] { "AC-02-report-1.table.md", "AC-02-report-1.txt", "AC-02-report-2.table.md", "AC-02-report-2.txt",
                "order-saves-report-1.table.md", "order-saves-report-1.txt" }));
            var log = await File.ReadAllTextAsync(Path.Combine(folder, "evidence", "AC-02-report-1.txt"));
            Assert.That(log, Does.StartWith("task: T-7").And.Contain("FAIL line 3: check-record").And.Contain("failed steps:"));
            var table = await File.ReadAllTextAsync(Path.Combine(folder, "evidence", "AC-02-report-1.table.md"));
            Assert.That(table, Does.StartWith(EvidenceWriter.TableHeader).And.Contain("| AC-02 | fail | `evidence/AC-02-report-1.txt`: line 3:"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    private sealed class Probe : IExistable
    {
        private readonly bool _present;
        public Probe(bool present) => _present = present;
        public int Asked { get; private set; }
        public string Code => "probe";
        public string Label => "probe";
        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => ExistsAsync(0, cancellationToken);
        public Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(_present);
        }
    }

    private sealed record Seen(string Method, string Path, string? Body, string? Csrf, string? ForceUseSession);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add(new Seen(request.Method.Method, Uri.UnescapeDataString(path),
                body, request.Headers.TryGetValues("BPMCSRF", out var c) ? c.Single() : null,
                request.Headers.TryGetValues("ForceUseSession", out var f) ? f.Single() : null));

            if (path.Contains("AuthService", StringComparison.Ordinal))
            {
                var login = Json("{\"Code\":0}");
                login.Headers.Add("Set-Cookie", "BPMCSRF=csrf-1; path=/");
                return login;
            }

            return (request.Method.Method, path) switch
            {
                ("POST", _) => Json("{\"Id\":\"11111111-2222-3333-4444-555555555555\"}", HttpStatusCode.Created),
                ("GET", var p) when p.Contains("/$count", StringComparison.Ordinal) => Json("not supported", HttpStatusCode.BadRequest),
                ("GET", _) => Json("{\"@odata.count\":3,\"value\":[]}"),
                ("DELETE", var p) when p.Contains("UsrLocked", StringComparison.Ordinal) => Json("{\"error\":\"denied\"}", HttpStatusCode.Forbidden),
                _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            };
        }

        private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
