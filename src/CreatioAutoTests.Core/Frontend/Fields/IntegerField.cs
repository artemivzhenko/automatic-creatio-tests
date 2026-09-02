namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Integer", HostTag = "crt-number-input", ValueKind = ValueKind.Number)]
public sealed class IntegerField : NumberFieldBase
{
    public IntegerField(ElementRuntimeContext context) : base(context)
    {
    }

    protected override bool AllowsFraction => false;
}