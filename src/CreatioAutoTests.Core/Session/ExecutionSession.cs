using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Frontend.Runtime;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Session;

/// <summary>
/// Runtime state of a single test case: who is logged in, which page is open, and how a resolved
/// target becomes a live element. One instance per test case, so cases cannot leak state into
/// each other, while browser contexts stay shared through the pool.
/// </summary>
public sealed class ExecutionSession : IExecutionSession, IAsyncDisposable
{
    private readonly BrowserSessionPool _pool;
    private readonly UiElementFactory _factory;
    private readonly CreatioODataClient _backend;
    private readonly ILogger<ExecutionSession> _logger;

    private readonly List<(string Entity, Guid Id)> _cleanup = new();
    private readonly List<string> _consoleErrors = new();

    private IPage? _page;
    private string? _currentUser;

    /// <summary>Initializes isolated per-case state over shared authenticated browser contexts.</summary>
    public ExecutionSession(
        BrowserSessionPool pool,
        UiElementFactory factory,
        CreatioODataClient backend,
        ElementCatalog catalog,
        RunSettings settings,
        IReadOnlyDictionary<string, string> staticValues,
        IReadOnlyDictionary<string, ObjectDefinition> objects,
        IReadOnlyDictionary<string, ProcessDefinition> processes,
        ILogger<ExecutionSession> logger)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        StaticValues = staticValues ?? throw new ArgumentNullException(nameof(staticValues));
        Objects = objects ?? throw new ArgumentNullException(nameof(objects));
        Processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <summary>Processes a script may start, from the processes directory.</summary>
    public IReadOnlyDictionary<string, ProcessDefinition> Processes { get; }

    /// <summary>Outcome of the last 'start' in this case.</summary>
    public ProcessRunResult? LastProcess { get; private set; }

    /// <summary>Starts the process over the engine service as the first configured user and keeps the result.</summary>
    public async Task<ProcessRunResult> StartProcessAsync(
        string process,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        if (!Processes.TryGetValue(process, out var definition))
        {
            throw new InvalidOperationException("Process '" + process + "' is not configured.");
        }

        var outputs = definition.Outputs.Select(p => p.Code).ToArray();
        LastProcess = await _backend.RunProcessAsync(process, values, outputs, cancellationToken).ConfigureAwait(false);
        return LastProcess;
    }

    /// <summary>Reads at most one row of the object with the filter over OData.</summary>
    public async Task<bool> RecordExistsAsync(string entity, string filter, CancellationToken cancellationToken = default)
    {
        var rows = await _backend.QueryAsync(entity, new[] { "Id" }, filter, top: 1, cancellationToken).ConfigureAwait(false);
        return rows.Count > 0;
    }

    /// <summary>The case's <c>$name</c> values.</summary>
    public IDictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Browser console errors and page errors of every page this case opened.</summary>
    public IReadOnlyList<string> ConsoleErrors => _consoleErrors;

    /// <summary>Inserts over OData; the record is deleted at the end of the case unless kept.</summary>
    public async Task<string> InsertRecordAsync(string entity, IReadOnlyList<RecordAssignment> values, bool keep, CancellationToken cancellationToken = default)
    {
        var id = await _backend.InsertAsync(entity, ODataValues.Body(values), cancellationToken).ConfigureAwait(false);
        if (!keep)
        {
            _cleanup.Add((entity, id));
        }

        _logger.LogDebug("Inserted {Entity} {Id}{Keep}.", entity, id, keep ? " (kept)" : string.Empty);
        return id.ToString();
    }

    /// <summary>Changes columns of one record over OData.</summary>
    public Task UpdateRecordAsync(string entity, string id, IReadOnlyList<RecordAssignment> values, CancellationToken cancellationToken = default) =>
        _backend.UpdateAsync(entity, ParseId(id), ODataValues.Body(values), cancellationToken);

