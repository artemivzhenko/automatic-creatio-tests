using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Runtime;

/// <summary>
/// Freedom UI: the page is ready when the Shell's loading overlay is gone and the number of
/// <c>element-name</c> hosts has stopped growing.
/// </summary>
public sealed class FreedomUiRuntime : IPageRuntime
{
    private const int PollMs = 250;
    private const int MaxRenderWaitMs = 10_000;

    /// <inheritdoc />
    public UiGeneration Generation => UiGeneration.FreedomUI;

    /// <inheritdoc />
    public string BuildUrl(string pageUrl, string? recordId) => ShellAddress.WithRecord(pageUrl, recordId);

    /// <inheritdoc />
    public async Task WaitForReadyAsync(
        IPage browserPage,
        PageDefinition? page,
        RunSettings settings,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await ShellAddress.WaitForLoadingOverlayAsync(browserPage, settings, logger).ConfigureAwait(false);
        await WaitForRenderedAsync(browserPage, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the page stops adding controls.
    ///
    /// The loading overlay goes away long before Freedom UI has rendered the form, so 'open'
    /// followed immediately by a command used to look at a page that was still empty. Two identical
    /// counts in a row mean the render has settled.
    /// </summary>
    private static async Task WaitForRenderedAsync(IPage page, ILogger logger)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(MaxRenderWaitMs);
        var previous = -1;

        while (DateTime.UtcNow < deadline)
        {
            int current;
            try
            {
                current = await page.EvaluateAsync<int>(
                    "() => document.querySelectorAll('[element-name]').length").ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                return;
            }

            if (current > 0 && current == previous)
            {
                logger.LogDebug("Page settled with {Count} control(s).", current);
                return;
            }

            previous = current;
            await page.WaitForTimeoutAsync(PollMs).ConfigureAwait(false);
        }

        logger.LogWarning("Page was still rendering after {Timeout} ms; continuing anyway.", MaxRenderWaitMs);
    }
}

/// <summary>What both generations share because both live inside the 8.x Shell.</summary>
internal static class ShellAddress
{
    /// <summary>Appends <c>/edit/{id}</c> to a page template and makes the path base-relative.</summary>
    public static string WithRecord(string pageUrl, string? recordId)
    {
        var url = string.IsNullOrWhiteSpace(recordId) ? pageUrl : pageUrl.TrimEnd('/') + "/edit/" + recordId;
        return url.StartsWith('/') ? url : "/" + url;
    }

    /// <summary>Waits out the Creatio splash overlay; absence of the overlay is success too.</summary>
    public static async Task WaitForLoadingOverlayAsync(IPage page, RunSettings settings, ILogger logger)
    {
        try
        {
            await page.Locator("#loading-animation").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Detached,
                Timeout = settings.PageLoadTimeoutMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Creatio loading overlay was still visible after {Timeout} ms.", settings.PageLoadTimeoutMs);
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Waiting for the loading overlay failed.");
        }
    }
}
