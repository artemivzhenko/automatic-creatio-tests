namespace CreatioAutoTests.Frontend.Controls;

/// <summary>
/// Extra members a toggle tab group declares in its page file.
/// </summary>
public sealed class ToggleTabOptions
{
    /// <summary>
    /// Captions of the tabs in the group, in the order they are rendered.
    ///
    /// Filled in by <c>analyse</c>. A toggle tab has no <c>element-name</c> of its own -- the group
    /// is the addressable element and a tab inside it is named by its caption -- so this list is the
    /// only record of what the group contains.
    /// </summary>
    public string[] Tabs { get; set; } = Array.Empty<string>();
}
