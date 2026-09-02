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

    public static DateTime? Parse(string? text) => TryParse(text, out var value) ? value : null;

    public static string ToIso(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>Formats accepted on input, for error messages.</summary>
    public static string DescribeAcceptedFormats() => string.Join(", ", AcceptedFormats);
}
