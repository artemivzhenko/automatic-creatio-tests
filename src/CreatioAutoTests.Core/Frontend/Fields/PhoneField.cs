using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Phone input that locates Creatio's specialized telephone editor first.</summary>
[UiElement("Phone", HostTag = "crt-phone-input", ValueKind = ValueKind.Text)]
public sealed class PhoneField : TextInputFieldBase
{
    /// <summary>Initializes a configured phone control.</summary>
    public PhoneField(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Uses the phone-specific input, with the shared Material input as a fallback.</summary>
    protected override ILocator ValueLocator =>
        Root.Locator("input[crtphoneinput], " + MaterialInputSelector);
}