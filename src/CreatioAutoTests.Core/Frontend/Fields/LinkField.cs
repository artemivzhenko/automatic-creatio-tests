using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Link", HostTag = "crt-web-input", ValueKind = ValueKind.Text)]
public sealed class LinkField : TextInputFieldBase
{
    public LinkField(ElementRuntimeContext context) : base(context)
    {
    }


    protected override ILocator ValueLocator =>
        Root.Locator("input[data-qa=\"web-field-input\"], " + MaterialInputSelector);
}