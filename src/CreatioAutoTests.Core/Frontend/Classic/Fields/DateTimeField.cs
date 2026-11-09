using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic date-and-time column. One element in the schema renders two controls that share the
/// name and the caption: <c>{Name}DateEdit</c> and <c>{Name}TimeEdit</c>, bound to the same column.
/// The date control is the root; a write fills both.
/// </summary>
[UiElement("DateTime", Generation = UiGeneration.ClassicUI, Controls = new[] { "DateEdit", "TimeEdit" },
    ValueKind = ValueKind.DateTime, Options = typeof(ClassicElementOptions))]
public sealed class DateTimeField : ClassicDateTimeFieldBase
{
    /// <summary>Initializes a Classic date-time field.</summary>
    public DateTimeField(ElementRuntimeContext context) : base(context)
    {
    }

    private ILocator DateInput => Page.Locator(ClassicIds.Selector(ClassicIds.Element(Schema, Name, "DateEdit")));

    private ILocator TimeInput => Page.Locator(ClassicIds.Selector(ClassicIds.Element(Schema, Name, "TimeEdit")));

    /// <summary>Both controls bind the same column, so the date control alone identifies the element.</summary>
    protected override string BuildSelector() => ClassicIds.Selector(ClassicIds.Element(Schema, Name, "DateEdit"));

    /// <summary>Seconds are not shown, so they are dropped to keep round trips stable.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);

    /// <summary>Writes the date into the date control and the time into the time control.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            await WritePartAsync(DateInput, null, timePart: false).ConfigureAwait(false);
            return;
        }

        var parsed = Normalize(ParseIso(value));
        await WritePartAsync(DateInput, parsed, timePart: false).ConfigureAwait(false);

        if (await TimeInput.CountAsync().ConfigureAwait(false) > 0)
        {
            await WritePartAsync(TimeInput, parsed, timePart: true).ConfigureAwait(false);
        }
    }
}
