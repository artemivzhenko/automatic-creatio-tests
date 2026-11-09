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
