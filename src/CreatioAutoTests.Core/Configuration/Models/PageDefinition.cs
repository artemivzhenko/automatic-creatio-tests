using System.Text.Json.Serialization;
using CreatioAutoTests.Frontend;

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

    /// <summary>
    /// Declared page technology: <c>FreedomUI</c> (the default) or <c>ClassicUI</c>. It decides which
    /// element types the page may use and how the framework waits for the page and finds its controls.
    /// </summary>
    public string Type { get; set; } = UiGenerations.FreedomUIName;

    /// <summary>
    /// Classic UI only: the page schema name, for example <c>ContactPageV2</c>. Every control id on a
    /// Classic page starts with it, so without it nothing on the page can be addressed. Ignored on a
    /// Freedom UI page, where elements carry their own <c>element-name</c>.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Controls on this page. Each element's <see cref="ElementDefinition.Page"/> is filled in from
    /// the owning page while loading, so the page never has to be repeated inside the file.
    /// </summary>
    public List<ElementDefinition> Elements { get; set; } = new();

    /// <summary>File the page was read from. Set by the loader, used to make errors traceable.</summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>
    /// <see cref="Type"/> as a generation. An unrecognised type reads as Freedom UI here so that
    /// callers never throw; the catalog is where an unknown type is reported.
    /// </summary>
    [JsonIgnore]
    public UiGeneration Generation =>
        UiGenerations.TryParse(Type, out var generation) ? generation : UiGeneration.FreedomUI;

    /// <summary>Formats the DSL page name and its base-relative address for diagnostics.</summary>
    public override string ToString() => Name + " (" + Url + ")";
}
