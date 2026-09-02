namespace CreatioAutoTests.Execution;

/// <summary>
/// Raised when a command runs but its outcome is wrong: an assertion did not hold, or the UI
/// refused the operation. Distinct from a framework fault, and the only failure kind the runner
/// reports as a failed test rather than a broken run.
/// </summary>
public sealed class CommandFailedException : Exception
{
    public CommandFailedException(string message) : base(message)
    {
    }

    public CommandFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
