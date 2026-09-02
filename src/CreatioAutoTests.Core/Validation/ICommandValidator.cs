using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation;

/// <summary>
/// Checks the shape of one command and resolves the elements it addresses.
/// Validators are deliberately generic: several commands share the same shape, so a validator is
/// picked by a command through <see cref="Commands.CommandAttribute.Validator"/> rather than
/// written once per keyword.
/// </summary>
public interface ICommandValidator
{
    ValidationResult Validate(ParsedCommand command, ValidationContext context);
}
