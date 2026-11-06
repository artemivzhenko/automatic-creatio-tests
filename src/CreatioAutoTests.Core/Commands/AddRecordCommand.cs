using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Adds a record to an element that accepts one in place.
///   add-record X set Column=Value;Column=Value
///
/// The values are entered into the row the element opens, column by column. Whether the row reaches
/// the database follows the page's own save, the same way a value written with 'set' does.
/// </summary>
[Command("add-record",
    Validator = typeof(AddRecordValidator),
    SupportedTargets = new[] { typeof(IRecordAddable) },
    Usage = "add-record <code> set Column=Value;Column=Value")]
public sealed class AddRecordCommand : ICommand
{
    /// <summary>Enters the validated assignments as one new record on the resolved target.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var text = RecordAssignments.Rejoin(context.Command.Arguments.Skip(2));

        // Validation already rejected anything unreadable, so a failure here would be a defect
        // rather than a bad script, and it is reported as one.
        if (!RecordAssignments.TryParse(text, out var assignments, out var error))
        {
            throw new CommandFailedException(
                "'" + context.Command.RawText + "' passed validation but cannot be read: " + error + ".");
        }

        return context.Target<IRecordAddable>().AddRecordAsync(assignments, cancellationToken);
    }
}
