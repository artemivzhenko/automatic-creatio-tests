using CreatioAutoTests.Commands;
using CreatioAutoTests.Validation;

namespace CreatioAutoTests.Execution;

/// <summary>One validated command, ready to run: nothing is looked up again at execution time.</summary>
public sealed record ExecutionStep(
    ParsedCommand Command,
    CommandRegistration Registration,
    IReadOnlyList<ResolvedTarget> Targets)
{
    /// <summary>Formats the validated command this step will execute.</summary>
    public override string ToString() => Command.ToString();
}

/// <summary>A problem found during the validation pass, tied to the line that caused it.</summary>
public sealed record ValidationIssue(int LineNumber, string RawText, string Message)
{
    /// <summary>Formats a source-tied validation problem for CLI and report output.</summary>
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

    /// <summary>Logical test name associated with this complete plan or issue collection.</summary>
    public string TestName { get; }

    /// <summary>Source script path associated with this plan.</summary>
    public string FilePath { get; }

    /// <summary>Ordered calls to execute when the whole script validated successfully.</summary>
    public IReadOnlyList<ExecutionStep> Steps { get; }

    /// <summary>Every validation issue that prevents this file from running.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>Whether the plan has no validation issues and may be executed.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Creates an executable plan from the fully validated ordered steps.</summary>
    public static ExecutionPlan Valid(string testName, string filePath, IReadOnlyList<ExecutionStep> steps) =>
        new(testName, filePath, steps, Array.Empty<ValidationIssue>());

    /// <summary>Creates a non-executable plan that preserves every issue found in the file.</summary>
    public static ExecutionPlan Invalid(string testName, string filePath, IReadOnlyList<ValidationIssue> issues) =>
        new(testName, filePath, Array.Empty<ExecutionStep>(), issues);

    /// <summary>Human readable summary used in reports and in the CLI.</summary>
    public string DescribeIssues() =>
        Issues.Count == 0
            ? string.Empty
            : string.Join(System.Environment.NewLine, Issues.Select(i => "  " + i));
}
