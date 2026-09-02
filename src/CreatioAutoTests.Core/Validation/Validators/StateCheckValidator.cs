using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check': presence or a state assertion.
///   check X exists            check X not exists
///   check X is required       check X is not required
///   check X is readonly       check X is not readonly
/// State assertions additionally require the element to expose that state at all.
/// </summary>
public sealed class StateCheckValidator : ICommandValidator
{
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 2)
        {
            return ValidationResult.Fail(
                "'check' takes a code and what to assert: 'check <code> [not] exists' or " +
                "'check <code> is [not] required|readonly'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'check' cannot run: " + resolveError + ".");
        }

        var rest = command.Arguments.Skip(1).Select(a => a.Value.ToLowerInvariant()).ToArray();

        return rest switch
        {
            ["exists"] => ValidationResult.Ok(target),
            ["not", "exists"] => ValidationResult.Ok(target),
            ["is", var property] => RequireState(property, target, code),
            ["is", "not", var property] => RequireState(property, target, code),
            _ => ValidationResult.Fail(
                "'check " + code + " " + string.Join(' ', rest) +
                "' is not a known form. Use '[not] exists' or 'is [not] required|readonly'.")
        };
    }

    private static ValidationResult RequireState(string property, ResolvedTarget target, string code)
    {
        if (property is not ("required" or "readonly"))
        {
            return ValidationResult.Fail(
                "'check " + code + " is ...' supports 'required' and 'readonly', not '" + property + "'.");
        }

        if (!target.Entry.Supports(typeof(IStateAware)))
        {
            return ValidationResult.Fail(
                "'" + code + "' is a " + target.Entry.Definition.Type +
                " and does not expose required/readonly state.");
        }

        return ValidationResult.Ok(target);
    }
}
