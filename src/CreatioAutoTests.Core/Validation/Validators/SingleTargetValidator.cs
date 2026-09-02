using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape: keyword followed by exactly one element code, for example 'click SaveButton'.
/// Reference implementation for every validator: check arity, resolve, return targets.
/// Shared by click, clear and goto.
/// </summary>
public sealed class SingleTargetValidator : ICommandValidator
{
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 1)
        {
            return ValidationResult.Fail(
                "'" + command.Keyword + "' takes exactly one element code, for example '" +
                command.Keyword + " SaveButton'.");
        }

        var code = command.ValueAt(0)!;
        if (string.IsNullOrWhiteSpace(code))
        {
            return ValidationResult.Fail("'" + command.Keyword + "' needs an element code.");
        }

        if (!context.TryResolve(code, out var target, out var error))
        {
            return ValidationResult.Fail("'" + command.Keyword + "' cannot run: " + error + ".");
        }

        return ValidationResult.Ok(target);
    }
}
