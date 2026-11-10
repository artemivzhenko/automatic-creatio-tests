using CreatioAutoTests.Frontend;
using CreatioAutoTests.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CreatioAutoTests.Cli;

/// <summary>
/// Standalone entry point. The framework itself has no test-runner dependency, so this is a thin
/// shell over <see cref="TestRunner"/>; an NUnit fixture is an alternative shell over the same API.
/// </summary>
public static class Program
{
    /// <summary>Runs CLI analysis, validation, or test execution and returns its process exit code.</summary>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        // 'analyse <path>' describes a page and writes its configuration; everything else runs tests.
        var analysing = args.Length > 0 &&
                        string.Equals(args[0], "analyse", StringComparison.OrdinalIgnoreCase);

        // 'schemas' answers the question you have before you can analyse anything: what are this
        // instance's pages called. The names are rarely what you would guess.
        var listingSchemas = args.Length > 0 &&
                             string.Equals(args[0], "schemas", StringComparison.OrdinalIgnoreCase);

        var root = GetOption(args, "--root") ?? Directory.GetCurrentDirectory();
        var filter = GetOption(args, "--filter");
        var validateOnly = args.Contains("--validate-only");
        var analyseFirst = args.Contains("--analyse") || args.Contains("--analyze");
        var analyseOnly = args.Contains("--analyse-only") || args.Contains("--analyze-only");
        var verbose = args.Contains("--verbose");

