using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Asserts that a row with the given cell values is, or is not, among the rows an element renders.
///   check-row Products Product=Pump;Quantity=2 exists
///   check-row Products Product=Pump not exists
/// Cells are named by the column captions, never by position.
/// </summary>
[Command("check-row",
    Validator = typeof(RowCheckValidator),
    SupportedTargets = new[] { typeof(IRowed) },
    Usage = "check-row <code> Column=Value;Column=Value [not] exists")]
public sealed class CheckRowCommand : ICommand
{
    /// <summary>Compares the presence of the described row with the expectation the line ends in.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var expected = RecordCheckValidator.Expectation(context.Command, out var tokens)
                       ?? throw new CommandFailedException("'" + context.Command.RawText + "' must end in 'exists' or 'not exists'.");

        var cells = RowCommands.Cells(context, tokens);
        var element = context.Target<IRowed>();
        var actual = await element.HasRowAsync(cells, cancellationToken).ConfigureAwait(false);

        if (actual != expected)
        {
            throw new CommandFailedException(
                "'" + element.Code + "' " + (actual ? "has" : "has no") + " row " + string.Join("; ", cells) +
                " but it was expected " + (expected ? "to be there." : "not to be."));
        }
    }
}

/// <summary>Selects the row with the given cell values: <c>select-row Products Product=Pump</c>.</summary>
[Command("select-row",
    Validator = typeof(RowTargetValidator),
    SupportedTargets = new[] { typeof(IRowed) },
    Usage = "select-row <code> Column=Value;Column=Value")]
public sealed class SelectRowCommand : ICommand
{
    /// <summary>Clicks the described row.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<IRowed>().SelectRowAsync(RowCommands.Cells(context, context.Command.Arguments.Skip(1)), cancellationToken);
}

/// <summary>
/// Deletes the row with the given cell values through the element's own delete action, answering
/// the confirmation. The record is gone from the database when the line completes.
/// </summary>
[Command("delete-row",
    Validator = typeof(RowTargetValidator),
    SupportedTargets = new[] { typeof(IRowed) },
    Usage = "delete-row <code> Column=Value;Column=Value")]
public sealed class DeleteRowCommand : ICommand
{
    /// <summary>Deletes the described row.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<IRowed>().DeleteRowAsync(RowCommands.Cells(context, context.Command.Arguments.Skip(1)), cancellationToken);
}

/// <summary>
/// Declares which configured page the browser is on after Creatio navigated by itself: a button
/// that opened another record, a link that was followed. Nothing is navigated; the open page is
/// rebound so that later lines resolve their codes against the right configuration.
///   click ServicesAddButton
///   on ServiceClassic
/// </summary>
[Command("on", Validator = typeof(OnPageValidator), Usage = "on <page>")]
public sealed class OnCommand : ICommand
{
    /// <summary>Rebinds the open page to the named configuration and waits for it to be ready.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Session.AdoptPageAsync(context.Command.ValueAt(0)!, cancellationToken);
}

internal static class RowCommands
{
    /// <summary>Reads the cell list that validation already accepted.</summary>
    public static IReadOnlyList<RecordAssignment> Cells(CommandContext context, IEnumerable<CommandToken> tokens)
    {
        var text = RecordAssignments.Rejoin(tokens);
        if (!RecordAssignments.TryParse(text, out var cells, out var error))
        {
            throw new CommandFailedException("'" + context.Command.RawText + "' passed validation but cannot be read: " + error + ".");
        }

        return cells;
    }
}
