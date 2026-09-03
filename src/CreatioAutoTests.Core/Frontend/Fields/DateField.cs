namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Date-only picker that returns values as <c>2025-11-30T00:00:00</c>.</summary>
[UiElement("Date", HostTag = "crt-datetimepicker", ValueKind = ValueKind.DateTime)]
public sealed class DateField : DateTimeFieldBase
{
    /// <summary>Initializes a date-only picker.</summary>
    public DateField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Creatio display format for date-only values.</summary>
    protected override string UiFormat => "MM/dd/yyyy";

    /// <summary>Single rendered format accepted when reading the picker.</summary>
    protected override string[] UiPatterns => new[] { "MM/dd/yyyy" };

    /// <summary>Drops time because the control stores a calendar date only.</summary>
    protected override DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
}