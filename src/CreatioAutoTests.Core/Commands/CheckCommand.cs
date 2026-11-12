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
///   check X is [not] editable
///   check X is [not] shown  - waits up to the element timeout for a rule to show or hide X
///   check X columns         - the grid renders exactly the configured columns, in that order
///   check console no errors [except "regex"] - no browser console error (or uncaught page error) so far
/// Presence deliberately does not assert required or readonly: those are separate, explicit lines.
/// </summary>
[Command("check",
    Validator = typeof(StateCheckValidator),
    SupportedTargets = new[] { typeof(IExistable) },
    Usage = "check <code> [not] exists | check <code> is [not] required|readonly|editable|shown | check <code> columns | check console no errors [except \"<regex>\"]")]
public sealed class CheckCommand : ICommand
{
    /// <summary>Asserts validated presence, absence, required, or readonly state on the target.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (StateCheckValidator.ConsoleForm(context.Command, out var except) == true)
        {
            var allowed = except is null ? null : new System.Text.RegularExpressions.Regex(except);
            var errors = context.Session.ConsoleErrors.Where(e => allowed is null || !allowed.IsMatch(e)).ToArray();
            if (errors.Length > 0)
            {
                throw new CommandFailedException(errors.Length + " console error(s): " + string.Join(" | ", errors.Take(5).Select(e => e.Length > 200 ? e[..200] : e)));
            }

            return;
        }

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

            case ["columns"]:
                await AssertColumnsAsync(context, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Compares rendered columns with the configured ones, order included.
    ///
    /// The message names the first position that differs rather than only reporting a mismatch:
    /// two columns swapped and a column renamed look identical in a bare pass/fail, and they are
    /// entirely different page changes.
    /// </summary>
    private static async Task AssertColumnsAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var grid = context.Target<IColumned>();
        var expected = grid.ExpectedColumns;
        var actual = await grid.GetColumnsAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            if (!string.Equals(
                    UiElementBase.NormalizeLabel(actual[i]),
                    UiElementBase.NormalizeLabel(expected[i]),
                    StringComparison.Ordinal))
            {
                throw new CommandFailedException(
                    "'" + grid.Code + "' has '" + actual[i] + "' in column " + (i + 1) +
                    " but the configuration expects '" + expected[i] + "'. Columns are [" +
                    Describe(actual) + "], expected [" + Describe(expected) + "].");
            }
        }

        if (expected.Count != actual.Count)
        {
            throw new CommandFailedException(
                "'" + grid.Code + "' renders " + actual.Count + " column(s) but the configuration expects " +
                expected.Count + ". Columns are [" + Describe(actual) + "], expected [" + Describe(expected) + "].");
        }
    }

    private static string Describe(IReadOnlyList<string> columns) =>
        columns.Count == 0 ? string.Empty : string.Join(", ", columns.Select(c => "'" + c + "'"));

    private static async Task AssertStateAsync(
        CommandContext context,
        IExistable element,
        string property,
        bool expected,
        CancellationToken cancellationToken)
    {
        if (property == "shown")
        {
            // The wait is the assertion: a rule's effect is polled up to the element timeout, never sampled.
            if (!await context.Target<IVisibilityAware>()
                    .IsShownAsync(expected, context.Session.Settings.ElementTimeoutMs, cancellationToken).ConfigureAwait(false))
            {
                throw new CommandFailedException(
                    "'" + element.Code + "' was expected to be " + (expected ? string.Empty : "not ") + "shown, but " +
                    (expected ? "it is absent or hidden" : "it is still on the page") + " after " +
                    context.Session.Settings.ElementTimeoutMs + " ms.");
            }

            return;
        }

        var actual = property switch
        {
            "required" => await context.Target<IStateAware>()
                .IsRequiredAsync(cancellationToken).ConfigureAwait(false),
            "readonly" => await context.Target<IStateAware>()
                .IsReadOnlyAsync(cancellationToken).ConfigureAwait(false),
            "editable" => await context.Target<IEditableAware>()
                .IsEditableAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new CommandFailedException("Unknown property '" + property + "'."),
        };

        if (actual != expected)
        {
            throw new CommandFailedException(
                "'" + element.Code + "' is " + (actual ? string.Empty : "not ") + property +
                " but was expected to be " + (expected ? string.Empty : "not ") + property + ".");
        }
    }
}
