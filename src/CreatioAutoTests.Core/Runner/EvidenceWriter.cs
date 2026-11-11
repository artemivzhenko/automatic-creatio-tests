using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>
/// Writes the tester's evidence into a task folder of the author/tester loop: per case
/// <c>evidence/AC-nn-report-N.txt</c> (every line with its outcome, the failed steps, console errors) and
/// <c>AC-nn-report-N.table.md</c>, a draft row of the report table. A case names its file by its
/// <c># ac:</c> directive, else by its file name; N is the next number free for that name.
/// </summary>
public sealed class EvidenceWriter : IReportWriter
{
    /// <summary>The header of the tester's report table (skill/creatio-loop/templates/report.md).</summary>
    public const string TableHeader = "| AC | Result | Evidence |";

    private readonly string _taskFolder;
    private readonly string? _taskId;

    /// <summary>Writes into <c>&lt;taskFolder&gt;/evidence</c>.</summary>
    /// <param name="taskFolder">The task folder.</param>
    /// <param name="taskId">The task's id, written at the top of each file; optional.</param>
    public EvidenceWriter(string taskFolder, string? taskId)
    {
        _taskFolder = taskFolder ?? throw new ArgumentNullException(nameof(taskFolder));
        _taskId = taskId;
    }

    /// <summary>The files written by the last call, for the CLI to print.</summary>
    public List<string> Written { get; } = new();

    /// <inheritdoc />
    public async Task WriteAsync(RunResult result, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_taskFolder, "evidence");
        Directory.CreateDirectory(folder);

        foreach (var testCase in result.Cases)
        {
            var stem = Regex.Replace(testCase.AcceptanceCriterion ?? testCase.Name, @"[^\w.-]+", "-");
            var n = NextFree(folder, stem);
            var log = Path.Combine(folder, stem + "-report-" + n.ToString(CultureInfo.InvariantCulture) + ".txt");

            await File.WriteAllTextAsync(log, Log(testCase), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.ChangeExtension(log, ".table.md"), Table(testCase, stem, Path.GetFileName(log)), cancellationToken)
                .ConfigureAwait(false);
            Written.Add(log);
        }
    }

    /// <summary>1 + the highest N of <c>&lt;stem&gt;-report-N.txt</c> already in the folder.</summary>
    internal static int NextFree(string folder, string stem)
    {
        var pattern = new Regex("^" + Regex.Escape(stem) + @"-report-(\d+)\.txt$", RegexOptions.IgnoreCase);
        return 1 + Directory.EnumerateFiles(folder)
            .Select(f => pattern.Match(Path.GetFileName(f)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
    }

    private string Log(TestCaseResult testCase)
    {
        var text = new StringBuilder();
        if (_taskId is not null)
        {
            text.AppendLine("task: " + _taskId);
        }

        text.AppendLine("case: " + testCase.Name + " (" + testCase.FilePath + ")");
        text.AppendLine("result: " + (testCase.Success ? "PASS" : "FAIL") + ", " +
                        testCase.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
        text.AppendLine();

        foreach (var issue in testCase.ValidationIssues)
        {
            text.AppendLine("invalid " + issue);
        }

        foreach (var outcome in testCase.Outcomes)
        {
            text.AppendLine(outcome.ToString());
        }

        if (testCase.RunError is not null)
        {
            text.AppendLine("run error: " + testCase.RunError);
        }

        var failed = testCase.Failures.ToArray();
        if (failed.Length > 0)
        {
            text.AppendLine().AppendLine("failed steps:");
            foreach (var failure in failed)
            {
                text.AppendLine("  line " + failure.LineNumber + ": " + failure.RawText + " -- " + failure.Message);
            }
        }

        if (testCase.ConsoleErrors.Count > 0)
        {
            text.AppendLine().AppendLine("console errors:");
            foreach (var error in testCase.ConsoleErrors)
            {
                text.AppendLine("  " + error);
            }
        }

        return text.ToString();
    }

    private static string Table(TestCaseResult testCase, string stem, string logName) =>
        TableHeader + Environment.NewLine + "|---|---|---|" + Environment.NewLine +
        "| " + stem + " | " + (testCase.Success ? "pass" : "fail") + " | `evidence/" + logName + "`" +
        (testCase.FailureSummary is { } why ? ": " + why.Replace("|", "\\|") : string.Empty) + " |" + Environment.NewLine;
}
