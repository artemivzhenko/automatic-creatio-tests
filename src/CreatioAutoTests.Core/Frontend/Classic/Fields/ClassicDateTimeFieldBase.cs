using System.Text.Json;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Base for the Classic date family. The DSL speaks ISO 8601; the control shows the user's culture
/// (<c>d.m.Y</c> and <c>G:i</c> in uk-UA). Reads take the view model's Date and come back as local
/// ISO text. Writes ask the page itself to render the culture form through <c>Ext.Date.format</c>
/// with <c>Terrasoft.Resources.CultureSettings</c>, so no date format is ever hard-coded here.
/// </summary>
public abstract class ClassicDateTimeFieldBase : ClassicFieldBase
{
    /// <summary>Initializes a Classic date-family field.</summary>
    protected ClassicDateTimeFieldBase(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Drops the component this control does not store, so a round trip is stable.</summary>
    protected abstract DateTime Normalize(DateTime value);

    /// <summary>Reads the model's date as normalised ISO text; empty when the column is empty.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var model = await ReadModelValueAsync().ConfigureAwait(false);
        if (model.Kind == "date" && IsoDateTime.TryParse(model.Text, out var value))
        {
            return IsoDateTime.ToIso(Normalize(value));
        }

        return string.Empty;
    }

    /// <summary>Parses the ISO value the DSL supplied, or reports the accepted formats.</summary>
    protected DateTime ParseIso(string value)
    {
        if (!IsoDateTime.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' expects one of " + IsoDateTime.DescribeAcceptedFormats() +
                " but received '" + value + "'.");
        }

        return parsed;
    }

    /// <summary>
    /// Renders a value the way the page would show it, in the user's culture, for the date or the
    /// time part. The page knows its culture; the framework does not have to.
    /// </summary>
    protected async Task<string> FormatInPageCultureAsync(DateTime value, bool timePart)
    {
        const string script = @"(c, [iso, time]) => {
            const d = new Date(iso);
            const cs = Terrasoft.Resources.CultureSettings;
            return Ext.Date.format(d, time ? cs.timeFormat : cs.dateFormat);
        }";

        var result = await EvaluateOnComponentAsync(script, new object[] { IsoDateTime.ToIso(value), timePart })
            .ConfigureAwait(false);

        if (result.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "The page could not format a " + (timePart ? "time" : "date") + " for field '" + Code + "'.");
        }

        return result.GetString() ?? string.Empty;
    }

    /// <summary>Writes one part of the value into one control: empty clears it.</summary>
    protected async Task WritePartAsync(ILocator input, DateTime? value, bool timePart)
    {
        var text = value is null ? string.Empty : await FormatInPageCultureAsync(value.Value, timePart).ConfigureAwait(false);
        await FillAndCommitAsync(input, text).ConfigureAwait(false);
    }
}
