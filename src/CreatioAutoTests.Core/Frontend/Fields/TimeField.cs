namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Time", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class TimeField : DateTimeFieldBase
{
    public TimeField(ElementRuntimeContext context) : base(context)
    {
    }

    protected override string UiFormat => "h:mm tt";

    protected override string[] UiPatterns => new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" };

    protected override DateTime Normalize(DateTime value) =>
        new(1, 1, 1, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
}