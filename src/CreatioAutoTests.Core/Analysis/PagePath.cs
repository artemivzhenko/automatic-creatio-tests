using System.Text.RegularExpressions;

namespace CreatioAutoTests.Analysis;

/// <summary>
/// A page address split into the parts configuration and the DSL each need: the url template,
/// the record it was opened for, and the schema that gives the page its default name.
/// </summary>
public sealed partial class PagePath
{
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
    }

    public string Raw { get; }

    public string Url { get; }

    public string? RecordId { get; }

    public string? Schema { get; }

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

    public string WithRecord(string recordId) => Url.TrimEnd('/') + "/edit/" + recordId;

    [GeneratedRegex(@"/edit/([0-9a-fA-F-]{36})/?$")]
    private static partial Regex RecordSuffix();

    [GeneratedRegex(@"#(?:Card|Section|Page)/([A-Za-z0-9_]+)")]
    private static partial Regex SchemaInPath();
}
