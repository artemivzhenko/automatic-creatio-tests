namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Number field that rejects values with a fractional component.</summary>
[UiElement("Integer", HostTag = "crt-number-input", ValueKind = ValueKind.Number)]
public sealed class IntegerField : NumberFieldBase
{
    /// <summary>Initializes an integer-only number field.</summary>
    public IntegerField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Rejects fractional values before they reach the Creatio control.</summary>
    protected override bool AllowsFraction => false;
}