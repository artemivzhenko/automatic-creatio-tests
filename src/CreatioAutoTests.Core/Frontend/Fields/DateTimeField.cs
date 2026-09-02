namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Date and time control. Reference implementation of the date family: a sibling type only changes
/// <see cref="UiFormat"/>, <see cref="UiPatterns"/> and what <see cref="Normalize"/> discards.
/// </summary>
[UiElement("DateTime", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class DateTimeField : DateTimeFieldBase
{
    public DateTimeField(ElementRuntimeContext context) : base(context)
    {
    }

    protected override string UiFormat => "MM/dd/yyyy h:mm tt";

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
