using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>Outcome of a whole run.</summary>
public sealed class RunResult
{
    /// <summary>Test case results, kept separate from page-describing analysis scripts.</summary>
    public required IReadOnlyList<TestCaseResult> Cases { get; init; }

    /// <summary>
    /// Analysis scripts that ran before the cases, empty when analysis was not asked for.
    /// Kept apart from the cases: they describe pages, they do not assert anything.
    /// </summary>
    public IReadOnlyList<TestCaseResult> AnalysisScripts { get; init; } = Array.Empty<TestCaseResult>();

    /// <summary>Total elapsed duration of both requested run phases.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>A failed analysis script fails the run: the configuration it should have written is missing.</summary>
    public bool Success => Cases.All(c => c.Success) && AnalysisScripts.All(a => a.Success);

    /// <summary>Number of successful test cases, excluding analysis scripts.</summary>
    public int Passed => Cases.Count(c => c.Success);

    /// <summary>Number of unsuccessful test cases, excluding analysis scripts.</summary>
    public int Failed => Cases.Count - Passed;
}

/// <summary>
/// Renders a finished run. Kept as an interface so extra formats, such as JUnit XML for CI,
/// are additions rather than edits.
/// </summary>
public interface IReportWriter
{
    /// <summary>Emits a completed run in this writer's format, including any analysis phase result.</summary>
    Task WriteAsync(RunResult result, CancellationToken cancellationToken = default);
}
