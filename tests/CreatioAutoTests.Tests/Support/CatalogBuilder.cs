using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend.Registry;

namespace CreatioAutoTests.Tests.Support;

/// <summary>
/// Builds small in-memory catalogs so validation can be tested without configuration files
/// and without a browser.
/// </summary>
public static class CatalogBuilder
{
    public static UiElementRegistry Registry { get; } = UiElementRegistry.Discover();

    /// <summary>Groups the elements into page definitions, the way the loader does from disk.</summary>
    public static ElementCatalog Build(params ElementDefinition[] elements)
    {
        var pages = elements
            .GroupBy(e => e.Page, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PageDefinition
            {
                Name = group.Key,
                Url = "/0/Shell/#Card/" + group.Key,
                SourceFile = group.Key + ".json",
                Elements = group.ToList(),
            })
            .ToList();

        return ElementCatalog.Build(pages, Registry);
    }

    /// <summary>Builds from explicit page definitions, for tests about pages themselves.</summary>
    public static ElementCatalog BuildPages(params PageDefinition[] pages) =>
        ElementCatalog.Build(pages, Registry);

    public static ElementDefinition Element(string code, string type, string page = "TestPage", string? label = null) =>
        new()
        {
            Code = code,
            Type = type,
            Page = page,
            Label = label ?? code
        };

    /// <summary>The catalog most validation tests use: one of each interesting kind.</summary>
    public static ElementCatalog Default() => Build(
        Element("Name", "Text"),
        Element("Notes", "RichText"),
        Element("Account", "Lookup"),
        Element("Started", "DateTime"),
        Element("SaveButton", "Button", label: "Save"));
}
