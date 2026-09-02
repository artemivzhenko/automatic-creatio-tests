using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>Outcome of a whole run.</summary>
public sealed class RunResult
{
    public required IReadOnlyList<TestCaseResult> Cases { get; init; }

    /// <summary>
    /// Analysis scripts that ran before the cases, empty when analysis was not asked for.
    /// Kept apart from the cases: they describe pages, they do not assert anything.
    /// </summary>
    public IReadOnlyList<TestCaseResult> AnalysisScripts { get; init; } = Array.Empty<TestCaseResult>();

    public TimeSpan Duration { get; init; }

    /// <summary>A failed analysis script fails the run: the configuration it should have written is missing.</summary>
    public bool Success => Cases.All(c => c.Success) && AnalysisScripts.All(a => a.Success);

    public int Passed => Cases.Count(c => c.Success);

    public int Failed => Cases.Count - Passed;
}

/// <summary>
/// Renders a finished run. Kept as an interface so extra formats, such as JUnit XML for CI,
/// are additions rather than edits.
/// </summary>
public interface IReportWriter
{
    Task WriteAsync(RunResult result, CancellationToken cancellationToken = default);
}
