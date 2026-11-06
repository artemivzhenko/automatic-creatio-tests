using System.Text.Json;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'add-record':
///   add-record X set Column=Value;Column=Value
///
/// The assignment list is read here as well as at execution time, so a malformed list, a repeated
/// column or a column the page does not have is reported before a browser is started.
/// </summary>
public sealed class AddRecordValidator : ICommandValidator
{
    /// <summary>Validates the shape, the assignment list, and the columns it names.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 3)
        {
            return ValidationResult.Fail(
                "'add-record' takes a code and the values for the new record: " +
                "'add-record <code> set Column=Value;Column=Value'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'add-record' cannot run: " + resolveError + ".");
        }

        if (!string.Equals(command.ValueAt(1), "set", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Fail(
                "'add-record " + code + "' must be followed by 'set', not '" + command.ValueAt(1) + "'.");
        }

        if (!target.Entry.Supports(typeof(IRecordAddable)))
        {
            return ValidationResult.Fail(
                "'" + code + "' is a " + target.Entry.Definition.Type + " and cannot take a new record.");
        }

        var text = RecordAssignments.Rejoin(command.Arguments.Skip(2));
        if (!RecordAssignments.TryParse(text, out var assignments, out var parseError))
        {
            return ValidationResult.Fail(
                "'add-record " + code + "' cannot be read: " + parseError +
                ". Expected 'Column=Value" + RecordAssignments.PairSeparator + "Column=Value'.");
        }

        var unknown = UnknownColumn(target, assignments, out var known);
        if (unknown is not null)
        {
            return ValidationResult.Fail(
                "'" + code + "' has no column '" + unknown + "'. Its columns are: " + known + ".");
        }

        return ValidationResult.Ok(target);
    }

    /// <summary>
    /// Finds the first assigned column the page file does not list.
    ///
    /// Only possible when the element declares its columns; one that does not is left to fail at
    /// execution time, where the grid itself can say what it has.
    /// </summary>
    private static string? UnknownColumn(
        ResolvedTarget target,
        IReadOnlyList<RecordAssignment> assignments,
        out string known)
    {
        known = string.Empty;

        var extra = target.Entry.Definition.Extra;
        if (extra is null
            || !extra.TryGetValue("columns", out var declared)
            || declared.ValueKind != JsonValueKind.Array
            || declared.GetArrayLength() == 0)
        {
            return null;
        }

        var columns = declared.EnumerateArray()
            .Select(c => c.GetString() ?? string.Empty)
            .Where(c => c.Length > 0)
            .ToArray();

        known = string.Join(", ", columns.Select(c => "'" + c + "'"));

        return assignments
            .Select(a => a.Column)
            .FirstOrDefault(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase));
    }
}
