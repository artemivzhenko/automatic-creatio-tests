using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Analysis;

/// <summary>One control found on a page, already mapped to an element type.</summary>
/// <param name="Code">Code the DSL will address the control by.</param>
/// <param name="Label">Caption the control renders.</param>
/// <param name="Type">Element type name in the page's generation.</param>
/// <param name="HostTag">Freedom UI host tag, or the Classic control class, the control was found as.</param>
/// <param name="Required">Whether the page marks the control required.</param>
/// <param name="ReadOnly">Whether the page renders the control locked.</param>
/// <param name="Visible">Whether the control was visible when found.</param>
/// <param name="Stages">Stage captions of a stage bar.</param>
/// <param name="Columns">Column captions of a grid or detail.</param>
/// <param name="Tabs">Tab captions of a toggle group or tab panel.</param>
/// <param name="Uncertainty">Why the type is a guess, when it is one.</param>
/// <param name="TypeUndecidable">Whether nobody could settle the type: presence only.</param>
/// <param name="Name">Classic UI: the element's name in the schema, when it differs from the code.</param>
/// <param name="Detail">Classic UI: the detail schema a button belongs to.</param>
/// <param name="Subvalues">Stage caption to the sub-stages behind its menu.</param>
/// <param name="IsSave">Whether this is the page's Save button.</param>
public sealed record DiscoveredElement(
    string Code,
    string Label,
    string Type,
    string HostTag,
    bool Required,
    bool ReadOnly,
    bool Visible,
    IReadOnlyList<string>? Stages,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<string>? Tabs,
    string? Uncertainty,
    bool TypeUndecidable,
    string? Name = null,
    string? Detail = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Subvalues = null,
    bool IsSave = false);

/// <summary>A control the page renders that no registered element type claims.</summary>
public sealed record UnknownControl(string HostTag, string Code);

/// <summary>Everything one analysis run learned about a page.</summary>
public sealed class PageAnalysis
{
    /// <summary>DSL page name chosen for the analyzed configuration file.</summary>
    public required string PageName { get; init; }

    /// <summary>Url template: the address with any /edit/{id} removed.</summary>
    public required string Url { get; init; }

    /// <summary>Optional record identifier from the address that was analyzed.</summary>
    public string? RecordId { get; init; }

    /// <summary>UI generation the page was analysed as; it becomes the page file's <c>type</c>.</summary>
    public UiGeneration Generation { get; init; } = UiGeneration.FreedomUI;

    /// <summary>Classic UI: the page schema name every control id starts with; null for Freedom UI.</summary>
    public string? Schema { get; init; }

    /// <summary>Addressable controls mapped to registered element types on the analyzed page.</summary>
    public required IReadOnlyList<DiscoveredElement> Elements { get; init; }

    /// <summary>Controls with no element type yet: the backlog of what the framework cannot address.</summary>
    public required IReadOnlyList<UnknownControl> UnknownControls { get; init; }

    /// <summary>Controls whose type a person can settle by looking at the page.</summary>
    public IEnumerable<DiscoveredElement> Uncertain =>
        Elements.Where(e => e.Uncertainty is not null && !e.TypeUndecidable);

    /// <summary>
    /// Controls whose exact type nobody can establish: read-only and empty, so there is neither a
    /// picker icon nor a value to go by. Their presence and caption are still assertable, and
    /// nothing a test can do with them depends on which of the date types was recorded.
    /// </summary>
    public IEnumerable<DiscoveredElement> PresenceOnly =>
        Elements.Where(e => e.TypeUndecidable);

    /// <summary>Discovered controls grouped by their resolved element type for concise reporting.</summary>
    public IEnumerable<IGrouping<string, DiscoveredElement>> ByType =>
        Elements.GroupBy(e => e.Type).OrderBy(g => g.Key, StringComparer.Ordinal);
}
