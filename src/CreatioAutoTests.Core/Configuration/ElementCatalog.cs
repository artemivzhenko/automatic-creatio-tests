using System.Text.Json.Serialization;
using System.Text.Json;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Registry;

namespace CreatioAutoTests.Configuration;

/// <summary>
/// Every configured element, keyed by page and code. This is what lets the validator decide,
/// before a browser is started, which class a command will have to work with.
/// </summary>
public sealed class ElementCatalog
{
    private readonly Dictionary<ElementKey, CatalogEntry> _entries = new();
    private readonly Dictionary<string, PageDefinition> _pages;

    private ElementCatalog(Dictionary<string, PageDefinition> pages)
    {
        _pages = pages;
    }

    /// <summary>All configured pages, each owning its configured elements.</summary>
    public IReadOnlyCollection<PageDefinition> Pages => _pages.Values;

    /// <summary>All elements keyed by their page-and-code identity.</summary>
    public IReadOnlyCollection<CatalogEntry> Entries => _entries.Values;

    /// <summary>Returns whether a page name exists in the configured catalog.</summary>
    public bool PageExists(string pageName) => _pages.ContainsKey(pageName ?? string.Empty);

    /// <summary>Looks up a configured page without throwing for an unknown name.</summary>
    public bool TryGetPage(string pageName, out PageDefinition page) =>
        _pages.TryGetValue(pageName ?? string.Empty, out page!);

    /// <summary>Looks up an element by page and code, allowing the same code on different pages.</summary>
    public bool TryGetElement(string pageName, string code, out CatalogEntry entry) =>
        _entries.TryGetValue(new ElementKey(pageName ?? string.Empty, code ?? string.Empty), out entry!);

