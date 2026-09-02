using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

[Command("clear",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(IClearable) },
    Usage = "clear <code>")]
public sealed class ClearCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<IClearable>().ClearAsync(cancellationToken);
}