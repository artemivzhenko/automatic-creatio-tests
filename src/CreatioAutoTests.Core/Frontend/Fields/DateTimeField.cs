namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Date and time control. Reference implementation of the date family: a sibling type only changes
/// <see cref="UiFormat"/>, <see cref="UiPatterns"/> and what <see cref="Normalize"/> discards.
/// </summary>
[UiElement("DateTime", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class DateTimeField : DateTimeFieldBase
{
    /// <summary>Initializes a picker that retains both calendar date and time of day.</summary>
    public DateTimeField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Creatio display format used when writing a date and time.</summary>
    protected override string UiFormat => "MM/dd/yyyy h:mm tt";

    /// <summary>Supported 12-hour and 24-hour formats used when reading the picker.</summary>
    protected override string[] UiPatterns => new[]
    {
        "MM/dd/yyyy h:mm tt",
        "MM/dd/yyyy hh:mm tt",
        "MM/dd/yyyy H:mm",
        "MM/dd/yyyy HH:mm"
    };

    /// <summary>Seconds are not rendered by Creatio, so they are dropped to keep round trips stable.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
}
