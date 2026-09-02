namespace CreatioAutoTests.Execution;

/// <summary>How one command finished.</summary>
public sealed record CommandOutcome(
    ExecutionStep Step,
    bool Success,
    string? Message,
    TimeSpan Duration)
{
    public int LineNumber => Step.Command.LineNumber;

    public string RawText => Step.Command.RawText;

    public override string ToString() =>
        (Success ? "ok   " : "FAIL ") + "line " + LineNumber + ": " + RawText +
        (Message is null ? string.Empty : " -- " + Message);
}

/// <summary>Everything that happened to one .act file.</summary>
public sealed class TestCaseResult
{
    public required string Name { get; init; }

    public required string FilePath { get; init; }

    public TimeSpan Duration { get; set; }

    /// <summary>Problems found before anything ran. When non-empty nothing was executed.</summary>
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; init; } = Array.Empty<ValidationIssue>();

    public List<CommandOutcome> Outcomes { get; } = new();

    /// <summary>Set when the run itself broke, as opposed to a test assertion failing.</summary>
    public string? RunError { get; set; }

    public bool IsValid => ValidationIssues.Count == 0;

    public bool Success => IsValid && RunError is null && Outcomes.All(o => o.Success);

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
