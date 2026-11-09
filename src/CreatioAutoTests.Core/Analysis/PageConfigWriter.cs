using System.Text.Json;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Analysis;

/// <summary>What happened when an analysis was written to disk.</summary>
public sealed record PageConfigWriteResult(
    string FilePath,
    int Written,
    IReadOnlyList<ElementDefinition> NoLongerOnPage,
    bool KeptMissing,
    IReadOnlyList<ElementDefinition> Pinned);

/// <summary>
/// Writes an analysis into the page's own configuration file.
///
/// One file per page is what keeps the configuration readable, and it means analysing a page can
/// never disturb another one.
/// </summary>
public sealed class PageConfigWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Page files are hand-edited, so they match the camelCase of every other config file.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Omits pinned:false on every entry. A string's default is null rather than empty,
        // so a deliberately blank label still survives.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    private readonly ILogger<PageConfigWriter> _logger;

    /// <summary>Initializes logging for page-scoped configuration writing.</summary>
    public PageConfigWriter(ILogger<PageConfigWriter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes one page file while retaining optionally missing entries and person-pinned decisions,
    /// so analyzing one page cannot disturb another page's configuration.
    /// </summary>
    public PageConfigWriteResult Write(
        PageAnalysis analysis,
        string pagesDirectory,
        bool keepMissing = false,
        string? pageType = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        Directory.CreateDirectory(pagesDirectory);
        var path = Path.Combine(pagesDirectory, analysis.PageName + ".json");

        var existing = ReadExisting(path);
        var found = analysis.Elements.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);

        // An entry that is configured but no longer rendered is either a page change worth knowing
        // about, or a deliberate expectation that something must not appear.
        var missing = existing
            .Where(e => !found.Contains(e.Code))
            .ToArray();

        // An entry a person pinned survives untouched: analysis cannot know what it knows.
        var pinned = existing.Where(e => e.Pinned).ToDictionary(e => e.Code, StringComparer.Ordinal);

        var elements = analysis.Elements
            .Select(e => pinned.TryGetValue(e.Code, out var kept) ? kept : ToDefinition(e))
            .ToList();

        if (keepMissing)
        {
            elements.AddRange(missing);
        }

        elements = elements
            .OrderBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .ToList();

        var document = new PageDefinition
        {
            Name = analysis.PageName,
            Url = analysis.Url,
            Type = pageType ?? analysis.Generation.Name(),
            // Null rather than empty so that a Freedom UI page file carries no "schema" line at all.
            Schema = string.IsNullOrWhiteSpace(analysis.Schema) ? null : analysis.Schema,
            Elements = elements,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(document, WriteOptions) + System.Environment.NewLine);
        _logger.LogInformation("Wrote {Count} element(s) to {Path}.", elements.Count, path);

        return new PageConfigWriteResult(
            path,
            elements.Count,
            missing,
            keepMissing,
            pinned.Values.Where(p => found.Contains(p.Code)).ToArray());
    }

    private static ElementDefinition ToDefinition(DiscoveredElement element)
    {
        var definition = new ElementDefinition
        {
            Code = element.Code,
            Label = element.Label,
            Type = element.Type,
        };

        // Type-specific findings travel as extra members, bound to the element's options class when
        // the catalog builds it. Anything absent stays absent: an empty array in a page file would
        // read as a deliberate expectation of nothing.
        var extra = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (element.Stages is { Count: > 0 })
        {
            extra["values"] = JsonSerializer.SerializeToElement(element.Stages);
        }

        if (element.Columns is { Count: > 0 })
        {
            extra["columns"] = JsonSerializer.SerializeToElement(element.Columns);
        }

        if (element.Tabs is { Count: > 0 })
        {
            extra["tabs"] = JsonSerializer.SerializeToElement(element.Tabs);
        }

        if (element.Subvalues is { Count: > 0 })
        {
            extra["subvalues"] = JsonSerializer.SerializeToElement(element.Subvalues);
        }

        // Classic UI: the schema name when the code is the friendlier column name, and the detail a
        // button belongs to. Both are identity, so they are written even when a page file is regenerated.
        if (!string.IsNullOrWhiteSpace(element.Name) &&
            !string.Equals(element.Name, element.Code, StringComparison.Ordinal))
        {
            extra["name"] = JsonSerializer.SerializeToElement(element.Name);
        }

        if (!string.IsNullOrWhiteSpace(element.Detail))
        {
            extra["detail"] = JsonSerializer.SerializeToElement(element.Detail);
        }

        if (extra.Count > 0)
        {
            definition.Extra = extra;
        }

        return definition;
    }

    private List<ElementDefinition> ReadExisting(string path)
    {
        if (!File.Exists(path))
        {
            return new List<ElementDefinition>();
        }

        try
        {
            var page = JsonSerializer.Deserialize<PageDefinition>(
                File.ReadAllText(path),
                ConfigurationLoader.SerializerOptions);

            return page?.Elements ?? new List<ElementDefinition>();
        }
        catch (JsonException ex)
        {
            // A broken file must not stop the analysis; it is about to be replaced anyway.
            _logger.LogWarning(ex, "Existing page file {Path} could not be read; it will be replaced.", path);
            return new List<ElementDefinition>();
        }
    }
}
