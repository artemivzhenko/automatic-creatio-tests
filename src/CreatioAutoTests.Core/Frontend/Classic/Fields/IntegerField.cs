namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>Classic whole-number input (<c>IntegerEdit</c>).</summary>
[UiElement("Integer", Generation = UiGeneration.ClassicUI, Controls = new[] { "IntegerEdit" },
    ValueKind = ValueKind.Number, Options = typeof(ClassicElementOptions))]
public sealed class IntegerField : ClassicNumberFieldBase
{
    /// <summary>Initializes a Classic integer field.</summary>
    public IntegerField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Rejects fractional values before they reach the control.</summary>
    protected override bool AllowsFraction => false;
}
