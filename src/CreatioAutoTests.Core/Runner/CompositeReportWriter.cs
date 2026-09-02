using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Runner;

/// <summary>Fans a completed run out to every report format selected by the run settings.</summary>
public sealed class CompositeReportWriter : IReportWriter
{
    private readonly IReadOnlyList<IReportWriter> _writers;

    /// <summary>Combines the concrete report writers selected during service registration.</summary>
    public CompositeReportWriter(IReadOnlyList<IReportWriter> writers)
    {
        _writers = writers ?? throw new ArgumentNullException(nameof(writers));
    }

    /// <inheritdoc />
    public async Task WriteAsync(RunResult result, CancellationToken cancellationToken = default)
    {
        foreach (var writer in _writers)
        {
            await writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
        }
    }
}