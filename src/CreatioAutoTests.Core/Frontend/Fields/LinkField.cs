using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Web-address input that locates Creatio's specialized URL editor first.</summary>
[UiElement("Link", HostTag = "crt-web-input", ValueKind = ValueKind.Text)]
public sealed class LinkField : TextInputFieldBase
{
    /// <summary>Initializes a configured web-address control.</summary>
    public LinkField(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Uses the URL-specific input, with the shared Material input as a fallback.</summary>
    protected override ILocator ValueLocator =>
        Root.Locator("input[data-qa=\"web-field-input\"], " + MaterialInputSelector);
}