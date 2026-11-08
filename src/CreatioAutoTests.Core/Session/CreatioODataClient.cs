using System.Net;
using System.Text.Json;
using System.Web;
using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Session;

/// <summary>
/// Reads Creatio data over OData as an authenticated user.
///
/// The framework drives a browser for everything a test asserts. This exists for the questions a
/// browser cannot answer cheaply, such as which page schemas an instance has — knowledge you need
/// before you can analyse a page at all.
/// </summary>
public sealed class CreatioODataClient : IDisposable
{
    private readonly EnvironmentConfig _environment;
    private readonly SiteConfig _site;
    private readonly ILogger<CreatioODataClient> _logger;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _client;

    private bool _authenticated;

    /// <summary>Creates a client for the configured instance.</summary>
    /// <param name="environment">Instance address and the users that may sign in to it.</param>
    /// <param name="site">Service paths shared by every environment.</param>
    /// <param name="logger">Receives the progress of each request.</param>
    public CreatioODataClient(
        EnvironmentConfig environment,
        SiteConfig site,
        ILogger<CreatioODataClient> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        BaseUrl = _environment.BaseUrl.TrimEnd('/');

        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _client = new HttpClient(_handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    /// <summary>Address of the Creatio instance, without a trailing slash.</summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Signs in, once per client. Creatio answers 200 even for a rejected login, so the verdict is
    /// taken from the body rather than the status code.
    /// </summary>
    /// <param name="username">User to sign in as, or null for the first configured user.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="InvalidOperationException">The user is unknown, or Creatio refused the login.</exception>
    public async Task AuthenticateAsync(string? username = null, CancellationToken cancellationToken = default)
    {
        if (_authenticated)
        {
            return;
        }

        var credentials = username is null
            ? _environment.Users.FirstOrDefault()
            : _environment.Users.FirstOrDefault(
                u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (credentials is null)
        {
            throw new InvalidOperationException(
                "User '" + (username ?? "(first configured)") + "' is not in the environment configuration.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            UserName = credentials.Username,
            UserPassword = credentials.Password,
        });

        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await _client
            .PostAsync(BuildAuthUri(), content, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var parsed = JsonDocument.Parse(body);

        if (!parsed.RootElement.TryGetProperty("Code", out var code) || code.GetInt32() != 0)
        {
            var message = parsed.RootElement.TryGetProperty("Message", out var m) ? m.GetString() : body;
            throw new InvalidOperationException("Creatio refused the login: " + message);
        }

        _authenticated = true;
        _logger.LogDebug("Authenticated {User} for OData.", credentials.Username);
    }

    /// <summary>
    /// Reads an OData collection and returns the rows as parsed JSON.
    /// </summary>
    /// <param name="entity">Entity set, for example <c>SysSchema</c>.</param>
    /// <param name="select">Columns to return; all of them when null.</param>
    /// <param name="filter">OData filter expression, applied server side.</param>
    /// <param name="top">Maximum rows to return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="InvalidOperationException">The request failed, or the response was not OData.</exception>
    public async Task<IReadOnlyList<JsonElement>> QueryAsync(
        string entity,
        IEnumerable<string>? select = null,
        string? filter = null,
        int? top = null,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var query = new List<string>();
        if (select is not null)
        {
            query.Add("$select=" + string.Join(",", select));
        }

        if (filter is not null)
        {
            query.Add("$filter=" + HttpUtility.UrlEncode(filter));
        }

        if (top is not null)
        {
            query.Add("$top=" + top.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var path = (_site.ODataBasePath ?? "/0/odata").TrimEnd('/') + "/" + entity;
        if (query.Count > 0)
        {
            path += "?" + string.Join("&", query);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Add("Accept", "application/json");

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "OData request for '" + entity + "' failed with " + (int)response.StatusCode + ": " +
                error[..Math.Min(error.Length, 400)]);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("value", out var rows))
        {
            throw new InvalidOperationException("OData response for '" + entity + "' had no value array.");
        }

        return rows.EnumerateArray().Select(r => r.Clone()).ToArray();
    }

    /// <summary>
    /// Starts a process through <c>ProcessEngineService.svc/RunProcess</c> and returns what the
    /// engine answered, result parameters included. The CSRF cookie Creatio sets at login is echoed
    /// as a header, which is what every writing call needs.
    /// </summary>
    /// <param name="schemaName">Schema name of the process.</param>
    /// <param name="parameters">Values to pass in, as text; the engine converts them.</param>
    /// <param name="resultParameters">Codes of the parameters to read back.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<ProcessRunResult> RunProcessAsync(
        string schemaName,
        IReadOnlyDictionary<string, string> parameters,
        IEnumerable<string> resultParameters,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            schemaName,
            parameterValues = parameters.Select(p => new { name = p.Key, value = p.Value }).ToArray(),
            resultParameterNames = resultParameters.ToArray(),
        });

        var path = (_site.ProcessStartPath ?? "/0/ServiceModel/ProcessEngineService.svc/RunProcess").Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + (path.StartsWith('/') ? path : "/" + path));
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var csrf = _cookies.GetCookies(new Uri(BaseUrl))["BPMCSRF"]?.Value;
        if (!string.IsNullOrEmpty(csrf))
        {
            request.Headers.Add("BPMCSRF", csrf);
        }

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "RunProcess '" + schemaName + "' failed with " + (int)response.StatusCode + ": " +
                body[..Math.Min(body.Length, 400)]);
        }

        _logger.LogDebug("RunProcess {Process}: {Body}", schemaName, body[..Math.Min(body.Length, 400)]);
        return ProcessRunResult.Parse(schemaName, body);
    }

    private Uri BuildAuthUri()
    {
        if (!string.IsNullOrWhiteSpace(_environment.AuthUrl))
        {
            return new Uri(_environment.AuthUrl, UriKind.Absolute);
        }

        var path = _site.AuthPath.Trim();
        return new Uri(BaseUrl + (path.StartsWith('/') ? path : "/" + path), UriKind.Absolute);
    }

    /// <summary>Releases the underlying HTTP client.</summary>
    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }
}
