namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Decimal", HostTag = "crt-number-input", ValueKind = ValueKind.Number)]
public sealed class DecimalField : NumberFieldBase
{
    public DecimalField(ElementRuntimeContext context) : base(context)
    {
    }

    protected override bool AllowsFraction => true;
}