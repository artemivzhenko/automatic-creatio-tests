using System.Xml.Linq;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Runner;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ReportWriterTests
{
    private string _reports = null!;

    [SetUp]
    public void SetUp()
    {
        _reports = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_reports)) Directory.Delete(_reports, recursive: true);
    }

    [Test]
    public async Task JUnit_reports_cases_validation_failures_and_analysis_separately()
    {
        var result = Result();
        await new JUnitReportWriter(Settings()).WriteAsync(result);

        var document = XDocument.Load(Directory.GetFiles(_reports, "*.xml").Single());
        Assert.That(document.Descendants("testsuite").Select(suite => suite.Attribute("name")!.Value), Is.EquivalentTo(new[] { "cases", "analysis" }));
        Assert.That(document.Descendants("testcase").Count(), Is.EqualTo(4));
        Assert.That(document.Descendants("failure").Count(), Is.EqualTo(2));
        Assert.That(document.Descendants("failure").Any(failure => failure.Attribute("type")?.Value == "validation"), Is.True);
        Assert.That(document.Descendants("testcase").First().Attribute("name")!.Value, Is.EqualTo("pass < & \"case\""));
    }

    [Test]
    public async Task Html_encodes_case_content_and_renders_analysis_before_cases()
    {
        var result = Result();
        await new HtmlReportWriter(Settings()).WriteAsync(result);

        var html = await File.ReadAllTextAsync(Directory.GetFiles(_reports, "*.html").Single());
        Assert.That(html, Does.Contain("pass &lt; &amp; &quot;case&quot;"));
        Assert.That(html.IndexOf("<h2>Analysis</h2>", StringComparison.Ordinal), Is.LessThan(html.IndexOf("<h2>Cases</h2>", StringComparison.Ordinal)));
        Assert.That(XDocument.Parse(html).Descendants("details").Count(), Is.EqualTo(4));
    }

    [Test]
    public async Task Selecting_JUnit_and_HTML_writes_exactly_two_non_text_artifacts()
    {
        var settings = Settings();
        settings.Reports = new List<string> { "junit", "html" };
        var writer = new CompositeReportWriter(new IReportWriter[] { new JUnitReportWriter(settings), new HtmlReportWriter(settings) });

        await writer.WriteAsync(Result());

        Assert.That(Directory.GetFiles(_reports), Has.Length.EqualTo(2));
        Assert.That(Directory.GetFiles(_reports, "*.txt"), Is.Empty);
    }

    private RunSettings Settings() => new() { ReportsDirectory = _reports };

    private static RunResult Result()
    {
        var failed = new TestCaseResult { Name = "failed", FilePath = "failed.act", Duration = TimeSpan.FromSeconds(2) };
        failed.Outcomes.Add(new CommandOutcome(Step(), false, "wrong <value>", TimeSpan.FromMilliseconds(5)));
        var invalid = new TestCaseResult
        {
            Name = "invalid",
            FilePath = "invalid.act",
            ValidationIssues = new[] { new ValidationIssue(4, "set X <bad>", "not valid") }
        };
        return new RunResult
        {
            Cases = new[]
            {
                new TestCaseResult { Name = "pass < & \"case\"", FilePath = "pass.act", Duration = TimeSpan.FromSeconds(1) },
                failed,
                invalid
            },
            AnalysisScripts = new[] { new TestCaseResult { Name = "analysis", FilePath = "analysis.act" } },
            Duration = TimeSpan.FromSeconds(3)
        };
    }

    private static ExecutionStep Step() => new(
        new ParsedCommand(7, "set X <bad>", "set", Array.Empty<CommandToken>()),
        new CommandRegistration("set", typeof(ReportWriterTests), typeof(NoArgumentsValidator), Array.Empty<Type>(), null),
        Array.Empty<Validation.ResolvedTarget>());
}