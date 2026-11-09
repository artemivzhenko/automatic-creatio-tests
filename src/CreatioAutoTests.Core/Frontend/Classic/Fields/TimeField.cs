namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>Classic time-only control (<c>TimeEdit</c> on a Time column). Returns <c>0001-01-01T07:36:00</c>.</summary>
[UiElement("Time", Generation = UiGeneration.ClassicUI, Controls = new[] { "TimeEdit" },
    ValueKind = ValueKind.DateTime, Options = typeof(ClassicElementOptions))]
public sealed class TimeField : ClassicDateTimeFieldBase
{
    /// <summary>Initializes a Classic time field.</summary>
    public TimeField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Drops the date because the control stores only a time of day.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(1, 1, 1, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);

    /// <summary>Writes the time part in the page's culture.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        DateTime? time = string.IsNullOrWhiteSpace(value) ? null : ParseIso(value);
        await WritePartAsync(ValueLocator, time, timePart: true).ConfigureAwait(false);
    }
}
