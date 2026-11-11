using System.Globalization;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Inserts a record over OData, as a user would through a page: the object's listeners and the database's
/// triggers react the same way. The Id becomes <c>$alias</c>; the record is deleted when the case ends,
/// passed or not, unless the line says <c>keep</c>.
///   insert UsrOrder as Order set UsrName=Listener bed;UsrAmount=500
/// </summary>
[Command("insert", Validator = typeof(InsertValidator), Usage = "insert <object> as <alias> [keep] set Column=Value;...")]
public sealed class InsertCommand : ICommand
{
    /// <summary>Inserts the record and binds its Id to the alias.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var keep = DataShapes.Is(context.Command, 3, "keep");
        var values = context.Assignments(context.Command.Arguments.Skip(keep ? 5 : 4));
        var id = await context.Session.InsertRecordAsync(context.Command.ValueAt(0)!, Resolve(context, values), keep, cancellationToken)
            .ConfigureAwait(false);
        context.Session.Variables[CaseVariables.NameOf(context.Command.ValueAt(2)!)] = id;
    }

    /// <summary>A value naming a static value is written as that value, as in the filters.</summary>
    internal static IReadOnlyList<RecordAssignment> Resolve(CommandContext context, IReadOnlyList<RecordAssignment> values) =>
        values.Select(v => context.Session.StaticValues.TryGetValue(v.Value, out var s) ? v with { Value = s } : v).ToArray();
}

/// <summary>Changes columns of a record over OData: <c>update UsrOrder $Order set UsrAmount=5000</c>.</summary>
[Command("update", Validator = typeof(UpdateValidator), Usage = "update <object> $alias|<guid> set Column=Value;...")]
public sealed class UpdateCommand : ICommand
{
    /// <summary>Patches the record.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Session.UpdateRecordAsync(
            context.Command.ValueAt(0)!,
            context.Substitute(context.Command.ValueAt(1)!),
            InsertCommand.Resolve(context, context.Assignments(context.Command.Arguments.Skip(3))),
            cancellationToken);
}

/// <summary>Deletes a record over OData: <c>delete UsrOrder $Order</c>. The alias leaves the cleanup list.</summary>
[Command("delete", Validator = typeof(DeleteValidator), Usage = "delete <object> $alias|<guid>")]
public sealed class DeleteCommand : ICommand
{
    /// <summary>Deletes the record.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Session.DeleteRecordAsync(context.Command.ValueAt(0)!, context.Substitute(context.Command.ValueAt(1)!), cancellationToken);
}

