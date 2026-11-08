using System.Globalization;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Execution;

/// <summary>
/// The one place a DSL comparison is evaluated. <c>check-value</c> (a field on a page) and
/// <c>check-result</c> (a parameter of a process) share it, so a number compares as a number and a
/// date as a date whichever side of the browser the value came from.
/// </summary>
public static class ValueComparer
{
    /// <summary>Evaluates <paramref name="actual"/> <paramref name="op"/> <paramref name="expected"/> for the given kind.</summary>
    public static bool Satisfies(ValueKind kind, string actual, string expected, string op) => kind switch
    {
        ValueKind.Number => CompareNumbers(actual, expected, op),
        ValueKind.DateTime => CompareDates(actual, expected, op),
        _ => CompareText(actual, expected, op),
    };

    private static bool CompareText(string actual, string expected, string op) => op switch
    {
        "eq" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        "ne" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        "startswith" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
        "endswith" => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
        _ => throw new CommandFailedException("Operator '" + op + "' does not apply to text.")
    };

    private static bool CompareNumbers(string actual, string expected, string op)
    {
        var left = ParseNumber(actual);
        var right = ParseNumber(expected);

        if (left is null || right is null)
        {
            // An empty numeric value can still be compared for equality with an empty expectation.
            return CompareText(actual, expected, op is "eq" or "ne" ? op : "eq");
        }

        return Compare(left.Value.CompareTo(right.Value), op);
    }

    private static bool CompareDates(string actual, string expected, string op)
    {
        var left = IsoDateTime.Parse(actual);
        var right = IsoDateTime.Parse(expected);

        if (left is null || right is null)
        {
            return CompareText(actual, expected, op is "eq" or "ne" ? op : "eq");
        }

        return Compare(left.Value.CompareTo(right.Value), op);
    }

    private static bool Compare(int comparison, string op) => op switch
    {
        "eq" => comparison == 0,
        "ne" => comparison != 0,
        "gt" => comparison > 0,
        "ge" => comparison >= 0,
        "lt" => comparison < 0,
        "le" => comparison <= 0,
        _ => throw new CommandFailedException("Operator '" + op + "' does not apply to ordered values.")
    };

    private static decimal? ParseNumber(string text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
}
