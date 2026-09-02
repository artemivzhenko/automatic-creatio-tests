using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Base for controls backed by a plain input element: text, phone, link.
/// A concrete type only supplies its host tag and, when needed, a value locator.
/// </summary>
public abstract class TextInputFieldBase : FieldElementBase
{
    protected TextInputFieldBase(ElementRuntimeContext context) : base(context)
    {
    }

    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await ValueLocator.InputValueAsync().ConfigureAwait(false);
    }

    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var input = ValueLocator;
        var text = value ?? string.Empty;

        // Creatio hides some inputs behind a rendered link; those can only be written through the DOM.
        var visible = false;
        try
        {
            visible = await input.IsVisibleAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
        }

        if (visible)
        {
            await input.FillAsync(text).ConfigureAwait(false);
            return;
        }

        await SetValueThroughDomAsync(input, text).ConfigureAwait(false);
    }
}
