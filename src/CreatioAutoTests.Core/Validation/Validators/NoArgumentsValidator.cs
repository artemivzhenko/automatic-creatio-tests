using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

public sealed class NoArgumentsValidator : ICommandValidator
{
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        return command.ArgumentCount == 0
            ? ValidationResult.Ok()
            : ValidationResult.Fail("'" + command.Keyword + "' does not take arguments.");
    }
}