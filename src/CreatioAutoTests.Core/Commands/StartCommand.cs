using CreatioAutoTests.Execution;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Starts a business process over the process engine service and keeps its result parameters for
/// the 'check-result' lines that follow. No browser is involved: this is the backend tier of a
/// case, which is why a process case can run without a page ever being opened.
/// </summary>
[Command("start",
    Validator = typeof(ProcessStartValidator),
    Usage = "start <process> [with Param=Value;Param=Value]")]
public sealed class StartCommand : ICommand
{
    /// <summary>Starts the validated process with its assignments and stores the outcome in the session.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var name = context.Command.ValueAt(0)!;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (context.Command.ArgumentCount > 2)
        {
            RecordAssignments.TryParse(RecordAssignments.Rejoin(context.Command.Arguments.Skip(2)), out var assignments, out _);
            foreach (var assignment in assignments)
            {
                values[assignment.Column] = context.Session.StaticValues.TryGetValue(assignment.Value, out var staticValue)
                    ? staticValue
                    : assignment.Value;
            }
        }

        var run = await context.Session.StartProcessAsync(name, values, cancellationToken).ConfigureAwait(false);

        if (!run.Success)
        {
            throw new CommandFailedException("'" + name + "' did not start: " + run.Error);
        }

        context.Logger.LogInformation("Started {Process} ({ProcessId}): {Results}.",
            name, run.ProcessId, string.Join(", ", run.Results.Select(r => r.Key + "=" + r.Value)));
    }
}
