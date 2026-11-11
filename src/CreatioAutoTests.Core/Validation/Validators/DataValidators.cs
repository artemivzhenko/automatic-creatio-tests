using System.Globalization;
using System.Text.RegularExpressions;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'insert':
///   insert UsrOrder as Order set UsrName=Bed;UsrAmount=500
///   insert UsrOrder as Order keep set UsrName=Bed        - the record outlives the case
/// The alias becomes <c>$Order</c>; the record is deleted when the case ends unless kept.
/// </summary>
public sealed class InsertValidator : ICommandValidator
{
    /// <summary>Validates the object, the alias, the optional keep and the assignments.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        const string usage = "'insert <object> as <alias> [keep] set Column=Value;...'";
        if (command.ArgumentCount < 5 || !DataShapes.Is(command, 1, "as"))
        {
            return ValidationResult.Fail("'insert' reads " + usage + ".");
        }

        var alias = CaseVariables.NameOf(command.ValueAt(2)!);
        if (!DataShapes.IsName(alias))
        {
            return ValidationResult.Fail("'" + command.ValueAt(2) + "' is not an alias: letters, digits and '_', starting with a letter.");
        }

        var keep = DataShapes.Is(command, 3, "keep");
        var setAt = keep ? 4 : 3;
        if (!DataShapes.Is(command, setAt, "set"))
        {
            return ValidationResult.Fail("'insert' reads " + usage + "; expected 'set' after the alias.");
        }

        return DataShapes.Pairs("insert", command.Arguments.Skip(setAt + 1).ToArray())
               ?? ValidationResult.Ok().WithVariable(new VariableEffect(alias, Inserts: true, Kept: keep));
    }
}

/// <summary>
/// Shape of 'update':
///   update UsrOrder $Order set UsrAmount=5000
///   update UsrOrder 0c48c818-08f0-4f70-9b58-be512227f3bf set UsrAmount=5000
/// </summary>
public sealed class UpdateValidator : ICommandValidator
{
    /// <summary>Validates the object, the record reference and the assignments.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 4 || !DataShapes.Is(command, 2, "set"))
        {
            return ValidationResult.Fail("'update' reads 'update <object> $alias|<guid> set Column=Value;...'.");
        }

        return DataShapes.IdReference("update", command.Arguments[1])
               ?? DataShapes.Pairs("update", command.Arguments.Skip(3).ToArray())
               ?? ValidationResult.Ok();
    }
}

/// <summary>
/// Shape of 'delete':
///   delete UsrOrder $Order
/// A deleted alias leaves the cleanup list, and counts as deleted for '# writes'.
/// </summary>
public sealed class DeleteValidator : ICommandValidator
{
    /// <summary>Validates the object and the record reference.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 2)
        {
            return ValidationResult.Fail("'delete' reads 'delete <object> $alias|<guid>'.");
        }

        return DataShapes.IdReference("delete", command.Arguments[1])
               ?? ValidationResult.Ok().WithVariable(new VariableEffect(null, Deletes: DataShapes.AliasIn(command.ValueAt(1)!)));
    }
}

/// <summary>
/// Shape of 'delete-record': every record matching a filter, at most <see cref="DataShapes.DeleteLimit"/>.
///   delete-record UsrOrder Id=$Order
///   delete-record UsrOrderLog UsrOrder=$Order;UsrKind=Big
/// </summary>
public sealed class DeleteRecordValidator : ICommandValidator
{
    /// <summary>Validates the object and the filter.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 2)
        {
            return ValidationResult.Fail("'delete-record' reads 'delete-record <object> Column=Value;...'.");
        }

        var filter = command.Arguments.Skip(1).ToArray();
        if (DataShapes.Pairs("delete-record", filter) is { } error)
        {
            return error;
        }

        // 'Id=$Order' deletes the aliased record: for '# writes' that is the same as 'delete <object> $Order'.
        var text = RecordAssignments.Rejoin(filter);
        var byId = Regex.Match(text, @"^\s*Id\s*=\s*\$([A-Za-z_]\w*)\s*$");
        return ValidationResult.Ok().WithVariable(new VariableEffect(null, Deletes: byId.Success ? byId.Groups[1].Value : null));
    }
}

/// <summary>
/// Shape of 'remember': the value of an element, as text, into a case variable.
///   remember UsrNumber as $first
/// </summary>
public sealed class RememberValidator : ICommandValidator
{
    /// <summary>Validates the element, which must hold a value, and the name.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 3 || !DataShapes.Is(command, 1, "as"))
        {
            return ValidationResult.Fail("'remember' reads 'remember <code> as $name'.");
        }

        var name = CaseVariables.NameOf(command.ValueAt(2)!);
        if (!DataShapes.IsName(name))
        {
            return ValidationResult.Fail("'" + command.ValueAt(2) + "' is not a variable name: '$' and letters, digits, '_'.");
        }

        var code = command.ValueAt(0)!;
        if (!context.TryResolve(code, out var target, out var error))
        {
            return ValidationResult.Fail("'remember' cannot run: " + error + ".");
        }

        if (target.Entry.ValueKind == ValueKind.None)
        {
            return ValidationResult.Fail("'" + code + "' is a " + target.Entry.Definition.Type + " and has no value to remember.");
        }

        return ValidationResult.Ok(target).WithVariable(new VariableEffect(name));
    }
}

