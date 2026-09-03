using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Writes a value: a quoted literal, a static value, or whatever another element currently holds.
/// Which of the three it is was settled during validation, so this body has no type switch and no
/// knowledge of text, numbers, dates or lookups.
/// </summary>
[Command("set",
    Validator = typeof(AssignmentValidator),
    SupportedTargets = new[] { typeof(IValueElement) },
    Usage = "set <code> \"value\" | set <code> <sourceCodeOrStaticValue>")]
public sealed class SetCommand : ICommand
{
    /// <summary>Writes a validated literal, static value, or source element value to the target.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Target<IValueElement>();

        if (target is IStateAware stateAware &&
            await stateAware.IsReadOnlyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommandFailedException("'" + target.Code + "' is read-only on the page and cannot be set.");
        }

        // A second target means the validator resolved an element to copy from.
        var value = context.HasTarget(1)
            ? await context.Target<IValueElement>(1).GetValueAsStringAsync(cancellationToken).ConfigureAwait(false)
            : context.ResolveValue(1);

        await target.SetValueFromStringAsync(value ?? string.Empty, cancellationToken).ConfigureAwait(false);

        context.Logger.LogDebug("Set {Code} to {Value}.", target.Code, value);
    }
}
