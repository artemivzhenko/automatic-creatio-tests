using System.Globalization;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Base for numeric controls (crt-number-input). Values cross the DSL boundary as invariant
/// strings; a concrete type only decides whether a fractional part is allowed.
/// </summary>
public abstract class NumberFieldBase : FieldElementBase
{
    /// <summary>Initializes numeric field behavior for an integer or decimal implementation.</summary>
    protected NumberFieldBase(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Freedom UI host tag shared by integer and decimal controls.</summary>
    protected override string HostTag => "crt-number-input";

    /// <summary>Angular Material input that renders the numeric value.</summary>
    protected override ILocator ValueLocator => Root.Locator(MaterialInputSelector);

    /// <summary>False for integer controls, which reject a fractional value outright.</summary>
    protected abstract bool AllowsFraction { get; }

    /// <summary>Reads a number and returns its invariant string representation.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var raw = await ValueLocator.InputValueAsync().ConfigureAwait(false);
        var parsed = Parse(raw);

        return parsed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Validates and writes an invariant numeric value, honoring fractional support.</summary>
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

        var parsed = Parse(value);
        if (parsed is null)
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' expects a number but received '" + value + "'.");
        }

        if (!AllowsFraction && parsed.Value != decimal.Truncate(parsed.Value))
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' is an integer field and cannot accept '" + value + "'.");
        }

        await ValueLocator.FillAsync(parsed.Value.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
    }

    /// <summary>Accepts the invariant form first, then the machine culture, then gives up.</summary>
    protected static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
        {
            return current;
        }

        return null;
    }
}