/// <summary>
/// Shape of 'remember-record': the Id of the one record matching a filter, into a case variable.
///   remember-record UsrOrder UsrName=Bed 1 as $order
/// </summary>
public sealed class RememberRecordValidator : ICommandValidator
{
    /// <summary>Validates the object, the filter and the name.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        var asAt = DataShapes.LastIndexOf(command, "as");
        if (command.ArgumentCount < 4 || asAt != command.ArgumentCount - 2 || asAt < 2)
        {
            return ValidationResult.Fail("'remember-record' reads 'remember-record <object> Column=Value;... as $name'.");
        }

        var name = CaseVariables.NameOf(command.ValueAt(asAt + 1)!);
        if (!DataShapes.IsName(name))
        {
            return ValidationResult.Fail("'" + command.ValueAt(asAt + 1) + "' is not a variable name: '$' and letters, digits, '_'.");
        }

        return DataShapes.Pairs("remember-record", command.Arguments.Skip(1).Take(asAt - 1).ToArray())
               ?? ValidationResult.Ok().WithVariable(new VariableEffect(name));
    }
}

/// <summary>
/// Shape of 'check-count':
///   check-count UsrOrderLog UsrOrder=$Order eq 1
///   check-count UsrOrder UsrName=Bed ge $before
/// </summary>
public sealed class CountCheckValidator : ICommandValidator
{
    /// <summary>Validates the object, the filter, the operator and the number.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 4)
        {
            return ValidationResult.Fail("'check-count' reads 'check-count <object> Column=Value;... eq|ne|gt|ge|lt|le <number>'.");
        }

        var op = command.ValueAt(command.ArgumentCount - 2)!.ToLowerInvariant();
        if (!DataShapes.OrderOperators.Contains(op))
        {
            return ValidationResult.Fail("'check-count' compares with eq, ne, gt, ge, lt or le; found '" + op + "'.");
        }

        var number = command.Arguments[^1];
        if (!long.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            !(Regex.IsMatch(number.Value, @"^\$[A-Za-z_]\w*$") && !number.IsQuoted))
        {
            return ValidationResult.Fail("'check-count' needs a whole number or a '$name' to compare with; found '" + number.Value + "'.");
        }

        return DataShapes.Pairs("check-count", command.Arguments.Skip(1).Take(command.ArgumentCount - 3).ToArray()) ?? ValidationResult.Ok();
    }
}

/// <summary>
/// Shape of 'check-distinct': no two matching records share the column's value.
///   check-distinct UsrOrder UsrNumber where UsrName=Bed
/// </summary>
public sealed class DistinctCheckValidator : ICommandValidator
{
    /// <summary>Validates the object, the column and the filter after 'where'.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount < 4 || !DataShapes.Is(command, 2, "where"))
        {
            return ValidationResult.Fail("'check-distinct' reads 'check-distinct <object> <column> where Column=Value;...'.");
        }

        return DataShapes.Pairs("check-distinct", command.Arguments.Skip(3).ToArray()) ?? ValidationResult.Ok();
    }
}

/// <summary>The shared pieces of the data commands' shapes; the commands read their lines with the same helpers.</summary>
public static class DataShapes
{
    /// <summary>'delete-record' refuses a filter matching more records than this: a wide filter is a mistake, not a cleanup.</summary>
    public const int DeleteLimit = 20;

    /// <summary>Operators of an ordered comparison (a count, a number).</summary>
    public static readonly IReadOnlySet<string> OrderOperators = new HashSet<string> { "eq", "ne", "gt", "ge", "lt", "le" };

    /// <summary>Operators of 'check-record ... has': the ordered ones and the text ones.</summary>
    public static readonly IReadOnlySet<string> ValueOperators =
        new HashSet<string> { "eq", "ne", "gt", "ge", "lt", "le", "contains", "startswith", "endswith" };

    /// <summary>Whether the argument at <paramref name="index"/> is the given unquoted word.</summary>
    public static bool Is(ParsedCommand command, int index, string word) =>
        command.ArgumentAt(index) is { IsQuoted: false } token && string.Equals(token.Value, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Position of the last unquoted <paramref name="word"/>, or -1.</summary>
    public static int LastIndexOf(ParsedCommand command, string word)
    {
        for (var i = command.ArgumentCount - 1; i >= 0; i--)
        {
            if (Is(command, i, word))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A variable or alias name.</summary>
    public static bool IsName(string name) => Regex.IsMatch(name, @"^[A-Za-z_]\w*$");

    /// <summary>The alias of a <c>$alias</c> reference, or null for a GUID.</summary>
    public static string? AliasIn(string reference) => reference.StartsWith('$') ? CaseVariables.NameOf(reference) : null;

    /// <summary>A record reference is a <c>$alias</c> or a GUID; null when it is one.</summary>
    public static ValidationResult? IdReference(string keyword, CommandToken token) =>
        !token.IsQuoted && (Guid.TryParse(token.Value, out _) || Regex.IsMatch(token.Value, @"^\$[A-Za-z_]\w*$"))
            ? null
            : ValidationResult.Fail("'" + keyword + "' needs the record as '$alias' or a GUID; found '" + token.Value + "'.");

    /// <summary>A <c>Column=Value;...</c> list that parses; null when it does.</summary>
    public static ValidationResult? Pairs(string keyword, IReadOnlyList<CommandToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return ValidationResult.Fail("'" + keyword + "' needs 'Column=Value;Column=Value'.");
        }

        return RecordAssignments.TryParse(RecordAssignments.Rejoin(tokens), out _, out var error)
            ? null
            : ValidationResult.Fail("'" + keyword + "' cannot be read: " + error + ".");
    }
}
