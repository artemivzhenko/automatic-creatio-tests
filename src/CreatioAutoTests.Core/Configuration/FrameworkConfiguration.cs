using CreatioAutoTests.Frontend.Registry;

namespace CreatioAutoTests.Configuration;

/// <summary>Where each configuration file lives, relative to the working directory.</summary>
public sealed class ConfigurationPaths
{
    /// <summary>Relative path of the environment and user credentials file.</summary>
    public string Environment { get; set; } = "Configuration/creatio.env.config.json";

    /// <summary>Relative path of the site-wide service path file.</summary>
    public string Site { get; set; } = "Configuration/creatio.site.config.json";

    /// <summary>Directory holding one file per page, each with that page's elements.</summary>
    public string PagesDirectory { get; set; } = "Configuration/pages";

    /// <summary>Relative path of object creation workflow definitions.</summary>
    public string Objects { get; set; } = "Configuration/objects.config.json";

    /// <summary>Directory holding one file per process a script may start. Optional.</summary>
    public string ProcessesDirectory { get; set; } = "Configuration/processes";

    /// <summary>Relative path of named scalar values used by scripts and object workflows.</summary>
    public string StaticValues { get; set; } = "Configuration/static.values.json";

    /// <summary>Relative path of optional execution timeout and reporting settings.</summary>
    public string RunSettings { get; set; } = "Configuration/run.config.json";
}

/// <summary>
/// Everything read from disk for a run, validated as a whole. Building this is the first thing the
/// runner does, so a broken configuration is reported before a browser starts.
/// </summary>
public sealed class FrameworkConfiguration
{
    private readonly string _rootDirectory;
    private readonly ConfigurationPaths _paths;
    private readonly UiElementRegistry _registry;

    private FrameworkConfiguration(
        EnvironmentConfig environment,
        SiteConfig site,
        RunSettings runSettings,
        ElementCatalog catalog,
        IReadOnlyDictionary<string, string> staticValues,
        IReadOnlyDictionary<string, ObjectDefinition> objects,
        IReadOnlyDictionary<string, ProcessDefinition> processes,
        string rootDirectory,
        ConfigurationPaths paths,
        UiElementRegistry registry)
    {
        Environment = environment;
        Site = site;
        RunSettings = runSettings;
        Catalog = catalog;
        StaticValues = staticValues;
        Objects = objects;
        Processes = processes;
        _rootDirectory = rootDirectory;
        _paths = paths;
        _registry = registry;
    }

    /// <summary>
    /// Re-reads the pages directory. Called after the analysis phase so that test cases are
    /// validated against the configuration analysis has just written, not the one loaded at startup.
    /// </summary>
    public ElementCatalog RefreshCatalog()
    {
        var loader = new ConfigurationLoader(_rootDirectory);
        Catalog = ElementCatalog.Build(loader.LoadPages(_paths.PagesDirectory), _registry);
        return Catalog;
    }

    /// <summary>Target instance address and users loaded for this run.</summary>
    public EnvironmentConfig Environment { get; }

    /// <summary>Site-wide service path settings.</summary>
    public SiteConfig Site { get; }

    /// <summary>Optional runtime timeout, behavior, and report settings.</summary>
    public RunSettings RunSettings { get; }

    /// <summary>Current statically resolvable element catalog, refreshed after analysis when requested.</summary>
    public ElementCatalog Catalog { get; private set; }

    /// <summary>Named scalar values available to DSL commands and object creation.</summary>
    public IReadOnlyDictionary<string, string> StaticValues { get; }

    /// <summary>Object definitions keyed by name for the <c>create</c> command.</summary>
    public IReadOnlyDictionary<string, ObjectDefinition> Objects { get; }

    /// <summary>Process definitions keyed by schema name for the <c>start</c> command.</summary>
    public IReadOnlyDictionary<string, ProcessDefinition> Processes { get; }

    /// <summary>Folders scanned for process cases, defaulting to ProcessCases when nothing is configured.</summary>
    public IReadOnlyList<string> ProcessCasesFolders =>
        Environment.ProcessCasesFolders.Count > 0 ? Environment.ProcessCasesFolders : new List<string> { "ProcessCases" };

    /// <summary>Names a script may write after <c>use</c>: each user's alias, or its login when it has none.</summary>
    public IReadOnlyCollection<string> UserNames =>
        Environment.Users.Select(u => u.DisplayName).ToArray();

    /// <summary>Folders scanned for test cases, defaulting to Cases when nothing is configured.</summary>
    public IReadOnlyList<string> CasesFolders =>
        Environment.CasesFolders.Count > 0 ? Environment.CasesFolders : new List<string> { "Cases" };

    /// <summary>
    /// Folders scanned for analysis scripts. Empty when none are configured: analysis is opt-in,
    /// so a project that never describes pages automatically needs no such folder.
    /// </summary>
    public IReadOnlyList<string> AnalysisFolders => Environment.AnalysisFolders;

    /// <summary>Loads and validates all framework configuration rooted at the supplied working directory.</summary>
    public static FrameworkConfiguration Load(
        string rootDirectory,
        UiElementRegistry registry,
        ConfigurationPaths? paths = null)
    {
        paths ??= new ConfigurationPaths();
        var loader = new ConfigurationLoader(rootDirectory);

        var environment = loader.Load<EnvironmentConfig>(paths.Environment);
        if (string.IsNullOrWhiteSpace(environment.BaseUrl))
        {
            throw new InvalidOperationException("The environment configuration has no BaseUrl.");
        }

        if (environment.Users.Count == 0)
        {
            throw new InvalidOperationException("The environment configuration lists no users.");
        }

        var site = loader.Load<SiteConfig>(paths.Site);
        var runSettings = loader.LoadOrDefault<RunSettings>(paths.RunSettings);
        var staticValues = loader.LoadStaticValues(paths.StaticValues);

        var catalog = ElementCatalog.Build(loader.LoadPages(paths.PagesDirectory), registry);

        var objects = loader
            .LoadOrDefault<ObjectsConfig>(paths.Objects)
            .Objects
            .ToDictionary(o => o.Name, o => o, StringComparer.OrdinalIgnoreCase);

        var processes = loader
            .LoadProcesses(paths.ProcessesDirectory)
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        return new FrameworkConfiguration(
            environment, site, runSettings, catalog, staticValues, objects, processes,
            rootDirectory, paths, registry);
    }
}
