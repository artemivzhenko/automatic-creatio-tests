namespace CreatioAutoTests.Analysis;

/// <summary>One control found on a page, already mapped to an element type.</summary>
public sealed record DiscoveredElement(
    string Code,
    string Label,
    string Type,
    string HostTag,
    bool Required,
    bool ReadOnly,
    bool Visible,
    IReadOnlyList<string>? Stages,
    string? Uncertainty,
    bool TypeUndecidable);

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
