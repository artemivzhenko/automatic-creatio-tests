namespace CreatioAutoTests.Configuration;

/// <summary>Behavioural knobs for a whole run. Loaded from run.config.json, overridable from the CLI.</summary>
public sealed class RunSettings
{
    /// <summary>Run the browser without a visible window.</summary>
    public bool Headless { get; set; }

    /// <summary>
    /// When false (default) a test case stops at the first failed command.
    /// When true the executor keeps going and reports every failure of the case.
    /// </summary>
    public bool ContinueOnFailure { get; set; }

    /// <summary>Timeout for locating a single UI element, milliseconds.</summary>
    public int ElementTimeoutMs { get; set; } = 15_000;

    /// <summary>
    /// Budget for confirming that something is absent, milliseconds. Far shorter than
    /// <see cref="ElementTimeoutMs"/>, because absence is the expected answer and waiting the full
    /// element timeout for it makes negative assertions and diagnostics impractical.
    /// </summary>
    public int AbsenceTimeoutMs { get; set; } = 1500;

    /// <summary>Timeout for the Creatio loading overlay to disappear, milliseconds.</summary>
    public int PageLoadTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Run the analysis scripts before the test cases and reload the catalog from what they wrote.
    ///
    /// Off by default. Once pages are described the configuration is committed and reused, so
    /// re-analysing on every run costs a browser session per page for nothing; a pipeline wants it
    /// on only when a page is known to have changed.
    /// </summary>
    public bool RunAnalysis { get; set; }

    /// <summary>Directory the run report is written to.</summary>
    public string ReportsDirectory { get; set; } = "Reports";

    /// <summary>
    /// Formats written after a run. Supported values are text, junit and html; text remains the
    /// default so existing command lines produce the same report.
    /// </summary>
    public List<string> Reports { get; set; } = new() { "text" };

    /// <summary>Test case files starting with this prefix are skipped.</summary>
    public string SkipFilePrefix { get; set; } = "tmp";
}
