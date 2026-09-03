using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>Validates targetless commands that accept no arguments, such as <c>end</c>.</summary>
public sealed class NoArgumentsValidator : ICommandValidator
{
    /// <summary>Accepts the command only when it has no DSL arguments.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        return command.ArgumentCount == 0
            ? ValidationResult.Ok()
            : ValidationResult.Fail("'" + command.Keyword + "' does not take arguments.");
    }
}