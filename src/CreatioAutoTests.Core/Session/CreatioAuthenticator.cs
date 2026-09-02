using System.Net;
using System.Text;
using System.Text.Json;
using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Session;

/// <summary>Cookies obtained for one Creatio user.</summary>
public sealed record AuthenticatedUser(string Username, IReadOnlyDictionary<string, string> Cookies);

/// <summary>
/// Logs a user in over the AuthService REST endpoint and keeps the cookies. The browser never sees
/// the login form: cookies are injected into a fresh context instead, which is both faster and
/// removes the login screen as a source of test failures.
/// </summary>
public sealed class CreatioAuthenticator : IDisposable
{
    private readonly EnvironmentConfig _environment;
    private readonly SiteConfig _site;
    private readonly ILogger<CreatioAuthenticator> _logger;
    private readonly Dictionary<string, AuthenticatedUser> _cache;

    public CreatioAuthenticator(
        EnvironmentConfig environment,
        SiteConfig site,
        ILogger<CreatioAuthenticator> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new Dictionary<string, AuthenticatedUser>(StringComparer.OrdinalIgnoreCase);

        BaseUrl = _environment.BaseUrl.TrimEnd('/');
    }

    public string BaseUrl { get; }

    /// <summary>Logs the user in, or returns the cookies obtained earlier in this run.</summary>
    public async Task<AuthenticatedUser> AuthenticateAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(username, out var cached))
        {
            return cached;
        }

        var credentials = _environment.Users.FirstOrDefault(
            u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (credentials is null)
        {
            throw new InvalidOperationException("User '" + username + "' is not in the environment configuration.");
        }

        var authenticated = await LoginAsync(credentials, cancellationToken).ConfigureAwait(false);
        _cache[username] = authenticated;

        return authenticated;
    }

    private async Task<AuthenticatedUser> LoginAsync(
        UserCredentials credentials,
        CancellationToken cancellationToken)
    {
        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        var payload = JsonSerializer.Serialize(new
        {
            UserName = credentials.Username,
            UserPassword = credentials.Password
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var authUri = BuildAuthUri();
        _logger.LogDebug("Authenticating {User} at {Uri}.", credentials.Username, authUri);

        using var response = await client.PostAsync(authUri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var cookies = cookieContainer
            .GetCookies(new Uri(BaseUrl))
            .Cast<Cookie>()
            .ToDictionary(c => c.Name, c => c.Value, StringComparer.OrdinalIgnoreCase);

        if (cookies.Count == 0)
        {
            throw new InvalidOperationException(
                "Creatio accepted the login for '" + credentials.Username + "' but returned no cookies.");
        }

        _logger.LogInformation("Authenticated {User}.", credentials.Username);
        return new AuthenticatedUser(credentials.Username, cookies);
    }

    private Uri BuildAuthUri()
    {
        if (!string.IsNullOrWhiteSpace(_environment.AuthUrl))
        {
            return new Uri(_environment.AuthUrl, UriKind.Absolute);
        }

        var path = _site.AuthPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return new Uri(BaseUrl + path, UriKind.Absolute);
    }

    public void Dispose() => _cache.Clear();
}
