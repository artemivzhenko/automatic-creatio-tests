using System.Text.Json;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check': presence or a state assertion.
///   check X exists            check X not exists
///   check X is required       check X is not required
///   check X is readonly       check X is not readonly
///   check X is editable       check X is not editable
///   check X columns
/// State assertions additionally require the element to expose that state at all, and a column
/// assertion additionally requires the element to declare the columns it is being checked against.
/// </summary>
public sealed class StateCheckValidator : ICommandValidator
{
    /// <summary>Validates check syntax and ensures state assertions target a state-aware element.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 2)
        {
            return ValidationResult.Fail(
                "'check' takes a code and what to assert: 'check <code> [not] exists', " +
                "'check <code> is [not] required|readonly|editable' or 'check <code> columns'.");
        }

        if (ConsoleForm(command, out var except) is { } consoleForm)
        {
            if (!consoleForm)
            {
                return ValidationResult.Fail("'check console' reads 'check console no errors [except \"<regex>\"]'.");
            }

            try
            {
                _ = except is null ? null : new System.Text.RegularExpressions.Regex(except);
            }
            catch (ArgumentException ex)
            {
                return ValidationResult.Fail("'check console ... except' has a regex that does not compile: " + ex.Message);
            }

            return ValidationResult.Ok();
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
            ["columns"] => RequireColumns(target, code),
            ["is", var property] => RequireState(property, target, code),
            ["is", "not", var property] => RequireState(property, target, code),
            _ => ValidationResult.Fail(
                "'check " + code + " " + string.Join(' ', rest) +
                "' is not a known form. Use '[not] exists', 'is [not] required|readonly|editable' " +
                "or 'columns'.")
        };
    }

    /// <summary>
    /// 'check console no errors [except "regex"]': null when the line is about an element, false when it names the
    /// console in a wrong form, true for the right form with the optional regex.
    /// </summary>
    public static bool? ConsoleForm(ParsedCommand command, out string? except)
    {
        except = null;
        if (command.ArgumentAt(0) is not { IsQuoted: false } first || !string.Equals(first.Value, "console", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var words = command.Arguments.Skip(1).Take(2).Select(a => a.Value.ToLowerInvariant()).ToArray();
        if (words is not ["no", "errors"])
        {
            return false;
        }

        if (command.ArgumentCount == 3)
        {
            return true;
        }

        if (command.ArgumentCount == 5 && DataShapes.Is(command, 3, "except") && command.Arguments[4].IsQuoted)
        {
            except = command.Arguments[4].Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Accepts a column assertion only when there is something to assert.
    ///
    /// The expected columns live in the page file, so an empty list is not a failed comparison but a
    /// line that can never fail -- exactly the kind of quietly useless test the validation pass
    /// exists to catch before a browser is ever started.
    /// </summary>
    private static ValidationResult RequireColumns(ResolvedTarget target, string code)
    {
        if (!target.Entry.Supports(typeof(IColumned)))
        {
            return ValidationResult.Fail(
                "'" + code + "' is a " + target.Entry.Definition.Type + " and has no columns to check.");
        }

        var extra = target.Entry.Definition.Extra;
        var declared = extra is not null
            && extra.TryGetValue("columns", out var columns)
            && columns.ValueKind == JsonValueKind.Array
            && columns.GetArrayLength() > 0;

        if (!declared)
        {
            return ValidationResult.Fail(
                "'check " + code + " columns' has nothing to compare against: '" + code +
                "' declares no 'columns' in its page file. Re-run 'analyse' for the page, or add them by hand.");
        }

        return ValidationResult.Ok(target);
    }

    private static ValidationResult RequireState(string property, ResolvedTarget target, string code)
    {
        // Editability belongs to a grid as a whole, required and read-only to a field's value, so
        // each is answered by the element types that actually have it.
        var (capability, description) = property switch
        {
            "required" or "readonly" => (typeof(IStateAware), "required/readonly state"),
            "editable" => (typeof(IEditableAware), "an editable state"),
            _ => (null, string.Empty),
        };

        if (capability is null)
        {
            return ValidationResult.Fail(
                "'check " + code + " is ...' supports 'required', 'readonly' and 'editable', not '" +
                property + "'.");
        }

        if (!target.Entry.Supports(capability))
        {
            return ValidationResult.Fail(
                "'" + code + "' is a " + target.Entry.Definition.Type + " and does not expose " +
                description + ".");
        }

        return ValidationResult.Ok(target);
    }
}
