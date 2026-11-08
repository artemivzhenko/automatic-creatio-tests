using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check-record':
///   check-record UsrOrder UsrName=Order 1;UsrAmount=10 exists
///   check-record UsrOrder UsrName=Order 1 not exists
/// The object is read over OData at run time, so only the shape is checked here: the filter must
/// parse and the line must end in 'exists' or 'not exists'.
/// </summary>
public sealed class RecordCheckValidator : ICommandValidator
{
    /// <summary>Validates the object name, the filter list and the trailing expectation.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 3)
        {
            return ValidationResult.Fail(
                "'check-record' takes an object, a filter and an expectation: " +
                "'check-record <object> Column=Value;Column=Value [not] exists'.");
        }

        var expectation = Expectation(command, out var filterTokens);
        if (expectation is null)
        {
            return ValidationResult.Fail("'check-record' must end in 'exists' or 'not exists'.");
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
