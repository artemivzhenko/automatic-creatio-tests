using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

[Command("wait", Validator = typeof(DurationValidator), Usage = "wait <number> second|minute")]
public sealed class WaitCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var milliseconds = DurationValidator.ToMilliseconds(context.Command);
        context.Logger.LogInformation("Waiting for {Duration} ms.", milliseconds);

        return Task.Delay(milliseconds, cancellationToken);
    }
}