namespace CreatioAutoTests.Frontend.Composite;

/// <summary>
/// Extra members a stage progress bar declares in elements.config.json.
/// Example of an element type that needs more than code, page and label.
/// </summary>
public sealed class CasesBarOptions
{
    /// <summary>Stages expected on the bar, in display order.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();

    /// <summary>Stage name to the sub-stages hidden behind its menu arrow.</summary>
    public Dictionary<string, string[]>? Subvalues { get; set; }
}
