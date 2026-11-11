using System.Globalization;

namespace CreatioAutoTests.Execution;

/// <summary>
/// How a <c>Column=Value</c> pair is typed for OData, in one place for the filters of the checks and the
/// bodies of the writes. The literal's form decides: a GUID, <c>true</c>/<c>false</c> and a number go
/// unquoted, everything else is a string; a lookup column given a GUID is addressed by its <c>Id</c> suffix.
/// </summary>
public static class ODataValues
{
    /// <summary>The column as OData names it for this value (<c>Owner</c> + a GUID is <c>OwnerId</c>).</summary>
    public static string ColumnFor(string column, string value) =>
        Guid.TryParse(value, out _) && !column.EndsWith("Id", StringComparison.Ordinal) ? column + "Id" : column;

    /// <summary>The value as a filter literal: <c>OwnerId eq 1f…</c>, <c>UsrAmount eq 10</c>, <c>UsrName eq 'x'</c>.</summary>
    public static string FilterLiteral(string value) =>
        IsUnquoted(value) ? value : "'" + value.Replace("'", "''") + "'";

    /// <summary>The value as a JSON body value: a GUID stays a string, a boolean and a number are not quoted.</summary>
    public static object? JsonValue(string value)
    {
        if (Guid.TryParse(value, out _))
        {
            return value;
        }

        if (value is "true" or "false")
        {
            return value == "true";
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value;
    }

    /// <summary>The JSON body of an insert or an update.</summary>
    public static Dictionary<string, object?> Body(IEnumerable<RecordAssignment> assignments) =>
        assignments.ToDictionary(a => ColumnFor(a.Column, a.Value), a => JsonValue(a.Value), StringComparer.Ordinal);

    private static bool IsUnquoted(string value) =>
        Guid.TryParse(value, out _) || value is "true" or "false" ||
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
}
