namespace CreatioAutoTests.Configuration;

/// <summary>Root of creatio.env.config.json: the instance under test and its users.</summary>
public sealed class EnvironmentConfig
{
    public string BaseUrl { get; set; } = string.Empty;

    public string? AuthUrl { get; set; }

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
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public override string ToString() => Username;
}

/// <summary>Root of creatio.site.config.json: service paths shared by all environments.</summary>
public sealed class SiteConfig
{
    public string AuthPath { get; set; } = "/ServiceModel/AuthService.svc/Login";

    public string? ODataBasePath { get; set; }

    public string? ProcessStartPath { get; set; }
}
