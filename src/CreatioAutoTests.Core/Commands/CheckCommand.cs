using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Asserts presence or state.
///   check X exists          - X is in the DOM and its caption matches the configured label
///   check X not exists      - X is absent
///   check X is [not] required
///   check X is [not] readonly
/// Presence deliberately does not assert required or readonly: those are separate, explicit lines.
/// </summary>
[Command("check",
    Validator = typeof(StateCheckValidator),
    SupportedTargets = new[] { typeof(IExistable) },
    Usage = "check <code> [not] exists | check <code> is [not] required|readonly")]
public sealed class CheckCommand : ICommand
{
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var element = context.Target<IExistable>();
        var arguments = context.Command.Arguments.Skip(1).Select(a => a.Value.ToLowerInvariant()).ToArray();

        switch (arguments)
        {
            case ["exists"]:
                await AssertExistsAsync(element, cancellationToken).ConfigureAwait(false);
                return;

            case ["not", "exists"]:
                // Absence is the expected answer here, so it gets the short budget.
                if (await element
                        .ExistsAsync(context.Session.Settings.AbsenceTimeoutMs, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new CommandFailedException("'" + element.Code + "' is on the page but was expected to be absent.");
                }

                return;

            case ["is", var property]:
                await AssertStateAsync(context, element, property, expected: true, cancellationToken).ConfigureAwait(false);
                return;

            case ["is", "not", var property]:
                await AssertStateAsync(context, element, property, expected: false, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new CommandFailedException("Unsupported form of 'check': " + context.Command.RawText + ".");
        }
    }

    /// <summary>
    /// Presence also confirms the caption, so that a renamed field fails with a message naming both
    /// captions instead of a bare "not found".
    /// </summary>
    private static async Task AssertExistsAsync(IExistable element, CancellationToken cancellationToken)
    {
        if (!await element.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommandFailedException("'" + element.Code + "' was not found on the page.");
        }

        if (element is not ILabeled labeled || string.IsNullOrWhiteSpace(element.Label))
        {
            return;
        }

        var actual = await labeled.GetActualLabelAsync(cancellationToken).ConfigureAwait(false);
        var expected = UiElementBase.NormalizeLabel(element.Label);

        if (actual is null)
        {
            throw new CommandFailedException(
                "'" + element.Code + "' renders no caption, but the configuration expects '" + expected + "'.");
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new CommandFailedException(
                "'" + element.Code + "' is labelled '" + actual + "' but the configuration expects '" + expected + "'.");
        }
    }

    private static async Task AssertStateAsync(
        CommandContext context,
        IExistable element,
        string property,
        bool expected,
        CancellationToken cancellationToken)
    {
        var stateAware = context.Target<IStateAware>();

        var actual = property == "required"
            ? await stateAware.IsRequiredAsync(cancellationToken).ConfigureAwait(false)
            : await stateAware.IsReadOnlyAsync(cancellationToken).ConfigureAwait(false);

        if (actual != expected)
        {
            throw new CommandFailedException(
                "'" + element.Code + "' is " + (actual ? string.Empty : "not ") + property +
                " but was expected to be " + (expected ? string.Empty : "not ") + property + ".");
        }
    }
}
