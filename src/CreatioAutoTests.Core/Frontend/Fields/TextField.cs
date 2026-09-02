namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Plain single-line text input (crt-input).
/// Reference implementation for every input-backed field: supply the host tag, inherit the rest.
/// </summary>
[UiElement("Text", HostTag = "crt-input", ValueKind = ValueKind.Text)]
public sealed class TextField : TextInputFieldBase
{
    public TextField(ElementRuntimeContext context) : base(context)
    {
    }

}
