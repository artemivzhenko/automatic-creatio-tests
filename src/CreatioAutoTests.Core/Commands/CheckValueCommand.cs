using System.Globalization;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Compares what an element holds with an expected value. The comparison is driven by the value
/// kind the validator already determined, so numbers and dates compare as numbers and dates while
/// everything else compares as text.
/// </summary>
[Command("check-value",
    Validator = typeof(ComparisonValidator),
    SupportedTargets = new[] { typeof(IValueElement) },
    Usage = "check-value <code> eq|ne|gt|ge|lt|le|contains|startswith|endswith \"expected\"")]
public sealed class CheckValueCommand : ICommand
{
    /// <summary>Compares a target value using the static kind recorded during validation.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var element = context.Target<IValueElement>();
        var operatorText = context.Command.ValueAt(1)!.ToLowerInvariant();
        var expected = context.ResolveValue(2);

        var actual = await element.GetValueAsStringAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        var satisfied = context.KindOf() switch
        {
            ValueKind.Number => CompareNumbers(actual, expected, operatorText),
            ValueKind.DateTime => CompareDates(actual, expected, operatorText),
            _ => CompareText(actual, expected, operatorText)
        };

        if (!satisfied)
        {
            throw new CommandFailedException(
                "'" + element.Code + "' holds \"" + actual + "\" which is not " + operatorText +
                " \"" + expected + "\".");
        }
    }

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
            // An empty numeric field can still be compared for equality with an empty expectation.
            return CompareText(actual, expected, op is "eq" or "ne" ? op : "eq");
        }

        return Compare(left.Value.CompareTo(right.Value), op);
    }

    private static bool CompareDates(string actual, string expected, string op)
    {
        var left = ParseDate(actual);
        var right = ParseDate(expected);

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

    private static DateTime? ParseDate(string text) => IsoDateTime.Parse(text);
}
