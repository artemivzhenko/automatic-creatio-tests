namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic multi-line text (<c>MemoEdit</c>): a plain textarea, not an editor, so it reads and writes
/// like the single-line input. Registered as <c>RichText</c> so a page file names the long-text
/// column the same way in both generations.
/// </summary>
[UiElement("RichText", Generation = UiGeneration.ClassicUI, Controls = new[] { "MemoEdit" },
    ValueKind = ValueKind.Text, Options = typeof(ClassicElementOptions))]
public sealed class RichTextField : ClassicFieldBase
{
    /// <summary>Initializes a Classic memo textarea.</summary>
    public RichTextField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Reads the textarea's text.</summary>
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
