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
}

/// <summary>Login and password of a single Creatio user.</summary>
public sealed class UserCredentials
{
    /// <summary>Configured name used by scripts to select this credential pair.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password submitted when authenticating as this configured user.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Formats credentials as the safe user name rather than exposing the password.</summary>
    public override string ToString() => Username;
}

/// <summary>Root of creatio.site.config.json: service paths shared by all environments.</summary>
public sealed class SiteConfig
{
    /// <summary>Site-relative Creatio authentication service path.</summary>
    public string AuthPath { get; set; } = "/ServiceModel/AuthService.svc/Login";

    /// <summary>Optional site-relative OData service root.</summary>
    public string? ODataBasePath { get; set; }

    /// <summary>Optional site-relative endpoint for starting a process.</summary>
    public string? ProcessStartPath { get; set; }
}
