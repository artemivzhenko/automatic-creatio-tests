using System.Globalization;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// The one date format the DSL speaks. Parsing is deliberately exact: a loose parse would let
/// "30 November" through validation only to behave unpredictably against a real page.
/// </summary>
public static class IsoDateTime
{
    /// <summary>Format values are rendered in when the DSL reads them back.</summary>
    public const string Format = "yyyy-MM-ddTHH:mm:ss";

    private static readonly string[] AcceptedFormats =
    {
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd"
    };

    /// <summary>
    /// Parses a value written in one of the accepted formats. Exact rather than lenient: a loose
    /// parse would accept text no test author meant, and fail confusingly against a real control.
    /// </summary>
    /// <param name="text">Text to read; null, empty and whitespace all mean "no value".</param>
    /// <param name="value">The parsed value, or <c>default</c> when parsing failed.</param>
    /// <returns><see langword="true"/> when the text matched one of the accepted formats.</returns>
    public static bool TryParse(string? text, out DateTime value)
    {
        value = default;

        return !string.IsNullOrWhiteSpace(text) && DateTime.TryParseExact(
            text.Trim(),
            AcceptedFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    /// <summary>Same as <see cref="TryParse"/>, for callers that prefer a null over a flag.</summary>
    /// <param name="text">Text to read.</param>
    /// <returns>The parsed value, or <see langword="null"/> when the text did not match.</returns>
    public static DateTime? Parse(string? text) => TryParse(text, out var value) ? value : null;

    /// <summary>Renders a value in the format the DSL reads back, so a round trip is stable.</summary>
    /// <param name="value">Value to render.</param>
    public static string ToIso(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>Formats accepted on input, for error messages.</summary>
    public static string DescribeAcceptedFormats() => string.Join(", ", AcceptedFormats);
}