    /// <summary>Deletes one record over OData and forgets it in the cleanup list.</summary>
    public async Task DeleteRecordAsync(string entity, string id, CancellationToken cancellationToken = default)
    {
        var guid = ParseId(id);
        await _backend.DeleteAsync(entity, guid, cancellationToken).ConfigureAwait(false);
        _cleanup.RemoveAll(r => r.Id == guid && string.Equals(r.Entity, entity, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the filtered rows, every selected column as text (a null as empty).</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> QueryRecordsAsync(
        string entity, IReadOnlyList<string> select, string filter, int top, CancellationToken cancellationToken = default)
    {
        var rows = await _backend.QueryAsync(entity, select, filter, top, cancellationToken).ConfigureAwait(false);
        return rows.Select(row => (IReadOnlyDictionary<string, string>)select.ToDictionary(
            column => column,
            column => row.TryGetProperty(column, out var value) ? AsText(value) : string.Empty,
            StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>Counts the records matching the filter over OData.</summary>
    public Task<long> CountRecordsAsync(string entity, string filter, CancellationToken cancellationToken = default) =>
        _backend.CountAsync(entity, filter, cancellationToken);

    /// <summary>Deletes the inserted records the case left, newest first; reports the ones that stayed.</summary>
    public async Task<IReadOnlyList<string>> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        for (var i = _cleanup.Count - 1; i >= 0; i--)
        {
            var (entity, id) = _cleanup[i];
            try
            {
                await _backend.DeleteAsync(entity, id, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Cleanup deleted {Entity} {Id}.", entity, id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                failures.Add(entity + " " + id + ": " + ex.Message);
            }
        }

        _cleanup.Clear();
        return failures;
    }

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw new CommandFailedException("'" + id + "' is not a record id (a GUID).");

    private static string AsText(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => value.GetString() ?? string.Empty,
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => string.Empty,
        System.Text.Json.JsonValueKind.True => "true",
        System.Text.Json.JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    // Subscribed on every page the case opens, so 'check console' and the report see errors of all of them.
    private void Watch(IPage page)
    {
        page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                _consoleErrors.Add(message.Text);
            }
        };
        page.PageError += (_, error) => _consoleErrors.Add(error);
    }

    /// <summary>Runtime timeouts and continuation behavior for this case.</summary>
    public RunSettings Settings { get; }

    /// <summary>Static catalog used to bind configured page controls to live elements.</summary>
    public ElementCatalog Catalog { get; }

    /// <summary>Named scalar values resolved by command arguments.</summary>
    public IReadOnlyDictionary<string, string> StaticValues { get; }

    /// <summary>Configured workflows used when creating business objects.</summary>
    public IReadOnlyDictionary<string, ObjectDefinition> Objects { get; }

    /// <summary>User whose browser context currently owns the open page, if one is selected.</summary>
    public string? CurrentUser => _currentUser;

    /// <summary>Configured page currently bound to the browser, or null for a raw analysis address.</summary>
    public string? CurrentPage { get; private set; }

    /// <summary>Switches to a user's shared authenticated context and discards the prior user's open page.</summary>
    public async Task UseUserAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.Equals(_currentUser, username, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A page belongs to the previous user's context, so it cannot survive the switch.
        await ClosePageAsync().ConfigureAwait(false);

        await _pool.GetContextAsync(username, cancellationToken).ConfigureAwait(false);
        _currentUser = username;
        CurrentPage = null;

        _logger.LogDebug("Session now runs as {User}.", username);
    }

    /// <summary>
    /// Opens a configured page and waits for both the loading overlay and Freedom UI's control count
    /// to settle. The explicit Playwright default timeout prevents missing sub-elements from using
    /// Playwright's longer 30-second default.
    /// </summary>
    public async Task OpenPageAsync(
        string pageName,
        string? recordId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("No user is selected; 'use' must run before a page is opened.");
        }

        if (!Catalog.TryGetPage(pageName, out var page))
        {
            throw new InvalidOperationException("Page '" + pageName + "' is not configured.");
        }

        // The page's generation decides how the framework waits for it to be built.
        var runtime = PageRuntimes.For(page);
        var url = runtime.BuildUrl(page.Url, recordId);

        await ClosePageAsync().ConfigureAwait(false);

        var context = await _pool.GetContextAsync(_currentUser, cancellationToken).ConfigureAwait(false);
        _page = await context.NewPageAsync().ConfigureAwait(false);
        _page.SetDefaultTimeout(Settings.ElementTimeoutMs);
        Watch(_page);

        await _page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = Settings.PageLoadTimeoutMs
        }).ConfigureAwait(false);

        await runtime.WaitForReadyAsync(_page, page, Settings, _logger, cancellationToken).ConfigureAwait(false);

        CurrentPage = page.Name;
        _logger.LogDebug("Opened {Page} at {Url}.", page.Name, url);
    }

    /// <summary>Opens an unconfigured raw path for analysis and leaves no configured current page bound.</summary>
    public async Task NavigateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("No user is selected; 'use' must run before navigating.");
        }

        var url = path.StartsWith('/') ? path : "/" + path;

        await ClosePageAsync().ConfigureAwait(false);

        var context = await _pool.GetContextAsync(_currentUser, cancellationToken).ConfigureAwait(false);
        _page = await context.NewPageAsync().ConfigureAwait(false);
        _page.SetDefaultTimeout(Settings.ElementTimeoutMs);
        Watch(_page);

        await _page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = Settings.PageLoadTimeoutMs
        }).ConfigureAwait(false);

        // No configured page, so the generation is read off the address itself.
        await PageRuntimes.ForAddress(url)
            .WaitForReadyAsync(_page, null, Settings, _logger, cancellationToken).ConfigureAwait(false);

        // The address is not a configured page, so there is no page name to report.
        CurrentPage = null;
        _logger.LogDebug("Navigated to {Url}.", url);
    }

    /// <summary>Rebinds the current page after Creatio navigates itself, without issuing another navigation.</summary>
    public async Task AdoptPageAsync(string pageName, CancellationToken cancellationToken = default)
    {
        if (_page is null)
        {
            throw new InvalidOperationException("Nothing is open, so there is no page to adopt.");
        }

        if (!Catalog.TryGetPage(pageName, out var page))
        {
            throw new InvalidOperationException("Page '" + pageName + "' is not configured.");
        }

        await PageRuntimes.For(page)
            .WaitForReadyAsync(_page, page, Settings, _logger, cancellationToken).ConfigureAwait(false);

        // Elements on no tab come first: a control on a closed tab is legitimately absent.
        var sample = Catalog.Entries
            .Where(e => string.Equals(e.Page, pageName, StringComparison.OrdinalIgnoreCase) && e.Supports(typeof(IExistable)))
            .OrderBy(e => string.IsNullOrWhiteSpace(e.Definition.Tab) ? 0 : 1)
            .Take(PageProbe.Sample)
            .ToArray();
        var probes = sample.Select(e => (IExistable)_factory.Create(e.Definition, page, _page, Settings)).ToArray();
        if (!await PageProbe.AnyPresentAsync(probes, Settings.PageCheckTimeoutMs, cancellationToken).ConfigureAwait(false))
        {
            throw new CommandFailedException(
                "the browser is not on '" + pageName + "': none of " + string.Join(", ", sample.Select(e => e.Code)) +
                " appeared within " + Settings.PageCheckTimeoutMs + " ms (" + _page.Url + ").");
        }

        CurrentPage = pageName;
        _logger.LogDebug("Current page is now treated as {Page} ({Url}).", pageName, _page.Url);
    }

    /// <summary>Returns the open Playwright page or explains that a command must open one first.</summary>
    public IPage RequirePage() =>
        _page ?? throw new InvalidOperationException("No page is open; 'open' must run first.");

    /// <summary>Creates a live element from a target that validation resolved statically.</summary>
    public IUiElement Materialize(ResolvedTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Catalog.TryGetPage(target.Definition.Page, out var pageDefinition);
        return _factory.Create(target.Definition, pageDefinition, RequirePage(), Settings);
    }

    private async Task ClosePageAsync()
    {
        if (_page is null)
        {
            return;
        }

        try
        {
            await _page.CloseAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Closing the page failed.");
        }

        _page = null;
        CurrentPage = null;
    }

    /// <summary>Closes this test case's page while retaining shared contexts in the session pool.</summary>
    public async ValueTask DisposeAsync() => await ClosePageAsync().ConfigureAwait(false);
}
