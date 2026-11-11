using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check-record':
///   check-record UsrOrder UsrName=Order 1;UsrAmount=10 exists
///   check-record UsrOrder UsrName=Order 1 not exists
///   check-record UsrOrder Id=$Order has UsrStatus eq "New"
/// The object is read over OData at run time, so only the shape is checked here: the filter must
/// parse and the line must end in 'exists' or 'not exists'.
/// </summary>
public sealed class RecordCheckValidator : ICommandValidator
{
    /// <summary>Validates the object name, the filter list and the trailing expectation.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (HasForm(command, out var hasFilter, out _, out var op, out _))
        {
            if (!DataShapes.ValueOperators.Contains(op))
            {
                return ValidationResult.Fail("'check-record ... has' compares with " + string.Join(", ", DataShapes.ValueOperators) + "; found '" + op + "'.");
            }

            return DataShapes.Pairs("check-record " + command.ValueAt(0), hasFilter) ?? ValidationResult.Ok();
        }

        if (command.ArgumentCount < 3)
        {
            return ValidationResult.Fail(
                "'check-record' takes an object, a filter and an expectation: " +
                "'check-record <object> Column=Value;Column=Value [not] exists'.");
        }

        var expectation = Expectation(command, out var filterTokens);
        if (expectation is null)
        {
            return ValidationResult.Fail("'check-record' must end in 'exists' or 'not exists', or in 'has <column> <op> <value>'.");
        }

        if (filterTokens.Count == 0)
        {
            return ValidationResult.Fail("'check-record " + command.ValueAt(0) + "' needs a filter: 'Column=Value;Column=Value'.");
        }

        var text = RecordAssignments.Rejoin(filterTokens);
        if (!RecordAssignments.TryParse(text, out _, out var parseError))
        {
            return ValidationResult.Fail("'check-record " + command.ValueAt(0) + "' cannot be read: " + parseError + ".");
        }

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Reads the 'has &lt;column&gt; &lt;op&gt; &lt;value&gt;' form: the filter between the object and 'has', then the three
    /// tokens after it. False for the 'exists' forms.
    /// </summary>
    public static bool HasForm(ParsedCommand command, out IReadOnlyList<CommandToken> filterTokens, out string column, out string op, out CommandToken value)
    {
        var at = DataShapes.LastIndexOf(command, "has");
        filterTokens = Array.Empty<CommandToken>();
        column = op = string.Empty;
        value = null!;
        if (at < 2 || at != command.ArgumentCount - 4)
        {
            return false;
        }

        filterTokens = command.Arguments.Skip(1).Take(at - 1).ToArray();
        column = command.ValueAt(at + 1)!;
        op = command.ValueAt(at + 2)!.ToLowerInvariant();
        value = command.Arguments[at + 3];
        return true;
    }

    /// <summary>
    /// Reads the trailing 'exists' / 'not exists' and returns the tokens between the object and it.
    /// Null when the line does not end the way the command requires.
    /// </summary>
    public static bool? Expectation(ParsedCommand command, out IReadOnlyList<CommandToken> filterTokens)
    {
        var args = command.Arguments;
        filterTokens = Array.Empty<CommandToken>();

        if (args.Count >= 2 && string.Equals(args[^1].Value, "exists", StringComparison.OrdinalIgnoreCase))
        {
            var negated = args.Count >= 3 && string.Equals(args[^2].Value, "not", StringComparison.OrdinalIgnoreCase);
            filterTokens = args.Skip(1).Take(args.Count - (negated ? 3 : 2)).ToArray();
            return !negated;
        }

        return null;
    }
}
