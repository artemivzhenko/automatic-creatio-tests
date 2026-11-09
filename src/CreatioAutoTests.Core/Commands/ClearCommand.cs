using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Clears a target that advertises the <see cref="IClearable"/> capability.
/// A locked field is refused up front, the same way <c>set</c> refuses it: otherwise the clear
/// waits out the element timeout on an input that will never accept the keystroke.
/// </summary>
[Command("clear",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(IClearable) },
    Usage = "clear <code>")]
public sealed class ClearCommand : ICommand
{
    /// <summary>Delegates clearing to the validated target once it is known to be writable.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Target<IClearable>();

        if (target is IStateAware stateAware &&
            await stateAware.IsReadOnlyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommandFailedException("'" + target.Code + "' is read-only on the page and cannot be cleared.");
        }

        await target.ClearAsync(cancellationToken).ConfigureAwait(false);
    }
}
