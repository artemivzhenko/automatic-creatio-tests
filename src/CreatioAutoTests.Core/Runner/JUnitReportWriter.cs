using System.Globalization;
using System.Xml;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>Writes a CI-compatible JUnit XML report without relying on a particular build server.</summary>
public sealed class JUnitReportWriter : IReportWriter
{
    private readonly RunSettings _settings;

    /// <summary>Uses the configured reports directory for emitted XML artifacts.</summary>
    public JUnitReportWriter(RunSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public async Task WriteAsync(RunResult result, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.ReportsDirectory);
        var path = Path.Combine(_settings.ReportsDirectory, "junit-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture) + ".xml");
        var settings = new XmlWriterSettings { Async = true, Indent = true };

        await using var stream = File.Create(path);
        await using var writer = XmlWriter.Create(stream, settings);
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "testsuites", null).ConfigureAwait(false);
        await WriteSuiteAsync(writer, "cases", result.Cases).ConfigureAwait(false);
        if (result.AnalysisScripts.Count > 0)
        {
            await WriteSuiteAsync(writer, "analysis", result.AnalysisScripts).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndDocumentAsync().ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteSuiteAsync(XmlWriter writer, string name, IReadOnlyList<TestCaseResult> cases)
    {
        await writer.WriteStartElementAsync(null, "testsuite", null).ConfigureAwait(false);
        await writer.WriteAttributeStringAsync(null, "name", null, name).ConfigureAwait(false);
        await writer.WriteAttributeStringAsync(null, "tests", null, cases.Count.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await writer.WriteAttributeStringAsync(null, "failures", null, cases.Count(testCase => !testCase.Success && testCase.RunError is null).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await writer.WriteAttributeStringAsync(null, "errors", null, cases.Count(testCase => testCase.RunError is not null).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

        foreach (var testCase in cases)
        {
            await writer.WriteStartElementAsync(null, "testcase", null).ConfigureAwait(false);
            await writer.WriteAttributeStringAsync(null, "name", null, testCase.Name).ConfigureAwait(false);
            await writer.WriteAttributeStringAsync(null, "time", null, testCase.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (!testCase.IsValid)
            {
                await writer.WriteStartElementAsync(null, "failure", null).ConfigureAwait(false);
                await writer.WriteAttributeStringAsync(null, "type", null, "validation").ConfigureAwait(false);
                await writer.WriteAttributeStringAsync(null, "message", null, testCase.FailureSummary).ConfigureAwait(false);
                await writer.WriteStringAsync(string.Join(Environment.NewLine, testCase.ValidationIssues)).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }
            else if (testCase.RunError is not null)
            {
                await writer.WriteElementStringAsync(null, "error", null, testCase.RunError).ConfigureAwait(false);
            }
            else if (!testCase.Success)
            {
                await writer.WriteStartElementAsync(null, "failure", null).ConfigureAwait(false);
                await writer.WriteAttributeStringAsync(null, "message", null, testCase.FailureSummary).ConfigureAwait(false);
                await writer.WriteStringAsync(string.Join(Environment.NewLine, testCase.Failures.Select(failure => failure.ToString()))).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }
}