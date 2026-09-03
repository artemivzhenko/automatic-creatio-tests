namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Number field that accepts integral and fractional values.</summary>
[UiElement("Decimal", HostTag = "crt-number-input", ValueKind = ValueKind.Number)]
public sealed class DecimalField : NumberFieldBase
{
    /// <summary>Initializes a decimal-capable number field.</summary>
    public DecimalField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Allows the fractional component preserved by decimal values.</summary>
    protected override bool AllowsFraction => true;
}