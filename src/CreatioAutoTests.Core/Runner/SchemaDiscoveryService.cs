using CreatioAutoTests.Analysis;
using CreatioAutoTests.Session;

namespace CreatioAutoTests.Runner;

/// <summary>One Freedom UI page schema, with the address that opens it.</summary>
/// <param name="Name">Schema name, as used in a page address.</param>
/// <param name="AddressTemplate">Address to analyse, with a placeholder for the record.</param>
public sealed record PageSchema(string Name, string AddressTemplate);

/// <summary>
/// Lists the Freedom UI page schemas an instance has.
///
/// Analysing a page needs its schema name, and the name is rarely what you would guess: contacts
/// live on <c>Contacts_FormPage</c>, plural, while <c>ContactPageV2</c> is the classic UI page for
/// the same entity. Nothing in the browser shows the list, so it is read over OData.
/// </summary>
public sealed class SchemaDiscoveryService
{
    /// <summary>Suffix of a Freedom UI form page schema.</summary>
    public const string FormPageSuffix = "_FormPage";

    /// <summary>Suffix of a Freedom UI list page schema.</summary>
    public const string ListPageSuffix = "_ListPage";

    private readonly CreatioODataClient _client;

    /// <summary>Creates the service over an OData client for the configured instance.</summary>
    /// <param name="client">Reads <c>SysSchema</c> as an authenticated user.</param>
    public SchemaDiscoveryService(CreatioODataClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Finds page schemas whose name ends with <paramref name="suffix"/>, optionally narrowed to
    /// those containing <paramref name="term"/>.
    /// </summary>
    /// <param name="term">Substring to match, case-insensitively; all schemas when null.</param>
    /// <param name="suffix">Schema suffix to look for, defaulting to form pages.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Distinct schemas, ordered by name. Creatio stores several rows per schema.</returns>
    public async Task<IReadOnlyList<PageSchema>> FindAsync(
        string? term = null,
        string suffix = FormPageSuffix,
        CancellationToken cancellationToken = default)
    {
        var rows = await _client
            .QueryAsync(
                "SysSchema",
                select: new[] { "Name" },
                filter: "contains(Name,'" + suffix + "')",
                top: 2000,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var names = rows
            .Select(r => r.TryGetProperty("Name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n) && n!.EndsWith(suffix, StringComparison.Ordinal))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .Where(n => term is null || n.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        return names.Select(n => new PageSchema(n, BuildAddress(n, suffix))).ToArray();
    }

    /// <summary>
    /// Builds the address that opens a schema. A form page needs a record; a list page does not.
    /// </summary>
    /// <param name="schema">Schema name.</param>
    /// <param name="suffix">Suffix the schema was found by.</param>
    public static string BuildAddress(string schema, string suffix) =>
        suffix == ListPageSuffix
            ? "/0/Shell/#Section/" + schema
            : PagePath.CardRoute + schema + "/edit/<recordId>";
}
