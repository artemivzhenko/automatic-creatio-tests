using CreatioAutoTests.Commands;
using CreatioAutoTests.Validation;

namespace CreatioAutoTests.Execution;

/// <summary>One validated command, ready to run: nothing is looked up again at execution time.</summary>
public sealed record ExecutionStep(
    ParsedCommand Command,
    CommandRegistration Registration,
    IReadOnlyList<ResolvedTarget> Targets)
{
    public override string ToString() => Command.ToString();
}

/// <summary>A problem found during the validation pass, tied to the line that caused it.</summary>
public sealed record ValidationIssue(int LineNumber, string RawText, string Message)
{
    public override string ToString() => "line " + LineNumber + ": " + Message + "  <<" + RawText + ">>";
}

/// <summary>
/// Result of validating one .act file: either the ordered chain of calls to make, or every reason
/// the file cannot run. Nothing is executed until the whole file validates.
/// </summary>
public sealed class ExecutionPlan
{
    private ExecutionPlan(
        string testName,
        string filePath,
        IReadOnlyList<ExecutionStep> steps,
        IReadOnlyList<ValidationIssue> issues)
    {
        TestName = testName;
        FilePath = filePath;
        Steps = steps;
        Issues = issues;
    }

    public string TestName { get; }

    public string FilePath { get; }

    public IReadOnlyList<ExecutionStep> Steps { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    public static ExecutionPlan Valid(string testName, string filePath, IReadOnlyList<ExecutionStep> steps) =>
        new(testName, filePath, steps, Array.Empty<ValidationIssue>());

    public static ExecutionPlan Invalid(string testName, string filePath, IReadOnlyList<ValidationIssue> issues) =>
        new(testName, filePath, Array.Empty<ExecutionStep>(), issues);

    /// <summary>Human readable summary used in reports and in the CLI.</summary>
    public string DescribeIssues() =>
        Issues.Count == 0
            ? string.Empty
            : string.Join(System.Environment.NewLine, Issues.Select(i => "  " + i));
}
