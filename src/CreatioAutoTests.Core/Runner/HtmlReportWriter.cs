using System.Globalization;
using System.Net;
using System.Text;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>Writes a standalone, encoded HTML report suitable for opening directly from a CI artifact.</summary>
public sealed class HtmlReportWriter : IReportWriter
{
    private readonly RunSettings _settings;

    /// <summary>Uses the configured reports directory for emitted HTML artifacts.</summary>
    public HtmlReportWriter(RunSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public async Task WriteAsync(RunResult result, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.ReportsDirectory);
        var path = Path.Combine(_settings.ReportsDirectory, "report-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture) + ".html");
        await File.WriteAllTextAsync(path, Render(result), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders all data through HTML encoding so case content cannot become markup.</summary>
    public static string Render(RunResult result)
    {
        var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Creatio test report</title><style>body{font-family:system-ui;margin:2rem;color:#1d252c}details{border:1px solid #ccd4d9;margin:.5rem 0;padding:.6rem}summary{cursor:pointer;font-weight:600}.pass{color:#167a3f}.fail{color:#b42318}pre{white-space:pre-wrap;background:#f4f6f7;padding:.75rem}table{border-collapse:collapse;width:100%}td,th{padding:.4rem;border-bottom:1px solid #dce2e5;text-align:left}</style></head><body>");
        html.Append("<h1>Creatio automated tests</h1><p>Generated: ").Append(Encode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append("<br/>Cases: ").Append(result.Cases.Count).Append("; passed: ").Append(result.Passed).Append("; failed: ").Append(result.Failed).Append("<br/>Duration: ").Append(result.Duration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append("s</p>");
        if (result.AnalysisScripts.Count > 0)
        {
            html.Append("<section><h2>Analysis</h2>");
            foreach (var analysis in result.AnalysisScripts) AppendCase(html, analysis);
            html.Append("</section>");
        }
        html.Append("<section><h2>Cases</h2>");
        foreach (var testCase in result.Cases) AppendCase(html, testCase);
        return html.Append("</section></body></html>").ToString();
    }

    private static void AppendCase(StringBuilder html, TestCaseResult testCase)
    {
        html.Append("<details").Append(testCase.Success ? string.Empty : " open=\"open\"").Append("><summary class=\"").Append(testCase.Success ? "pass" : "fail").Append("\">").Append(testCase.Success ? "PASS " : "FAIL ").Append(Encode(testCase.Name)).Append(" (").Append(testCase.Duration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append("s)</summary>");
        if (!testCase.IsValid) html.Append("<pre>").Append(Encode(string.Join(Environment.NewLine, testCase.ValidationIssues))).Append("</pre>");
        if (testCase.RunError is not null) html.Append("<pre>").Append(Encode(testCase.RunError)).Append("</pre>");
        if (testCase.ConsoleErrors.Count > 0) html.Append("<pre>console:").Append(Environment.NewLine).Append(Encode(string.Join(Environment.NewLine, testCase.ConsoleErrors))).Append("</pre>");
        if (testCase.Outcomes.Count > 0)
        {
            html.Append("<table><tr><th>Line</th><th>Command</th><th>Outcome</th><th>Duration</th></tr>");
            foreach (var outcome in testCase.Outcomes)
            {
                html.Append("<tr><td>").Append(outcome.LineNumber).Append("</td><td>").Append(Encode(outcome.RawText)).Append("</td><td>").Append(outcome.Success ? "PASS" : "FAIL").Append("</td><td>").Append(outcome.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append("s</td></tr>");
                if (outcome.Message is not null) html.Append("<tr><td colspan=\"4\"><pre>").Append(Encode(outcome.Message)).Append("</pre></td></tr>");
            }
            html.Append("</table>");
        }
        html.Append("</details>");
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}