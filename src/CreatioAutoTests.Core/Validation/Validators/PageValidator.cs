using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'open': a configured page, optionally an existing record.
///   open TestPage
///   open TestPage for 0c48c818-08f0-4f70-9b58-be512227f3bf
/// Reports the page it moves to so later lines resolve their codes against it.
/// </summary>
public sealed class PageValidator : ICommandValidator
{
    /// <summary>Validates an <c>open</c> command and advances the virtual page for later code resolution.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount is not (1 or 3))
        {
            return ValidationResult.Fail(
                "'open' takes a page name, optionally followed by 'for <recordId>'.");
        }

        var pageName = command.ValueAt(0)!;
        if (string.IsNullOrWhiteSpace(pageName))
        {
            return ValidationResult.Fail("'open' needs a page name.");
        }

        if (!context.Catalog.PageExists(pageName))
        {
            return ValidationResult.Fail(
                "Page '" + pageName + "' is not configured. Known pages: " +
                string.Join(", ", context.Catalog.PageNames) + ".");
        }

        if (command.ArgumentCount == 3)
        {
            var keyword = command.ValueAt(1)!;
            if (!string.Equals(keyword, "for", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail(
                    "'open <page> <x> <id>' expects 'for' in the middle, found '" + keyword + "'.");
            }

            if (string.IsNullOrWhiteSpace(command.ValueAt(2)))
            {
                return ValidationResult.Fail("'open <page> for ...' needs a record id.");
            }
        }

        return ValidationResult.OkOnPage(pageName);
    }
}
