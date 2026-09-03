using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>Clears a target that advertises the <see cref="IClearable"/> capability.</summary>
[Command("clear",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(IClearable) },
    Usage = "clear <code>")]
public sealed class ClearCommand : ICommand
{
    /// <summary>Delegates clearing to the validated target.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<IClearable>().ClearAsync(cancellationToken);
}