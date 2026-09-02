using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Rich text editor (crt-rich-text-editor). The control is CKEditor, so the value has to go through
/// the CKEditor instance rather than the DOM node, otherwise Creatio never sees the change.
/// </summary>
[UiElement("RichText", HostTag = "crt-rich-text-editor", ValueKind = ValueKind.Text)]
public sealed class RichTextField : FieldElementBase
{
    private const string EditableSelector = ".cke_textarea_inline";

    public RichTextField(ElementRuntimeContext context) : base(context)
    {
    }


    protected override ILocator ValueLocator => Root.Locator(EditableSelector);

    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await ValueLocator.InnerTextAsync().ConfigureAwait(false);
    }

    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        const string script = @"(host, v) => {
            const editable = host.querySelector('.cke_textarea_inline');
            if (!editable) { return false; }

            const ck = window.CKEDITOR;
            if (ck && ck.instances) {
                for (const name in ck.instances) {
                    const instance = ck.instances[name];
                    if (instance.editable && instance.editable() && instance.editable().$ === editable) {
                        instance.setData(v ?? '');
                        instance.fire('change');
                        return true;
                    }
                }
            }

            editable.innerHTML = v ?? '';
            editable.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            editable.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
            return true;
        }";

        var applied = await Root.EvaluateAsync<bool>(script, value ?? string.Empty).ConfigureAwait(false);
        if (!applied)
        {
            throw new InvalidOperationException(
                "Rich text editor '" + Code + "' has no editable area; the control may not be initialised yet.");
        }
    }
}
