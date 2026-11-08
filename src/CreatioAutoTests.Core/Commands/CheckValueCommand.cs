using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Compares what an element holds with an expected value. The comparison is driven by the value
/// kind the validator already determined, so numbers and dates compare as numbers and dates while
/// everything else compares as text.
/// </summary>
[Command("check-value",
    Validator = typeof(ComparisonValidator),
    SupportedTargets = new[] { typeof(IValueElement) },
    Usage = "check-value <code> eq|ne|gt|ge|lt|le|contains|startswith|endswith \"expected\"")]
public sealed class CheckValueCommand : ICommand
{
    /// <summary>Compares a target value using the static kind recorded during validation.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var element = context.Target<IValueElement>();
        var operatorText = context.Command.ValueAt(1)!.ToLowerInvariant();
        var expected = context.ResolveValue(2);

        var actual = await element.GetValueAsStringAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        if (!ValueComparer.Satisfies(context.KindOf(), actual, expected, operatorText))
        {
            throw new CommandFailedException(
                "'" + element.Code + "' holds \"" + actual + "\" which is not " + operatorText +
                " \"" + expected + "\".");
        }
    }
}