/// <summary>
/// Deletes every record matching a filter, as the case's user - the records a page created, which the case
/// has no alias for. Refuses a filter matching more than <see cref="DataShapes.DeleteLimit"/> records and one
/// matching none.
///   delete-record UsrOrder UsrName=Bed 1
/// </summary>
[Command("delete-record", Validator = typeof(DeleteRecordValidator), Usage = "delete-record <object> Column=Value;...")]
public sealed class DeleteRecordCommand : ICommand
{
    /// <summary>Reads the matching Ids and deletes each.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        var filter = CheckRecordCommand.BuildFilter(context.Assignments(context.Command.Arguments.Skip(1)), context.Session.StaticValues);
        var rows = await context.Session.QueryRecordsAsync(entity, new[] { "Id" }, filter, DataShapes.DeleteLimit + 1, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + ": no record to delete.");
        }

        if (rows.Count > DataShapes.DeleteLimit)
        {
            var count = await context.Session.CountRecordsAsync(entity, filter, cancellationToken).ConfigureAwait(false);
            throw new CommandFailedException("'" + entity + "' where " + filter + " matches " + count + " records; 'delete-record' deletes at most " +
                                             DataShapes.DeleteLimit + ". Narrow the filter.");
        }

        foreach (var row in rows)
        {
            await context.Session.DeleteRecordAsync(entity, row["Id"], cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Keeps the value an element holds, as text, in a case variable: <c>remember UsrNumber as $first</c>.
/// </summary>
[Command("remember", Validator = typeof(RememberValidator), SupportedTargets = new[] { typeof(IValueElement) },
    Usage = "remember <code> as $name")]
public sealed class RememberCommand : ICommand
{
    /// <summary>Reads the element's value into the variable.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var value = await context.Target<IValueElement>().GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        context.Session.Variables[CaseVariables.NameOf(context.Command.ValueAt(2)!)] = value ?? string.Empty;
    }
}

/// <summary>
/// Keeps the Id of the one record matching a filter in a case variable - a record a page or a process
/// created: <c>remember-record UsrOrder UsrName=Bed 1 as $order</c>. No match and several matches both fail.
/// </summary>
[Command("remember-record", Validator = typeof(RememberRecordValidator), Usage = "remember-record <object> Column=Value;... as $name")]
public sealed class RememberRecordCommand : ICommand
{
    /// <summary>Reads the Id into the variable.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        var asAt = DataShapes.LastIndexOf(context.Command, "as");
        var filter = CheckRecordCommand.BuildFilter(context.Assignments(context.Command.Arguments.Skip(1).Take(asAt - 1)), context.Session.StaticValues);
        var rows = await context.Session.QueryRecordsAsync(entity, new[] { "Id" }, filter, 2, cancellationToken).ConfigureAwait(false);

        if (rows.Count != 1)
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + (rows.Count == 0 ? ": no such record." : ": more than one record; the filter must name one."));
        }

        context.Session.Variables[CaseVariables.NameOf(context.Command.ValueAt(asAt + 1)!)] = rows[0]["Id"];
    }
}

/// <summary>Counts the matching records: <c>check-count UsrOrderLog UsrOrder=$Order eq 1</c>.</summary>
[Command("check-count", Validator = typeof(CountCheckValidator), Usage = "check-count <object> Column=Value;... eq|ne|gt|ge|lt|le <number>")]
public sealed class CheckCountCommand : ICommand
{
    /// <summary>Counts over OData and compares.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        var count = context.Command.ArgumentCount;
        var filter = CheckRecordCommand.BuildFilter(context.Assignments(context.Command.Arguments.Skip(1).Take(count - 3)), context.Session.StaticValues);
        var op = context.Command.ValueAt(count - 2)!.ToLowerInvariant();
        var expected = context.Substitute(context.Command.ValueAt(count - 1)!);

        var actual = (await context.Session.CountRecordsAsync(entity, filter, cancellationToken).ConfigureAwait(false))
            .ToString(CultureInfo.InvariantCulture);
        if (!ValueComparer.Satisfies(ValueKind.Number, actual, expected, op))
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + " has " + actual + " record(s), which is not " + op + " " + expected + ".");
        }
    }
}

/// <summary>
/// No two matching records share the column's value - two records saved at once got different numbers:
/// <c>check-distinct UsrOrder UsrNumber where UsrName=Bed</c>. Reads at most <see cref="Limit"/> records.
/// </summary>
[Command("check-distinct", Validator = typeof(DistinctCheckValidator), Usage = "check-distinct <object> <column> where Column=Value;...")]
public sealed class CheckDistinctCommand : ICommand
{
    /// <summary>How many records are compared at most.</summary>
    public const int Limit = 5000;

    /// <summary>Reads the column of every matching record and reports the repeated values.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        var column = context.Command.ValueAt(1)!;
        var filter = CheckRecordCommand.BuildFilter(context.Assignments(context.Command.Arguments.Skip(3)), context.Session.StaticValues);
        var rows = await context.Session.QueryRecordsAsync(entity, new[] { column }, filter, Limit, cancellationToken).ConfigureAwait(false);

        var repeated = rows.GroupBy(r => r[column], StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => "\"" + g.Key + "\" x" + g.Count()).ToArray();
        if (repeated.Length > 0)
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + ": " + column + " repeats in " + rows.Count + " record(s): " +
                                             string.Join(", ", repeated.Take(10)) + ".");
        }
    }
}
