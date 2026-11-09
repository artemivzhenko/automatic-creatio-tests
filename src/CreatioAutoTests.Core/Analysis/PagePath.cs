using System.Text.RegularExpressions;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Runtime;

namespace CreatioAutoTests.Analysis;

/// <summary>
/// A page address split into the parts configuration and the DSL each need: the url template,
/// the record it was opened for, the schema that gives the page its default name, and the UI
/// generation the Shell routes it to.
/// </summary>
public sealed partial class PagePath
{
    /// <summary>Route prefix a Freedom UI form page is opened through.</summary>
    public const string CardRoute = "/0/Shell/#Card/";

    /// <summary>Route prefix a Classic UI page is opened through inside the Shell.</summary>
    public const string ClassicCardRoute = "/0/Shell/#CardModuleV2/";

    /// <summary>Splits a page address into the parts configuration and the DSL each need.</summary>
    /// <param name="raw">Address as the browser shows it, with or without the instance prefix.</param>
    /// <exception cref="ArgumentException">The address was empty.</exception>
    public PagePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("A page address must be provided.", nameof(raw));
        }

        var path = raw.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath +
                   (string.IsNullOrEmpty(absolute.Fragment) ? string.Empty : absolute.Fragment);
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        Raw = path;

        var record = RecordSuffix().Match(path);
        RecordId = record.Success ? record.Groups[1].Value : null;

        // Configuration stores the template; the framework appends /edit/{id} when opening a record.
        Url = RecordSuffix().Replace(path, string.Empty);

        var schema = SchemaInPath().Match(path);
        Schema = schema.Success ? schema.Groups[1].Value : null;

        Generation = PageRuntimes.ForAddress(path).Generation;
    }

    /// <summary>Normalized original page address, including an optional record suffix.</summary>
    public string Raw { get; }

    /// <summary>Configuration URL template with any <c>/edit/{id}</c> suffix removed.</summary>
    public string Url { get; }

    /// <summary>Record identifier supplied in an optional terminal edit suffix.</summary>
    public string? RecordId { get; }

    /// <summary>
    /// Schema the route names: a Freedom UI page after <c>#Card/</c>, <c>#Section/</c> or
    /// <c>#Page/</c>, a Classic UI page after <c>#CardModuleV2/</c> or <c>#SectionModuleV2/</c>.
    /// </summary>
    public string? Schema { get; }

    /// <summary>UI generation the address belongs to, read off the route.</summary>
    public UiGeneration Generation { get; }

    /// <summary>Name the page gets in the DSL unless one is given explicitly.</summary>
    public string DefaultPageName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Schema))
            {
                return Schema;
            }

            var derived = Url.Trim('/').Replace('/', '_');
            return string.IsNullOrWhiteSpace(derived) ? "Page" : derived;
        }
    }

    /// <summary>Builds the edit address used when opening a particular record from this template.</summary>
    public string WithRecord(string recordId) => Url.TrimEnd('/') + "/edit/" + recordId;

    [GeneratedRegex(@"/edit/([0-9a-fA-F-]{36})/?$")]
    private static partial Regex RecordSuffix();

    [GeneratedRegex(@"#(?:CardModuleV2|SectionModuleV2|Card|Section|Page)/([A-Za-z0-9_]+)")]
    private static partial Regex SchemaInPath();
}
