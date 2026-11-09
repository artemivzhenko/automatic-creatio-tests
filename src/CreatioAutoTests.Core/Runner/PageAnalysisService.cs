using CreatioAutoTests.Analysis;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Runtime;
using CreatioAutoTests.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Runner;

/// <summary>
/// Runs an analysis on its own, outside a test case: a browser is opened, the page is described,
/// its configuration file is written, and everything is closed again.
///
/// The same work the 'analyse' DSL command performs, reachable from the CLI so a page can be
/// described before any test exists for it. The address decides the generation, and with it which
/// analyser describes the page.
/// </summary>
public sealed class PageAnalysisService
{
    private readonly FrameworkConfiguration _configuration;
    private readonly ConfigurationPaths _paths;
    private readonly PageAnalyzer _analyzer;
    private readonly ClassicPageAnalyzer _classicAnalyzer;
    private readonly PageConfigWriter _writer;
    private readonly CreatioAuthenticator _authenticator;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes the configuration, browser, analysis, and writing services used by CLI analysis.</summary>
    public PageAnalysisService(
        FrameworkConfiguration configuration,
        ConfigurationPaths paths,
        PageAnalyzer analyzer,
        ClassicPageAnalyzer classicAnalyzer,
        PageConfigWriter writer,
        CreatioAuthenticator authenticator,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _classicAnalyzer = classicAnalyzer ?? throw new ArgumentNullException(nameof(classicAnalyzer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>Opens a raw page address, analyzes its controls, and optionally writes its page configuration.</summary>
    public async Task<(PageAnalysis Analysis, PageConfigWriteResult Written)> AnalyseAsync(
        string address,
        string? pageName = null,
        string? user = null,
        bool keepMissing = true,
        bool includeHidden = false,
        bool walkTabs = true,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var path = new PagePath(address);
        var name = pageName ?? path.DefaultPageName;
        var username = user ?? _configuration.Environment.Users[0].DisplayName;

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _configuration.RunSettings.Headless
        }).ConfigureAwait(false);

        await using var pool = new BrowserSessionPool(
            browser,
            _authenticator,
            _loggerFactory.CreateLogger<BrowserSessionPool>(),
            _configuration.RunSettings.ViewportWidth,
            _configuration.RunSettings.ViewportHeight);

        var context = await pool.GetContextAsync(username, cancellationToken).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        // Without this, Playwright's own 30 s default applies and a missing sub-element blocks for
        // twice as long as the configured element timeout.
        page.SetDefaultTimeout(_configuration.RunSettings.ElementTimeoutMs);

        await page.GotoAsync(path.Raw, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = _configuration.RunSettings.PageLoadTimeoutMs
        }).ConfigureAwait(false);

        PageAnalysis analysis;
        if (path.Generation == UiGeneration.ClassicUI)
        {
            // The Classic runtime knows when the form is built; the schema from the address tells it
            // which controls to count.
            var definition = new PageDefinition { Name = name, Url = path.Url, Type = UiGenerations.ClassicUIName, Schema = path.Schema };
            await PageRuntimes.For(UiGeneration.ClassicUI)
                .WaitForReadyAsync(page, definition, _configuration.RunSettings, _loggerFactory.CreateLogger<PageAnalysisService>(), cancellationToken)
                .ConfigureAwait(false);

            analysis = await _classicAnalyzer
                .AnalyzeAsync(page, name, path.Url, path.RecordId, path.Schema, includeHidden, walkTabs, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await WaitForOverlayAsync(page).ConfigureAwait(false);

            analysis = await _analyzer
                .AnalyzeAsync(page, name, path.Url, path.RecordId, includeHidden, walkTabs, cancellationToken)
                .ConfigureAwait(false);
        }

        var written = dryRun
            ? new PageConfigWriteResult("(dry run)", analysis.Elements.Count, Array.Empty<ElementDefinition>(), false, Array.Empty<ElementDefinition>())
            : _writer.Write(analysis, _paths.PagesDirectory, keepMissing);

        return (analysis, written);
    }

    /// <summary>Freedom UI keeps rendering after networkidle, so the overlay is the real signal.</summary>
    private async Task WaitForOverlayAsync(IPage page)
    {
        try
        {
            await page.Locator("#loading-animation").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Detached,
                Timeout = _configuration.RunSettings.PageLoadTimeoutMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (PlaywrightException)
        {
        }

        await page.WaitForTimeoutAsync(3000).ConfigureAwait(false);
    }
}
