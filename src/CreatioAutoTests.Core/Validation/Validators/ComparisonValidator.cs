using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check-value': target, operator, expected value. The operator is checked against the
/// kind of value the target holds, so 'check-value SomeText gt "5"' fails before the run starts.
/// </summary>
public sealed class ComparisonValidator : ICommandValidator
{
    private readonly ValueCompatibilityPolicy _policy;

    /// <summary>Initializes static comparison operator and literal compatibility checking.</summary>
    public ComparisonValidator(ValueCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Validates a <c>check-value</c> target, operator, and expected literal or static value.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 3)
        {
            return ValidationResult.Fail(
                "'check-value' takes a code, an operator and a value: 'check-value <code> eq \"expected\"'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'check-value' cannot run: " + resolveError + ".");
        }

        var kind = target.Entry.ValueKind;
        if (kind == ValueKind.None)
        {
            return ValidationResult.Fail(
                "'" + code + "' is a " + target.Entry.Definition.Type + " and has no value to compare.");
        }

        var operatorText = command.ValueAt(1)!.ToLowerInvariant();
        var allowed = _policy.OperatorsFor(kind);

        if (!allowed.Contains(operatorText))
        {
            return ValidationResult.Fail(
                "operator '" + operatorText + "' does not apply to " + kind + " values. Allowed here: " +
                string.Join(", ", allowed) + ".");
        }

        var expected = command.Arguments[2];
        var literal = expected.IsQuoted
            ? expected.Value
            : context.StaticValues.TryGetValue(expected.Value, out var staticValue)
                ? staticValue
                : null;

        if (literal is null)
        {
            var known = context.StaticValues.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return ValidationResult.Fail(
                "the expected value must be quoted or name a static value; '" + expected.Value +
                "' is neither. Known static values: " +
                (known.Length == 0 ? "(none)" : string.Join(", ", known)) + ".");
        }

        // Ordering and equality on numbers and dates need a value that actually parses.
        if (!_policy.IsLiteralAssignable(literal, kind, out var literalError))
        {
            return ValidationResult.Fail("'check-value " + code + "' cannot compare: " + literalError + ".");
        }

        return ValidationResult.Ok(target);
    }
}
