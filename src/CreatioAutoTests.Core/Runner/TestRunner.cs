using System.Diagnostics;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Session;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Runner;

/// <summary>
/// Ties the layers together: read configuration, log in, find test cases, validate every one of
/// them, then run the ones that validate. Knows nothing about NUnit, so it works the same from the
/// CLI, from a test fixture or from CI.
/// </summary>
public sealed class TestRunner
{
    /// <summary>File extension used to discover DSL scripts.</summary>
    public const string TestFileExtension = ".act";

    private readonly FrameworkConfiguration _configuration;
    private readonly UiElementFactory _elementFactory;
    private readonly ScriptParser _parser;
    private readonly ScriptValidator _validator;
    private readonly CommandExecutor _executor;
    private readonly CreatioAuthenticator _authenticator;
    private readonly CreatioODataClient _backend;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TestRunner> _logger;

    /// <summary>Initializes all services needed to validate and execute case and analysis scripts.</summary>
    public TestRunner(
        FrameworkConfiguration configuration,
        UiElementFactory elementFactory,
        ScriptParser parser,
        ScriptValidator validator,
        CommandExecutor executor,
        CreatioAuthenticator authenticator,
        CreatioODataClient backend,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<TestRunner>();
    }

    /// <summary>Analysis scripts, in the folders configured for them. Empty when none are set up.</summary>
    public IReadOnlyList<string> DiscoverAnalysisFiles() =>
        DiscoverIn(_configuration.AnalysisFolders, nameFilter: null, warnWhenMissing: true);

    /// <summary>Every .act file the configuration points at, skipping the temporary ones.</summary>
    public IReadOnlyList<string> DiscoverTestFiles(string? nameFilter = null) =>
        DiscoverIn(_configuration.CasesFolders, nameFilter, warnWhenMissing: true);

    /// <summary>
    /// Process cases: the backend tier, run after the UI cases. The folder is optional, so a
    /// missing one is not reported.
    /// </summary>
    public IReadOnlyList<string> DiscoverProcessFiles(string? nameFilter = null) =>
        DiscoverIn(_configuration.ProcessCasesFolders, nameFilter, warnWhenMissing: false);

    private IReadOnlyList<string> DiscoverIn(
        IReadOnlyList<string> folders,
        string? nameFilter,
        bool warnWhenMissing)
    {
        var files = new List<string>();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                if (warnWhenMissing)
                {
                    _logger.LogWarning("Folder '{Folder}' does not exist.", folder);
                }

                continue;
            }

