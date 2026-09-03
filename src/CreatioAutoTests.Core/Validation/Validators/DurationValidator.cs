using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>Validates English duration syntax shared by the <c>wait</c> command.</summary>
public sealed class DurationValidator : ICommandValidator
{
    /// <summary>Requires a positive number and the exact lower-case unit <c>second</c> or <c>minute</c>.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 2)
        {
            return ValidationResult.Fail("'" + command.Keyword + "' expects <number> second|minute.");
        }

        if (!int.TryParse(command.ValueAt(0), out var amount) || amount <= 0)
        {
            return ValidationResult.Fail("'" + command.Keyword + "' expects a positive whole number of second or minute.");
        }

        var unit = command.ValueAt(1);
        if (unit is not "second" and not "minute")
        {
            return ValidationResult.Fail("'" + command.Keyword + "' expects the unit 'second' or 'minute'.");
        }

        return ValidationResult.Ok();
    }

    /// <summary>Converts an already validated duration into the delay used by execution.</summary>
    public static int ToMilliseconds(ParsedCommand command)
    {
        var amount = int.Parse(command.ValueAt(0)!);
        return checked(amount * (command.ValueAt(1) == "minute" ? 60_000 : 1_000));
    }
}