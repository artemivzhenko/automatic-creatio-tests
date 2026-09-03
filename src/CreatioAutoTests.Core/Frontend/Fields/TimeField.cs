namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Time-only picker that returns values as <c>0001-01-01T07:36:00</c>.</summary>
[UiElement("Time", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class TimeField : DateTimeFieldBase
{
    /// <summary>Initializes a time-only picker.</summary>
    public TimeField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Creatio display format for time-only values.</summary>
    protected override string UiFormat => "h:mm tt";

    /// <summary>12-hour and 24-hour formats Creatio may render for a time.</summary>
    protected override string[] UiPatterns => new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" };

    /// <summary>Drops the date because the control stores only a time of day.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(1, 1, 1, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
}