            files.AddRange(Directory
                .GetFiles(folder, "*" + TestFileExtension)
                .Where(f => !Path.GetFileName(f).StartsWith(
                    _configuration.RunSettings.SkipFilePrefix,
                    StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            files = files
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Validates a single file without touching a browser. Exposed so a test fixture or CI step can
    /// lint the whole suite in milliseconds.
    /// </summary>
    public ExecutionPlan Validate(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var lines = File.ReadAllLines(filePath);
        var commands = _parser.Parse(lines);
        var context = new ValidationContext(
            _configuration.Catalog,
            _configuration.StaticValues,
            _configuration.UserNames,
            _configuration.Objects)
        {
            Processes = _configuration.Processes,
            Directives = ScriptParser.Directives(lines),
        };

        return _validator.Validate(name, filePath, commands, context);
    }

    /// <summary>
    /// Runs a suite. When analysis is enabled the analysis scripts go first and the catalog is
    /// reloaded from what they wrote, so the test cases are validated against fresh configuration
    /// rather than whatever was on disk when the process started.
    /// </summary>
    public async Task<RunResult> RunAsync(
        string? nameFilter = null,
        bool? runAnalysis = null,
        bool analysisOnly = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var wantAnalysis = runAnalysis ?? _configuration.RunSettings.RunAnalysis;

        var analysisFiles = wantAnalysis || analysisOnly
            ? DiscoverAnalysisFiles()
            : Array.Empty<string>();

        var testFiles = analysisOnly ? Array.Empty<string>() : DiscoverTestFiles(nameFilter);
        var processFiles = analysisOnly ? Array.Empty<string>() : DiscoverProcessFiles(nameFilter);

        if (analysisFiles.Count == 0 && testFiles.Count == 0 && processFiles.Count == 0)
        {
            _logger.LogWarning("Nothing to do: no {Extension} files were found.", TestFileExtension);
            return new RunResult { Cases = Array.Empty<TestCaseResult>(), Duration = stopwatch.Elapsed };
        }

        var analysisResults = new List<TestCaseResult>();
        var caseResults = new List<TestCaseResult>();

        // One browser serves every phase, launched the first time a case opens a page; a run of
        // process cases alone never launches it.
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);

        await using var pool = new BrowserSessionPool(
            _ => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _configuration.RunSettings.Headless
            }),
            _authenticator,
            _loggerFactory.CreateLogger<BrowserSessionPool>(),
            _configuration.RunSettings.ViewportWidth,
            _configuration.RunSettings.ViewportHeight);

        if (analysisFiles.Count > 0)
        {
            _logger.LogInformation("Analysis phase: {Count} script(s).", analysisFiles.Count);
            analysisResults.AddRange(
                await RunPlansAsync(analysisFiles, pool, cancellationToken).ConfigureAwait(false));

            if (analysisResults.Any(r => !r.Success))
            {
                _logger.LogError("Analysis failed; the test cases were not run.");
                stopwatch.Stop();
                return new RunResult
                {
                    Cases = caseResults,
                    AnalysisScripts = analysisResults,
                    Duration = stopwatch.Elapsed,
                };
            }

            // This is what lets analysis and testing share a run: the catalog now holds whatever
            // the analysis scripts just described.
            var catalog = _configuration.RefreshCatalog();
            _logger.LogInformation("Catalog reloaded: {Pages} page(s), {Elements} element(s).",
                catalog.Pages.Count, catalog.Entries.Count);
        }

        if (testFiles.Count > 0)
        {
            caseResults.AddRange(
                await RunPlansAsync(testFiles, pool, cancellationToken).ConfigureAwait(false));
        }

        if (processFiles.Count > 0)
        {
            _logger.LogInformation("Process cases: {Count} script(s).", processFiles.Count);
            caseResults.AddRange(
                await RunPlansAsync(processFiles, pool, cancellationToken).ConfigureAwait(false));
        }

        stopwatch.Stop();
        return new RunResult
        {
            Cases = caseResults,
            AnalysisScripts = analysisResults,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>Validates every file first, then runs the ones that validate.</summary>
    private async Task<List<TestCaseResult>> RunPlansAsync(
        IReadOnlyList<string> files,
        BrowserSessionPool pool,
        CancellationToken cancellationToken)
    {
        var plans = files.Select(Validate).ToArray();
        var results = new List<TestCaseResult>(plans.Length);

        foreach (var broken in plans.Where(p => !p.IsValid))
        {
            _logger.LogError("{Name} does not validate:{NewLine}{Issues}",
                broken.TestName, System.Environment.NewLine, broken.DescribeIssues());
        }

        foreach (var plan in plans)
        {
            results.Add(plan.IsValid
                ? await RunCaseAsync(plan, pool, cancellationToken).ConfigureAwait(false)
                : ToUnrunResult(plan));
        }

        return results;
    }

    private async Task<TestCaseResult> RunCaseAsync(
        ExecutionPlan plan,
        BrowserSessionPool pool,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running {Name} ({Steps} commands).", plan.TestName, plan.Steps.Count);

        await using var session = new ExecutionSession(
            pool,
            _elementFactory,
            _backend,
            _configuration.Catalog,
            _configuration.RunSettings,
            _configuration.StaticValues,
            _configuration.Objects,
            _configuration.Processes,
            _loggerFactory.CreateLogger<ExecutionSession>());

        try
        {
            return await _executor.ExecuteAsync(plan, session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Name} could not be run.", plan.TestName);

            return new TestCaseResult
            {
                Name = plan.TestName,
                FilePath = plan.FilePath,
                RunError = ex.GetType().Name + ": " + ex.Message
            };
        }
    }

    private static TestCaseResult ToUnrunResult(ExecutionPlan plan) => new()
    {
        Name = plan.TestName,
        FilePath = plan.FilePath,
        ValidationIssues = plan.Issues
    };
}
