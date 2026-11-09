namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>Classic date-only control (<c>DateEdit</c> on a Date column). Returns <c>2025-11-30T00:00:00</c>.</summary>
[UiElement("Date", Generation = UiGeneration.ClassicUI, Controls = new[] { "DateEdit" },
    ValueKind = ValueKind.DateTime, Options = typeof(ClassicElementOptions))]
public sealed class DateField : ClassicDateTimeFieldBase
{
    /// <summary>Initializes a Classic date field.</summary>
    public DateField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Drops the time because the control stores a calendar date only.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Writes the date part in the page's culture.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        DateTime? date = string.IsNullOrWhiteSpace(value) ? null : Normalize(ParseIso(value));
        await WritePartAsync(ValueLocator, date, timePart: false).ConfigureAwait(false);
    }
}
