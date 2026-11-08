using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check-result': an output parameter of the process the script last started, an
/// operator and an expected value. The operator and the literal are checked against the
/// parameter's type the way 'check-value' checks them against a field's.
/// </summary>
public sealed class ResultCheckValidator : ICommandValidator
{
    private readonly ValueCompatibilityPolicy _policy;

    /// <summary>Initializes the operator and literal checks shared with the page commands.</summary>
    public ResultCheckValidator(ValueCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Validates the parameter, the operator and the expected literal or static value.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 3)
        {
            return ValidationResult.Fail(
                "'check-result' takes a parameter, an operator and a value: 'check-result <parameter> eq \"expected\"'.");
        }

        if (context.CurrentProcess is null || !context.Processes.TryGetValue(context.CurrentProcess, out var process))
        {
            return ValidationResult.Fail("'check-result' needs a process: use 'start <process>' before it.");
        }

        var code = command.ValueAt(0)!;
        var parameter = process.Find(code);
        if (parameter is null || !process.Outputs.Contains(parameter))
        {
            return ValidationResult.Fail(
                "'" + process.Name + "' has no output parameter '" + code + "'. Its outputs are: " +
                string.Join(", ", process.Outputs.Select(p => p.Code)) + ".");
        }

        var operatorText = command.ValueAt(1)!.ToLowerInvariant();
        var allowed = _policy.OperatorsFor(parameter.ValueKind);
        if (!allowed.Contains(operatorText))
        {
            return ValidationResult.Fail(
                "operator '" + operatorText + "' does not apply to " + parameter.ValueKind + " values. Allowed here: " +
                string.Join(", ", allowed) + ".");
        }

        var expected = command.Arguments[2];
        var literal = expected.IsQuoted
            ? expected.Value
            : context.StaticValues.TryGetValue(expected.Value, out var staticValue) ? staticValue : null;

        if (literal is null)
        {
            return ValidationResult.Fail(
                "the expected value must be quoted or name a static value; '" + expected.Value + "' is neither.");
        }

        if (!_policy.IsLiteralAssignable(literal, parameter.ValueKind, out var literalError))
        {
            return ValidationResult.Fail("'check-result " + code + "' cannot compare: " + literalError + ".");
        }

        return ValidationResult.Ok();
    }
}
