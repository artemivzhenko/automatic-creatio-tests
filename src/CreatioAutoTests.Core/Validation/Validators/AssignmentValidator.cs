using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'set': a target plus either a quoted literal, the name of a static value, or another
/// element to copy from. Every one of those three cases is resolved and type checked here, which is
/// why <see cref="Commands.SetCommand"/> contains no type switch at all.
/// </summary>
public sealed class AssignmentValidator : ICommandValidator
{
    private readonly ValueCompatibilityPolicy _policy;

    /// <summary>Initializes static literal and source-to-target compatibility checking.</summary>
    public AssignmentValidator(ValueCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Validates a <c>set</c> target and resolves its literal, static value, or source element.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 2)
        {
            return ValidationResult.Fail(
                "'set' takes an element code and a value: 'set <code> \"value\"' or 'set <code> <source>'.");
        }

        var targetCode = command.ValueAt(0)!;
        if (!context.TryResolve(targetCode, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'set' cannot run: " + resolveError + ".");
        }

        var targetKind = target.Entry.ValueKind;
        if (targetKind == ValueKind.None)
        {
            return ValidationResult.Fail(
                "'" + targetCode + "' is a " + target.Entry.Definition.Type + " and does not hold a value.");
        }

        var source = command.Arguments[1];

        // A quoted argument is always a literal, even when it happens to look like a code.
        if (source.IsQuoted)
        {
            return CheckLiteral(source.Value, targetKind, target);
        }

        // An unquoted argument is a static value name first, an element code second.
        if (context.StaticValues.TryGetValue(source.Value, out var staticValue))
        {
            return CheckLiteral(staticValue, targetKind, target);
        }

        if (!context.TryResolve(source.Value, out var sourceTarget, out var sourceError))
        {
            var known = context.StaticValues.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return ValidationResult.Fail(
                "'set' cannot read from '" + source.Value + "': " + sourceError +
                ". It is not a static value either. Known static values: " +
                (known.Length == 0 ? "(none)" : string.Join(", ", known)) + ".");
        }

        if (!_policy.CanCopy(sourceTarget.Entry.ValueKind, targetKind, out var copyError))
        {
            return ValidationResult.Fail(
                "'set " + targetCode + " " + source.Value + "' is not allowed: " + copyError + ".");
        }

        return ValidationResult.Ok(target, sourceTarget);
    }

    private ValidationResult CheckLiteral(string literal, ValueKind targetKind, ResolvedTarget target)
    {
        if (!_policy.IsLiteralAssignable(literal, targetKind, out var error))
        {
            return ValidationResult.Fail("'set " + target.Code + "' rejected the value: " + error + ".");
        }

        return ValidationResult.Ok(target);
    }
}