        try
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    if (args.Contains("--no-color"))
                    {
                        options.ColorBehavior = LoggerColorBehavior.Disabled;
                    }
                })
                .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information));

            services.AddCreatioAutoTests(root);

            await using var provider = services.BuildServiceProvider();

            if (listingSchemas)
            {
                return await ListSchemasAsync(provider, args).ConfigureAwait(false);
            }

            if (analysing)
            {
                return await AnalyseAsync(provider, args).ConfigureAwait(false);
            }

            var runner = provider.GetRequiredService<TestRunner>();

            return validateOnly
                ? Validate(runner, filter)
                : await RunAsync(provider, runner, filter,
                    analyseFirst || analyseOnly, analyseOnly).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("The run could not start: " + ex.Message);
            return 2;
        }
    }

    /// <summary>Lists the instance's Freedom UI page schemas and the address that opens each.</summary>
    private static async Task<int> ListSchemasAsync(IServiceProvider provider, string[] args)
    {
        var term = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : GetOption(args, "--filter");
        var suffix = args.Contains("--list-pages")
            ? SchemaDiscoveryService.ListPageSuffix
            : SchemaDiscoveryService.FormPageSuffix;

        var discovery = provider.GetRequiredService<SchemaDiscoveryService>();
        var schemas = args.Contains("--classic")
            ? await discovery.FindClassicAsync(term).ConfigureAwait(false)
            : await discovery.FindAsync(term, suffix).ConfigureAwait(false);

        if (schemas.Count == 0)
        {
            Console.WriteLine("No page schema matched.");
            return 0;
        }

        Console.WriteLine(schemas.Count + " page schema(s):");
        Console.WriteLine();

        foreach (var schema in schemas)
        {
            Console.WriteLine("  " + schema.Name);
            Console.WriteLine("      " + schema.AddressTemplate);
        }

        return 0;
    }

    /// <summary>Describes one page and writes Configuration/pages/&lt;Name&gt;.json.</summary>
    private static async Task<int> AnalyseAsync(IServiceProvider provider, string[] args)
    {
        var address = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : GetOption(args, "--path");
        if (string.IsNullOrWhiteSpace(address))
        {
            Console.Error.WriteLine("analyse needs a page address: analyse \"/0/Shell/#Card/Contacts_FormPage/edit/<id>\"");
            return 2;
        }

        var service = provider.GetRequiredService<PageAnalysisService>();

        var (analysis, written) = await service.AnalyseAsync(
            address,
            pageName: GetOption(args, "--name"),
            user: GetOption(args, "--user"),
            keepMissing: !args.Contains("--drop-missing"),
            includeHidden: args.Contains("--include-hidden"),
            walkTabs: !args.Contains("--active-tab-only"),
            dryRun: args.Contains("--dry-run")).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("page:   " + analysis.PageName);
        Console.WriteLine("type:   " + analysis.Generation.Name() + (analysis.Schema is null ? string.Empty : " (" + analysis.Schema + ")"));
        Console.WriteLine("url:    " + analysis.Url);
        Console.WriteLine("record: " + (analysis.RecordId ?? "(none)"));
        Console.WriteLine("found:  " + analysis.Elements.Count + " addressable control(s)");

        foreach (var group in analysis.ByType)
        {
            Console.WriteLine($"        {group.Count(),3}  {group.Key}");
        }

        // Which controls are writable decides what a test can actually do with the page, and it is
        // not in the configuration file, so it is only ever visible here.
        if (args.Contains("--list"))
        {
            Console.WriteLine();
            Console.WriteLine($"{"type",-10} {"code",-38} {"label",-26} flags");
            Console.WriteLine(new string('-', 96));

            foreach (var element in analysis.Elements)
            {
                var flags = string.Join(",", new[]
                {
                    element.Required ? "required" : null,
                    element.ReadOnly ? "readonly" : null,
                    element.Visible ? null : "hidden",
                }.Where(f => f is not null));

                var label = element.Label.Length > 26 ? element.Label[..26] : element.Label;
                Console.WriteLine($"{element.Type,-10} {element.Code,-38} {label,-26} {flags}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(args.Contains("--dry-run") ? "dry run: nothing was written" : "wrote " + written.FilePath);

        if (written.NoLongerOnPage.Count > 0)
        {
            var verb = written.KeptMissing ? "kept" : "dropped";
            Console.WriteLine();
            Console.WriteLine(written.NoLongerOnPage.Count + " configured entry(ies) not on the page, " + verb + ":");
            foreach (var entry in written.NoLongerOnPage)
            {
                Console.WriteLine($"  {entry.Type,-10} {entry.Code}");
            }
        }

        if (written.Pinned.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(written.Pinned.Count + " pinned entry(ies) left untouched:");
            foreach (var entry in written.Pinned)
            {
                Console.WriteLine($"  {entry.Type,-10} {entry.Code}");
            }
        }

        // A pinned entry has already had its decision made; it is not still open.
        var pinnedCodes = written.Pinned.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        var uncertain = analysis.Uncertain.Where(e => !pinnedCodes.Contains(e.Code)).ToArray();
        if (uncertain.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine(uncertain.Length + " control(s) need a human decision:");
            foreach (var element in uncertain)
            {
                Console.WriteLine("  " + element.Code + ": " + element.Uncertainty);
            }
        }

        var presenceOnly = analysis.PresenceOnly.Where(e => !pinnedCodes.Contains(e.Code)).ToArray();
        if (presenceOnly.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine(presenceOnly.Length + " control(s) are read-only and empty, so only their presence can be asserted:");
            foreach (var element in presenceOnly)
            {
                Console.WriteLine($"  {element.Type,-10} {element.Code,-38} {element.Label}");
            }
        }

        if (analysis.UnknownControls.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(analysis.UnknownControls.Count + " control(s) have no element type yet:");
            foreach (var control in analysis.UnknownControls)
            {
                Console.WriteLine($"  {control.HostTag,-34} {control.Code}");
            }
        }

        return 0;
    }

    private static async Task<int> RunAsync(
        IServiceProvider provider,
        TestRunner runner,
        string? filter,
        bool runAnalysis,
        bool analysisOnly)
    {
        var result = await runner
            .RunAsync(filter, runAnalysis ? true : null, analysisOnly)
            .ConfigureAwait(false);

        await provider.GetRequiredService<IReportWriter>().WriteAsync(result).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(TextReportWriter.Render(result));

        return result.Success ? 0 : 1;
    }

    /// <summary>Runs the validation pass only: useful as a fast pre-commit or CI lint step.</summary>
    private static int Validate(TestRunner runner, string? filter)
    {
        var files = runner.DiscoverTestFiles(filter).Concat(runner.DiscoverProcessFiles(filter)).ToList();
        var failed = 0;

        foreach (var file in files)
        {
            var plan = runner.Validate(file);

            foreach (var warning in plan.Warnings)
            {
                Console.WriteLine("warning: " + warning.Message);
            }

            if (plan.IsValid)
            {
                Console.WriteLine("ok    " + plan.TestName + " (" + plan.Steps.Count + " commands)");
                continue;
            }

            failed++;
            Console.WriteLine("FAIL  " + plan.TestName);
            Console.WriteLine(plan.DescribeIssues());
        }

        Console.WriteLine();
        Console.WriteLine(files.Count + " case(s) checked, " + failed + " with problems.");

        return failed == 0 ? 0 : 1;
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("creatio-tests - runs .act test cases against a Creatio instance");
        Console.WriteLine();
        Console.WriteLine("  schemas [<text>]        list this instance's Freedom UI page schemas");
        Console.WriteLine("      --list-pages         list section pages instead of form pages");
        Console.WriteLine("      --classic            list Classic UI pages (…Page, …PageV2) instead");
        Console.WriteLine();
        Console.WriteLine("  analyse \"<page path>\"   describe a page and write Configuration/pages/<Name>.json");
        Console.WriteLine("                          a #CardModuleV2/<Schema> address is analysed as a Classic UI page");
        Console.WriteLine("      --name <name>        page name for the DSL; defaults to the schema in the path");
        Console.WriteLine("      --user <username>    user to open the page as; defaults to the first configured");
        Console.WriteLine("      --drop-missing       remove configured entries the page no longer renders");
        Console.WriteLine("      --include-hidden     keep controls that are not currently visible");
        Console.WriteLine("      --active-tab-only    do not open the other tabs while analysing");
        Console.WriteLine("      --list               list every control found, with its flags");
        Console.WriteLine("      --dry-run            report findings, write nothing");
        Console.WriteLine();
        Console.WriteLine("  --root <dir>       working directory holding Configuration/ and Cases/");
        Console.WriteLine("  --filter <text>    run only cases whose file name contains <text>");
        Console.WriteLine("  --validate-only    check the cases without opening a browser");
        Console.WriteLine("  --analyse          run the analysis scripts first, then the cases");
        Console.WriteLine("  --analyse-only     run the analysis scripts and stop");
        Console.WriteLine("  --verbose          debug level logging");
        Console.WriteLine("  --help, -h         this text");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 success, 1 test failures, 2 the run could not start.");
    }
}
