namespace CreatioAutoTests.Execution;

/// <summary>How one command finished.</summary>
public sealed record CommandOutcome(
    ExecutionStep Step,
    bool Success,
    string? Message,
    TimeSpan Duration)
{
    /// <summary>Source line of the command whose execution produced this outcome.</summary>
    public int LineNumber => Step.Command.LineNumber;

    /// <summary>Original DSL text of the command for reports and diagnostics.</summary>
    public string RawText => Step.Command.RawText;

    /// <summary>Formats the command result as a report-ready success or failure line.</summary>
    public override string ToString() =>
        (Success ? "ok   " : "FAIL ") + "line " + LineNumber + ": " + RawText +
        (Message is null ? string.Empty : " -- " + Message);
}

/// <summary>Everything that happened to one .act file.</summary>
public sealed class TestCaseResult
{
    /// <summary>Logical test case name derived from the script file.</summary>
    public required string Name { get; init; }

    /// <summary>Path of the script that produced this result.</summary>
    public required string FilePath { get; init; }

    /// <summary>Elapsed execution time, excluding static validation.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Problems found before anything ran. When non-empty nothing was executed.</summary>
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; init; } = Array.Empty<ValidationIssue>();

    /// <summary>One outcome for each execution attempt after successful validation.</summary>
    public List<CommandOutcome> Outcomes { get; } = new();

    /// <summary>Set when the run itself broke, as opposed to a test assertion failing.</summary>
    public string? RunError { get; set; }

    /// <summary>Whether the script had no pre-execution validation issues.</summary>
    public bool IsValid => ValidationIssues.Count == 0;

    /// <summary>True only when validation passed, no run error occurred, and every attempted command succeeded.</summary>
    public bool Success => IsValid && RunError is null && Outcomes.All(o => o.Success);

    /// <summary>Command outcomes that failed assertions or browser operations during the run.</summary>
    public IEnumerable<CommandOutcome> Failures => Outcomes.Where(o => !o.Success);

    /// <summary>Short reason a case did not pass, suitable for a one-line report row.</summary>
    public string? FailureSummary
    {
        get
        {
            if (!IsValid)
            {
                return ValidationIssues.Count + " validation error(s)";
            }

            if (RunError is not null)
            {
                return RunError;
            }

            var first = Failures.FirstOrDefault();
            return first is null ? null : "line " + first.LineNumber + ": " + first.Message;
        }
    }
}
