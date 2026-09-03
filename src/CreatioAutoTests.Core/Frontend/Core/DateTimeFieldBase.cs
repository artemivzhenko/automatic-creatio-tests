using System.Globalization;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Base for date and time controls (crt-datetimepicker). The DSL always speaks ISO 8601
/// (yyyy-MM-ddTHH:mm:ss); each concrete type maps that onto the format Creatio renders and
/// normalises the part it does not store.
/// </summary>
public abstract class DateTimeFieldBase : FieldElementBase
{
    /// <summary>Initializes date-family behavior that converts between ISO and the rendered format.</summary>
    protected DateTimeFieldBase(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Freedom UI host tag shared by date, time, and date-time picker controls.</summary>
    protected override string HostTag => "crt-datetimepicker";

    /// <summary>Angular Material input that renders the picker value.</summary>
    protected override ILocator ValueLocator => Root.Locator(MaterialInputSelector);

    /// <summary>Format written into the control, for example MM/dd/yyyy.</summary>
    protected abstract string UiFormat { get; }

    /// <summary>Formats accepted when reading the control back.</summary>
    protected abstract string[] UiPatterns { get; }

    /// <summary>Drops the component this control does not store, so a round trip is stable.</summary>
    protected abstract DateTime Normalize(DateTime value);

    /// <summary>Reads the rendered date-family value and returns its normalized ISO representation.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var raw = await ValueLocator.InputValueAsync().ConfigureAwait(false);
        var parsed = ParseFromUi(raw);

        return parsed is null ? string.Empty : IsoDateTime.ToIso(parsed.Value);
    }

    /// <summary>Writes an ISO value in the concrete control's UI format.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            await ValueLocator.FillAsync(string.Empty).ConfigureAwait(false);
            return;
        }

        if (!IsoDateTime.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' expects one of " + IsoDateTime.DescribeAcceptedFormats() +
                " but received '" + value + "'.");
        }

        var formatted = Normalize(parsed).ToString(UiFormat, CultureInfo.InvariantCulture);
        await ValueLocator.FillAsync(formatted).ConfigureAwait(false);
    }

    /// <summary>Reads the rendered value, preferring the exact UI patterns over a loose parse.</summary>
    protected DateTime? ParseFromUi(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        foreach (var pattern in UiPatterns)
        {
            if (DateTime.TryParseExact(
                    text,
                    pattern,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var exact))
            {
                return Normalize(exact);
            }
        }

        if (DateTime.TryParse(
                text,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var loose))
        {
            return Normalize(loose);
        }

        return null;
    }
}
