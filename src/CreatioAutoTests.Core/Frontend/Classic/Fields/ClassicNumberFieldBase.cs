using System.Globalization;

namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Base for Classic numeric controls. The DOM shows the number in the user's culture
/// (<c>12,50</c> with a space for thousands in uk-UA), the view model holds the number itself, so
/// reads go to the model and come back invariant. Classic accepts both <c>.</c> and <c>,</c> when
/// typing, so writes send the invariant form.
/// </summary>
public abstract class ClassicNumberFieldBase : ClassicFieldBase
{
    /// <summary>Initializes a Classic number field.</summary>
    protected ClassicNumberFieldBase(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>False for integer controls, which reject a fractional value outright.</summary>
    protected abstract bool AllowsFraction { get; }

    /// <summary>Reads the model's number as invariant text; empty when the column is empty.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var model = await ReadModelValueAsync().ConfigureAwait(false);
        if (model.Kind == "number" && decimal.TryParse(model.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return Invariant(number);
        }

        // The model had no number: fall back to the rendered text, which may carry culture separators.
        var raw = await ValueLocator.InputValueAsync().ConfigureAwait(false);
        var parsed = ParseDisplayed(raw);
        return parsed is null ? string.Empty : Invariant(parsed.Value);
    }

    /// <summary>Validates the invariant number and types it in.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            await FillAndCommitAsync(ValueLocator, string.Empty).ConfigureAwait(false);
            return;
        }

        if (!decimal.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' expects a number but received '" + value + "'.");
        }

        if (!AllowsFraction && parsed != decimal.Truncate(parsed))
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' is an integer field and cannot accept '" + value + "'.");
        }

        await FillAndCommitAsync(ValueLocator, Invariant(parsed)).ConfigureAwait(false);
    }

    /// <summary>Reads a culture-formatted number: spaces and non-breaking spaces group, the last comma or dot is the decimal mark.</summary>
    private static decimal? ParseDisplayed(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        var mark = Math.Max(lastComma, lastDot);
        if (mark >= 0)
        {
            text = text.Substring(0, mark).Replace(",", string.Empty).Replace(".", string.Empty)
                   + "." + text.Substring(mark + 1);
        }

        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
