using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Email input (crt-email-input). Once a value is stored Creatio hides the input and renders a
/// mailto link instead, so reads fall back to the link and writes always go through the DOM.
/// </summary>
[UiElement("Email", HostTag = "crt-email-input", ValueKind = ValueKind.Text)]
public sealed class EmailField : FieldElementBase
{
    private const string TextContainerSelector = ".crt-input-control-email-text";
    private const string InputSelector = "input[data-qa=\"email-field-input\"]";
    private const string LinkSelector = "a[data-qa=\"email-link-field-href\"]";
    private const string HiddenClass = "crt-input-control-element-hidden";

    public EmailField(ElementRuntimeContext context) : base(context)
    {
    }


    protected override ILocator ValueLocator => Root.Locator(TextContainerSelector).Locator(InputSelector);

    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var container = Root.Locator(TextContainerSelector);
        var containerClass = await SafeAttributeAsync(container, "class").ConfigureAwait(false) ?? string.Empty;
        var inputHidden = containerClass.Contains(HiddenClass, StringComparison.OrdinalIgnoreCase);

        if (!inputHidden)
        {
            return await ValueLocator.InputValueAsync().ConfigureAwait(false);
        }

        var link = Root.Locator(LinkSelector);
        if (await link.CountAsync().ConfigureAwait(false) == 0)
        {
            return string.Empty;
        }

        var text = await link.First.InnerTextAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        var href = await link.First.GetAttributeAsync("href").ConfigureAwait(false) ?? string.Empty;
        return href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? href.Substring("mailto:".Length)
            : href;
    }

    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        await SetValueThroughDomAsync(ValueLocator, value ?? string.Empty).ConfigureAwait(false);
    }
}
