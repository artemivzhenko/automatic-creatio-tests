namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic fractional number (<c>FloatEdit</c>) and money (<c>MoneyEdit</c>) inputs. Money is a
/// float with a currency behind it; the control reads and writes the same way, so one type covers both.
/// </summary>
[UiElement("Decimal", Generation = UiGeneration.ClassicUI, Controls = new[] { "FloatEdit", "MoneyEdit" },
    ValueKind = ValueKind.Number, Options = typeof(ClassicElementOptions))]
public sealed class DecimalField : ClassicNumberFieldBase
{
    /// <summary>Initializes a Classic decimal field.</summary>
    public DecimalField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Allows the fractional component.</summary>
    protected override bool AllowsFraction => true;
}
