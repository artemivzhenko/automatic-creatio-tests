using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

[UiElement("Phone", HostTag = "crt-phone-input", ValueKind = ValueKind.Text)]
public sealed class PhoneField : TextInputFieldBase
{
    public PhoneField(ElementRuntimeContext context) : base(context)
    {
    }


    protected override ILocator ValueLocator =>
        Root.Locator("input[crtphoneinput], " + MaterialInputSelector);
}