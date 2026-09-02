namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Date", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class DateField : DateTimeFieldBase
{
    public DateField(ElementRuntimeContext context) : base(context)
    {
    }

    protected override string UiFormat => "MM/dd/yyyy";

    protected override string[] UiPatterns => new[] { "MM/dd/yyyy" };

    // The time component is dropped because the control does not store one.
    protected override DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
}