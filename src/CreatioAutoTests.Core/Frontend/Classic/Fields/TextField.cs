namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic single-line text (<c>TextEdit</c>). Reference implementation for the Classic fields:
/// the <c>-el</c> node is the input, filling it and tabbing out commits the value.
/// </summary>
[UiElement("Text", Generation = UiGeneration.ClassicUI, Controls = new[] { "TextEdit" },
    ValueKind = ValueKind.Text, Options = typeof(ClassicElementOptions))]
public sealed class TextField : ClassicFieldBase
{
    /// <summary>Initializes a Classic text input.</summary>
    public TextField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Reads the input's text as the user sees it.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await ValueLocator.InputValueAsync().ConfigureAwait(false);
    }

    /// <summary>Replaces the text and commits it.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        await FillAndCommitAsync(ValueLocator, value).ConfigureAwait(false);
    }
}
