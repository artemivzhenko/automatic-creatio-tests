using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>Pauses execution for the validated duration.</summary>
[Command("wait", Validator = typeof(DurationValidator), Usage = "wait <number> second|minute")]
public sealed class WaitCommand : ICommand
{
    /// <summary>Delays with cancellation support using the duration validator's millisecond conversion.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var milliseconds = DurationValidator.ToMilliseconds(context.Command);
        context.Logger.LogInformation("Waiting for {Duration} ms.", milliseconds);

        return Task.Delay(milliseconds, cancellationToken);
    }
}