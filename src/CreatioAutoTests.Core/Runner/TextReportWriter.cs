using System.Text;
using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Runner;

/// <summary>Writes a plain text report next to the run and logs a one-line summary per case.</summary>
public sealed class TextReportWriter : IReportWriter
{
    private readonly RunSettings _settings;
    private readonly ILogger<TextReportWriter> _logger;

    public TextReportWriter(RunSettings settings, ILogger<TextReportWriter> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task WriteAsync(RunResult result, CancellationToken cancellationToken = default)
    {
        var report = Render(result);

        Directory.CreateDirectory(_settings.ReportsDirectory);
        var path = Path.Combine(
            _settings.ReportsDirectory,
            "run-" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".txt");

        await File.WriteAllTextAsync(path, report, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "{Passed} passed, {Failed} failed in {Duration:0.0}s. Report: {Path}",
            result.Passed,
            result.Failed,
            result.Duration.TotalSeconds,
            path);
    }

    /// <summary>Also used by the CLI to print the same summary to the console.</summary>
    public static string Render(RunResult result)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Creatio automated tests");
        builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Cases: " + result.Cases.Count + "   passed: " + result.Passed +
                           "   failed: " + result.Failed);
        builder.AppendLine("Duration: " + result.Duration.TotalSeconds.ToString("0.00") + "s");
        builder.AppendLine();

        if (result.AnalysisScripts.Count > 0)
        {
            builder.AppendLine("Analysis phase:");
            foreach (var script in result.AnalysisScripts)
            {
                builder.AppendLine("  " + (script.Success ? "ok   " : "FAIL ") + script.Name +
                                   "   (" + script.Duration.TotalSeconds.ToString("0.00") + "s)");

                if (script.FailureSummary is not null)
                {
                    builder.AppendLine("       " + script.FailureSummary);
                }
            }

            builder.AppendLine();
        }

        foreach (var testCase in result.Cases)
        {
            builder.AppendLine((testCase.Success ? "PASS  " : "FAIL  ") + testCase.Name +
                               "   (" + testCase.Duration.TotalSeconds.ToString("0.00") + "s)");

            if (!testCase.IsValid)
            {
                builder.AppendLine("      the case did not run: it does not validate");
                foreach (var issue in testCase.ValidationIssues)
                {
                    builder.AppendLine("      line " + issue.LineNumber + ": " + issue.Message);
                    builder.AppendLine("        " + issue.RawText);
                }

                builder.AppendLine();
                continue;
            }

            if (testCase.RunError is not null)
            {
                builder.AppendLine("      run error: " + testCase.RunError);
            }

            foreach (var failure in testCase.Failures)
            {
                builder.AppendLine("      line " + failure.LineNumber + ": " + failure.Message);
                builder.AppendLine("        " + failure.RawText);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
