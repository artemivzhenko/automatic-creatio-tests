namespace CreatioAutoTests.Frontend.Controls;

/// <summary>
/// Extra members a button may declare in elements.config.json.
/// Save buttons need the double click because Creatio ignores the first one often enough
/// to make tests flaky; it stays configurable so the workaround can be turned off per button.
/// </summary>
public sealed class ButtonOptions
{
    /// <summary>Milliseconds to wait after a click so the UI can settle.</summary>
    public int SettleMs { get; set; } = 1500;

    /// <summary>Treat this button as Save: check for an error popup and wait for it to disappear.</summary>
    public bool IsSave { get; set; }

    /// <summary>Click a second time. Works around Creatio dropping the first Save click.</summary>
    public bool DoubleClick { get; set; }

    /// <summary>Milliseconds allowed for a Save button to leave the DOM after a successful save.</summary>
    public int DisappearTimeoutMs { get; set; } = 5000;
}
