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
    Usage = "check-record <object> Column=Value;Column=Value [not] exists")]
public sealed class CheckRecordCommand : ICommand
{
    /// <summary>Queries the object with the filter and compares the outcome with the expectation.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var entity = context.Command.ValueAt(0)!;
        var expected = RecordCheckValidator.Expectation(context.Command, out var filterTokens)!.Value;

        RecordAssignments.TryParse(RecordAssignments.Rejoin(filterTokens), out var assignments, out _);
        var filter = BuildFilter(assignments, context.Session.StaticValues);

        var found = await context.Session.RecordExistsAsync(entity, filter, cancellationToken).ConfigureAwait(false);
        context.Logger.LogDebug("{Entity} where {Filter}: {Found}.", entity, filter, found ? "found" : "none");

        if (found != expected)
        {
            throw new CommandFailedException(
                "'" + entity + "' where " + filter + (expected ? " has no record." : " has a record but none was expected."));
        }
    }

    /// <summary>
    /// Builds the OData filter. The literal's form decides its type: a GUID, <c>true</c>/<c>false</c>
    /// and a number go unquoted, everything else is a string. A lookup column is addressed by its
    /// <c>Id</c> suffix when a GUID is given.
    /// </summary>
    public static string BuildFilter(IReadOnlyList<RecordAssignment> assignments, IReadOnlyDictionary<string, string> staticValues)
    {
        var parts = new List<string>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var value = staticValues.TryGetValue(assignment.Value, out var staticValue) ? staticValue : assignment.Value;
            var column = assignment.Column;

            if (Guid.TryParse(value, out _))
            {
                if (!column.EndsWith("Id", StringComparison.Ordinal))
                {
                    column += "Id";
                }

                parts.Add(column + " eq " + value);
            }
            else if (value is "true" or "false")
            {
                parts.Add(column + " eq " + value);
            }
            else if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                parts.Add(column + " eq " + value);
            }
            else
            {
                parts.Add(column + " eq '" + value.Replace("'", "''") + "'");
            }
        }

        return string.Join(" and ", parts);
    }
}
