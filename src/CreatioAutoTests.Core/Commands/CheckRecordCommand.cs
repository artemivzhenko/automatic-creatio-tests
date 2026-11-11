using System.Globalization;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Asserts that a record matching a filter exists, or does not, by reading the object over OData.
/// This is how a process case proves a side effect: the record the process was meant to create,
/// change or delete.
/// </summary>
[Command("check-record",
    Validator = typeof(RecordCheckValidator),
    Usage = "check-record <object> Column=Value;Column=Value [not] exists | check-record <object> <filter> has <column> <op> <value>")]
public sealed class CheckRecordCommand : ICommand
{
    /// <summary>Queries the object with the filter and compares the outcome with the expectation.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        if (RecordCheckValidator.HasForm(context.Command, out var hasFilter, out var column, out var op, out var valueToken))
        {
            await CheckValueAsync(context, entity, BuildFilter(context.Assignments(hasFilter), context.Session.StaticValues),
                column, op, valueToken, cancellationToken).ConfigureAwait(false);
            return;
        }

        var expected = RecordCheckValidator.Expectation(context.Command, out var filterTokens)!.Value;
        var filter = BuildFilter(context.Assignments(filterTokens), context.Session.StaticValues);

        var found = await context.Session.RecordExistsAsync(entity, filter, cancellationToken).ConfigureAwait(false);
        context.Logger.LogDebug("{Entity} where {Filter}: {Found}.", entity, filter, found ? "found" : "none");

        if (found != expected)
        {
            throw new CommandFailedException(
                "'" + entity + "' where " + filter + (expected ? " has no record." : " has a record but none was expected."));
        }
    }

    /// <summary>
    /// 'has': reads the column of the matching record and compares it. A number compares as a number and a date
    /// as a date, judged by the expected value; a GUID reads the lookup's <c>Id</c> column.
    /// </summary>
    private static async Task CheckValueAsync(CommandContext context, string entity, string filter, string column, string op,
        CommandToken valueToken, CancellationToken cancellationToken)
    {
        var expected = valueToken.IsQuoted ? valueToken.Value : context.Substitute(valueToken.Value);
        expected = !valueToken.IsQuoted && context.Session.StaticValues.TryGetValue(expected, out var staticValue) ? staticValue : expected;
        var select = ODataValues.ColumnFor(column, expected);

        var rows = await context.Session.QueryRecordsAsync(entity, new[] { select }, filter, 1, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + ": no such record.");
        }

        var actual = rows[0][select];
        var kind = decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
                   decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            ? Frontend.ValueKind.Number
            : Frontend.IsoDateTime.Parse(actual) is not null && Frontend.IsoDateTime.Parse(expected) is not null
                ? Frontend.ValueKind.DateTime
                : Frontend.ValueKind.Text;

        if (!ValueComparer.Satisfies(kind, actual, expected, op))
        {
            throw new CommandFailedException("'" + entity + "' where " + filter + " has " + select + " = \"" + actual + "\", which is not " + op + " \"" + expected + "\".");
        }
    }

    /// <summary>
    /// Builds the OData filter. The literal's form decides its type: a GUID, <c>true</c>/<c>false</c>
    /// and a number go unquoted, everything else is a string. A lookup column is addressed by its
    /// <c>Id</c> suffix when a GUID is given.
    /// </summary>
    public static string BuildFilter(IReadOnlyList<RecordAssignment> assignments, IReadOnlyDictionary<string, string> staticValues)
    {
        return string.Join(" and ", assignments.Select(assignment =>
        {
            var value = staticValues.TryGetValue(assignment.Value, out var staticValue) ? staticValue : assignment.Value;
            return ODataValues.ColumnFor(assignment.Column, value) + " eq " + ODataValues.FilterLiteral(value);
        }));
    }
}
