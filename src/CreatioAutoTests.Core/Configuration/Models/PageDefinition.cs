using System.Text.Json.Serialization;

namespace CreatioAutoTests.Configuration;

/// <summary>
/// One page and everything on it, loaded from its own file in the pages directory.
///
/// A page owns its elements rather than being cross-referenced from a second file: the natural unit
/// of work is a page, so keeping its fields, buttons and tabs beside it is what stops the
/// configuration turning into one unreadable list.
/// </summary>
public sealed class PageDefinition
{
    /// <summary>Name used in the DSL: <c>open ContactForm</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path under the base URL. The framework appends /edit/{id} when a record is opened.</summary>
    public string Url { get; set; } = string.Empty;

    public string Type { get; set; } = "FreedomUI";

    /// <summary>
    /// Controls on this page. Each element's <see cref="ElementDefinition.Page"/> is filled in from
    /// the owning page while loading, so the page never has to be repeated inside the file.
    /// </summary>
    public List<ElementDefinition> Elements { get; set; } = new();

    /// <summary>File the page was read from. Set by the loader, used to make errors traceable.</summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    public override string ToString() => Name + " (" + Url + ")";
}
