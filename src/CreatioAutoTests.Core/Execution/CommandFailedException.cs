namespace CreatioAutoTests.Execution;

/// <summary>
/// Raised when a command runs but its outcome is wrong: an assertion did not hold, or the UI
/// refused the operation. Distinct from a framework fault, and the only failure kind the runner
/// reports as a failed test rather than a broken run.
/// </summary>
public sealed class CommandFailedException : Exception
{
    /// <summary>Creates a command-level test failure with an author-facing explanation.</summary>
    public CommandFailedException(string message) : base(message)
    {
    }

    /// <summary>Creates a command-level test failure that preserves its underlying exception.</summary>
    public CommandFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