    /// <summary>Codes available on a page, for "did you mean" style error messages.</summary>
    public IReadOnlyCollection<string> CodesOnPage(string pageName)
    {
        return _entries.Values
            .Where(e => string.Equals(e.Definition.Page, pageName, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Definition.Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Configured page names in stable order for validation messages.</summary>
    public IReadOnlyCollection<string> PageNames =>
        _pages.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Builds the catalog from every page file and reports all problems at once: unknown element
    /// type, duplicate page or code, unknown extra members.
    /// </summary>
    public static ElementCatalog Build(
        IReadOnlyList<PageDefinition> pageDefinitions,
        UiElementRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(pageDefinitions);
        ArgumentNullException.ThrowIfNull(registry);

        var errors = new List<string>();
        var pages = new Dictionary<string, PageDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pageDefinitions)
        {
            var origin = page.SourceFile is null ? string.Empty : " (" + page.SourceFile + ")";

            if (string.IsNullOrWhiteSpace(page.Name))
            {
                errors.Add("A page has no name" + origin + ".");
                continue;
            }

            if (string.IsNullOrWhiteSpace(page.Url))
            {
                errors.Add("Page '" + page.Name + "' has no url" + origin + ".");
                continue;
            }

            if (!UiGenerations.TryParse(page.Type, out var generation))
            {
                errors.Add("Page '" + page.Name + "' has unknown type '" + page.Type + "'" + origin +
                           ". Known types: " + string.Join(", ", UiGenerations.KnownNames) + ".");
                continue;
            }

            // A Classic UI control id is the page schema name followed by the element's own name, so a
            // Classic page without a schema cannot address a single control.
            if (generation == UiGeneration.ClassicUI && string.IsNullOrWhiteSpace(page.Schema))
            {
                errors.Add("Page '" + page.Name + "' is ClassicUI but declares no schema" + origin +
                           ". Set \"schema\" to the page schema name, for example \"ContactPageV2\".");
                continue;
            }

            if (!pages.TryAdd(page.Name, page))
            {
                errors.Add("Page '" + page.Name + "' is declared twice" + origin +
                           "; it is already defined in " + (pages[page.Name].SourceFile ?? "another file") + ".");
            }
        }

        var catalog = new ElementCatalog(pages);

        foreach (var page in pageDefinitions)
        {
            if (string.IsNullOrWhiteSpace(page.Name) || !pages.ContainsKey(page.Name))
            {
                continue;
            }

            foreach (var definition in page.Elements)
            {
                definition.Page = page.Name;

                var entry = Validate(definition, pages, registry, errors);
                if (entry is null)
                {
                    continue;
                }

                if (!catalog._entries.TryAdd(definition.Key, entry))
                {
                    errors.Add("Element '" + definition.Code + "' is declared twice on page '" +
                               page.Name + "'.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Element configuration is invalid:" + System.Environment.NewLine + " - " +
                string.Join(System.Environment.NewLine + " - ", errors));
        }

        return catalog;
    }

    private static CatalogEntry? Validate(
        ElementDefinition definition,
        IReadOnlyDictionary<string, PageDefinition> pages,
        UiElementRegistry registry,
        List<string> errors)
    {
        var label = string.IsNullOrWhiteSpace(definition.Code) ? "(no code)" : definition.Code;

        if (string.IsNullOrWhiteSpace(definition.Code))
        {
            errors.Add("An element entry has no code.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(definition.Page))
        {
            errors.Add("Element '" + label + "' does not say which page it lives on.");
            return null;
        }

        if (!pages.ContainsKey(definition.Page))
        {
            errors.Add("Element '" + label + "' refers to unknown page '" + definition.Page + "'.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(definition.Type))
        {
            errors.Add("Element '" + label + "' has no type.");
            return null;
        }

        // The page's generation decides which 'Text' or 'Lookup' this is. A Freedom UI type on a
        // Classic page is the most likely mistake, and the registry's message says so.
        var generation = pages[definition.Page].Generation;
        if (!registry.TryResolve(generation, definition.Type, out var registration))
        {
            errors.Add("Element '" + label + "' on page '" + definition.Page + "': " +
                       registry.DescribeUnknown(generation, definition.Type));
            return null;
        }

        ValidateExtraMembers(definition, registration, label, errors);

        return new CatalogEntry(definition, registration);
    }

    /// <summary>
    /// Extra JSON members are only allowed when the element type declares an options class,
    /// and then only the members that class actually has. This turns a typo in configuration
    /// into a load-time error instead of a silently ignored setting.
    /// </summary>
    private static void ValidateExtraMembers(
        ElementDefinition definition,
        UiElementRegistration registration,
        string label,
        List<string> errors)
    {
        if (definition.Extra is null || definition.Extra.Count == 0)
        {
            return;
        }

        if (registration.OptionsType is null)
        {
            errors.Add("Element '" + label + "' of type '" + definition.Type + "' does not accept extra members, " +
                       "but declares: " + string.Join(", ", definition.Extra.Keys) + ".");
            return;
        }

        var strict = new JsonSerializerOptions(ConfigurationLoader.SerializerOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        try
        {
            JsonSerializer.Deserialize(
                JsonSerializer.Serialize(definition.Extra),
                registration.OptionsType,
                strict);
        }
        catch (JsonException ex)
        {
            errors.Add("Element '" + label + "' has invalid settings for type '" + definition.Type + "': " +
                       ex.Message);
        }
    }
}

/// <summary>A configured element paired with the implementation class that will realise it.</summary>
public sealed record CatalogEntry(ElementDefinition Definition, UiElementRegistration Registration)
{
    /// <summary>Element code unique within the entry's configured page.</summary>
    public string Code => Definition.Code;

    /// <summary>Configured page that owns this element.</summary>
    public string Page => Definition.Page;

    /// <summary>Concrete CLR class that materializes this configured element at execution time.</summary>
    public Type ElementType => Registration.ImplementationType;

    /// <summary>Static value category used to validate assignments and comparisons before execution.</summary>
    public Frontend.ValueKind ValueKind => Registration.ValueKind;

    /// <summary>True when the implementation offers the requested capability or concrete type.</summary>
    public bool Supports(Type capability) => capability.IsAssignableFrom(Registration.ImplementationType);

    /// <summary>Formats the configured element for diagnostics.</summary>
    public override string ToString() => Definition.ToString();
}
