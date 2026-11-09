using System.Text.Json;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'check-row':
///   check-row X Column=Value;Column=Value exists
///   check-row X Column=Value not exists
/// The cell list is parsed here, and the columns it names are checked against the element's
/// <c>columns</c> when the page file declares them.
/// </summary>
public sealed class RowCheckValidator : ICommandValidator
{
    /// <summary>Validates the target, the cell list and the trailing expectation.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 3)
        {
            return ValidationResult.Fail(
                "'check-row' takes a code, cells and an expectation: 'check-row <code> Column=Value;Column=Value [not] exists'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'check-row' cannot run: " + resolveError + ".");
        }

        if (!target.Entry.Supports(typeof(IRowed)))
        {
            return ValidationResult.Fail("'" + code + "' is a " + target.Entry.Definition.Type + " and has no rows.");
        }

        var expectation = RecordCheckValidator.Expectation(command, out var cellTokens);
        if (expectation is null)
        {
            return ValidationResult.Fail("'check-row' must end in 'exists' or 'not exists'.");
        }

        return RowValidation.CheckCells(code, target, cellTokens) ?? ValidationResult.Ok(target);
    }
}

/// <summary>Shape of 'select-row' and 'delete-row': a code followed by the cells that name the row.</summary>
public sealed class RowTargetValidator : ICommandValidator
{
    /// <summary>Validates the target and the cell list.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 2)
        {
            return ValidationResult.Fail(
                "'" + command.Keyword + "' takes a code and the cells that name the row: '" + command.Keyword + " <code> Column=Value;Column=Value'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var resolveError))
        {
            return ValidationResult.Fail("'" + command.Keyword + "' cannot run: " + resolveError + ".");
        }

        if (!target.Entry.Supports(typeof(IRowed)))
        {
            return ValidationResult.Fail("'" + code + "' is a " + target.Entry.Definition.Type + " and has no rows.");
        }

        return RowValidation.CheckCells(code, target, command.Arguments.Skip(1).ToArray()) ?? ValidationResult.Ok(target);
    }
}

/// <summary>Shape of 'on': one configured page, which later lines then resolve against.</summary>
public sealed class OnPageValidator : ICommandValidator
{
    /// <summary>Validates the page name and advances the virtual page.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 1 || string.IsNullOrWhiteSpace(command.ValueAt(0)))
        {
            return ValidationResult.Fail("'on' takes exactly one page name: 'on <page>'.");
        }

        var page = command.ValueAt(0)!;
        if (!context.Catalog.PageExists(page))
        {
            return ValidationResult.Fail(
                "Page '" + page + "' is not configured. Known pages: " + string.Join(", ", context.Catalog.PageNames) + ".");
        }

        return ValidationResult.OkOnPage(page);
    }
}

internal static class RowValidation
{
    /// <summary>Parses the cell list and checks its columns against the declared ones; null when all is well.</summary>
    public static ValidationResult? CheckCells(string code, ResolvedTarget target, IReadOnlyList<CommandToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return ValidationResult.Fail("'" + code + "' needs the cells that name the row: 'Column=Value;Column=Value'.");
        }

        var text = RecordAssignments.Rejoin(tokens);
        if (!RecordAssignments.TryParse(text, out var cells, out var parseError))
        {
            return ValidationResult.Fail("The cells of '" + code + "' cannot be read: " + parseError + ".");
        }

        var extra = target.Entry.Definition.Extra;
        if (extra is null || !extra.TryGetValue("columns", out var declared) || declared.ValueKind != JsonValueKind.Array || declared.GetArrayLength() == 0)
        {
            return null;
        }

        var columns = declared.EnumerateArray().Select(c => c.GetString() ?? string.Empty).Where(c => c.Length > 0).ToArray();
        var unknown = cells.Select(c => c.Column).FirstOrDefault(c => !columns.Contains(c, StringComparer.OrdinalIgnoreCase));

        return unknown is null
            ? null
            : ValidationResult.Fail("'" + code + "' has no column '" + unknown + "'. Its columns are: " + string.Join(", ", columns.Select(c => "'" + c + "'")) + ".");
    }
}
