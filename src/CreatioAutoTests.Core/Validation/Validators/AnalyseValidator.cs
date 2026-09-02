using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'analyse':
///   analyse                         -- the page that is currently open
///   analyse "/0/Shell/#Card/X/..."  -- a page that is not configured yet
///   analyse "/0/Shell/#Card/X/..." AsName
///
/// The address is not checked against the catalog on purpose: analysing a page nobody has described
/// yet is the reason the command exists.
/// </summary>
public sealed class AnalyseValidator : ICommandValidator
{
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount > 2)
        {
            return ValidationResult.Fail(
                "'analyse' takes at most a page address and a name: 'analyse \"<path>\" [<name>]'.");
        }

        if (command.ArgumentCount == 0)
        {
            if (string.IsNullOrWhiteSpace(context.CurrentPage))
            {
                return ValidationResult.Fail(
                    "'analyse' without an address analyses the open page, so 'open' must come first. " +
                    "Give it an address to analyse a page that is not configured yet.");
            }

            return ValidationResult.Ok();
        }

        if (string.IsNullOrWhiteSpace(command.ValueAt(0)))
        {
            return ValidationResult.Fail("'analyse' needs a page address.");
        }

        if (command.ArgumentCount == 2 && string.IsNullOrWhiteSpace(command.ValueAt(1)))
        {
            return ValidationResult.Fail("'analyse' was given an empty page name.");
        }

        return ValidationResult.Ok();
    }
}
