using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Session;

/// <summary>
/// Keeps one browser context per user for the whole run. Re-using the context is what makes
/// switching users cheap and keeps each user's storage isolated from the others.
/// </summary>
public sealed class BrowserSessionPool : IAsyncDisposable
{
    private readonly IBrowser _browser;
    private readonly CreatioAuthenticator _authenticator;
    private readonly ILogger<BrowserSessionPool> _logger;
    private readonly Dictionary<string, IBrowserContext> _contexts;

    public BrowserSessionPool(
        IBrowser browser,
        CreatioAuthenticator authenticator,
        ILogger<BrowserSessionPool> logger)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contexts = new Dictionary<string, IBrowserContext>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the context for a user, creating and seeding it on first use.</summary>
    public async Task<IBrowserContext> GetContextAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (_contexts.TryGetValue(username, out var existing))
        {
            return existing;
        }

        var user = await _authenticator.AuthenticateAsync(username, cancellationToken).ConfigureAwait(false);
        var baseUri = new Uri(_authenticator.BaseUrl);

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _authenticator.BaseUrl
        }).ConfigureAwait(false);

        var cookies = user.Cookies.Select(pair => new Cookie
        {
            Name = pair.Key,
            Value = pair.Value,
            Domain = baseUri.Host,
            Path = "/"
        });

        await context.AddCookiesAsync(cookies).ConfigureAwait(false);

        _contexts[username] = context;
        _logger.LogDebug("Opened browser context for {User}.", username);

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts.Values)
        {
            try
            {
                await context.CloseAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                _logger.LogDebug(ex, "Closing a browser context failed.");
            }
        }

        _contexts.Clear();
    }
}
