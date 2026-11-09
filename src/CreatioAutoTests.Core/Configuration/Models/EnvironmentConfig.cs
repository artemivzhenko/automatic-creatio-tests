namespace CreatioAutoTests.Configuration;

/// <summary>Root of creatio.env.config.json: the instance under test and its users.</summary>
public sealed class EnvironmentConfig
{
    /// <summary>Base URL of the Creatio instance under test.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional absolute authentication endpoint that overrides the site-relative path.</summary>
    public string? AuthUrl { get; set; }

    /// <summary>Credentials selectable by the DSL <c>use</c> command.</summary>
    public List<UserCredentials> Users { get; set; } = new();

    /// <summary>Folders scanned for test cases.</summary>
    public List<string> CasesFolders { get; set; } = new();

    /// <summary>
    /// Folders scanned for analysis scripts: the ones that describe pages and write their
    /// configuration. They run before the test cases, and only when analysis is asked for.
    /// </summary>
    public List<string> AnalysisFolders { get; set; } = new();

    /// <summary>
    /// Folders scanned for process cases: scripts that start processes and read their results and
    /// side effects without a browser. They run after the UI cases. Defaults to ProcessCases.
    /// </summary>
    public List<string> ProcessCasesFolders { get; set; } = new();
}

/// <summary>Login and password of a single Creatio user.</summary>
public sealed class UserCredentials
{
    /// <summary>Login submitted to Creatio.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Optional name scripts use in <c>use</c> instead of the login. A case says <c>use Supervisor</c>
    /// and keeps working when the environment behind it logs in as <c>jane@example.com</c>: the
    /// alias is part of the case, the login is part of the environment.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>What a script writes after <c>use</c>: the alias when there is one, else the login.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Username : Name.Trim();

    /// <summary>Whether a <c>use</c> argument refers to this user, by alias or by login.</summary>
    public bool Matches(string name) =>
        string.Equals(DisplayName, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Username, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Password submitted when authenticating as this configured user.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Formats credentials as the safe user name rather than exposing the password.</summary>
    public override string ToString() => DisplayName;
}

/// <summary>Root of creatio.site.config.json: service paths shared by all environments.</summary>
public sealed class SiteConfig
{
    /// <summary>Site-relative Creatio authentication service path.</summary>
    public string AuthPath { get; set; } = "/ServiceModel/AuthService.svc/Login";

    /// <summary>Optional site-relative OData service root.</summary>
    public string? ODataBasePath { get; set; }

    /// <summary>Optional site-relative endpoint for starting a process; defaults to ProcessEngineService.svc/RunProcess.</summary>
    public string? ProcessStartPath { get; set; }
}
