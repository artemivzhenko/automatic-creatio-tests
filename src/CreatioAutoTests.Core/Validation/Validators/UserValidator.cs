using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>Shape of 'use': one username that must exist in the environment configuration.</summary>
public sealed class UserValidator : ICommandValidator
{
    /// <summary>Validates that the script selects one configured user before its browser work begins.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 1)
        {
            return ValidationResult.Fail("'use' takes exactly one username: 'use <username>'.");
        }

        var username = command.ValueAt(0)!;
        if (string.IsNullOrWhiteSpace(username))
        {
            return ValidationResult.Fail("'use' needs a username.");
        }

        if (!context.KnownUsers.Contains(username))
        {
            return ValidationResult.Fail(
                "User '" + username + "' is not configured. Known users: " +
                string.Join(", ", context.KnownUsers.OrderBy(u => u, StringComparer.OrdinalIgnoreCase)) + ".");
        }

        return ValidationResult.OkWithUser();
    }
}
