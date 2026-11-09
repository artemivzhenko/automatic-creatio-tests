using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Runtime;

/// <summary>
/// Classic UI inside the 8.x Shell: the page is ready when the loading overlay is gone, Ext JS is
/// up, and the number of <c>-el</c> control nodes under the page schema has stopped growing.
///
/// Verified on Creatio 8.3: the Shell renders a Classic page in the same document, with no iframe,
/// and the <c>/0/Nui/ViewModule.aspx</c> address redirects to the Shell, so the address and the
/// overlay are the same as for Freedom UI. What differs is only the signal that the form is built.
/// </summary>
public sealed class ClassicUiRuntime : IPageRuntime
{
    private const int PollMs = 400;
    private const int MaxRenderWaitMs = 20_000;

    /// <summary>
    /// How many identical counts in a row mean the form has settled. Classic renders in bursts as
    /// modules load, with pauses long enough to fool a single repeated count.
    /// </summary>
    private const int StablePolls = 3;

    /// <inheritdoc />
    public UiGeneration Generation => UiGeneration.ClassicUI;

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

        // Every Classic control is counted, not only the page's own: the actions dashboard and the
        // details are separate modules that finish after the page, and a case may ask for them
        // first. The page schema only decides when counting is allowed to start.
        var schema = page?.Schema?.Trim() ?? string.Empty;
        var pageSelector = schema.Length > 0 ? "[id^=\"" + schema + "\"][id$=\"-el\"]" : "[id$=\"-el\"]";
        const string allSelector = "[id$=\"-el\"]";

        var deadline = DateTime.UtcNow.AddMilliseconds(MaxRenderWaitMs);
        var previous = -1;
        var stable = 0;

        while (DateTime.UtcNow < deadline)
        {
            int current;
            try
            {
                current = await browserPage.EvaluateAsync<int>(
                    @"([own, all]) => {
                        if (!(window.Ext && Ext.getCmp)) { return 0; }
                        return document.querySelectorAll(own).length > 0 ? document.querySelectorAll(all).length : 0;
                    }", new[] { pageSelector, allSelector }).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                return;
            }

            if (current > 0 && current == previous)
            {
                if (++stable >= StablePolls)
                {
                    logger.LogDebug("Classic page settled with {Count} control(s).", current);
                    return;
                }
            }
            else
            {
                stable = 0;
            }

            previous = current;
            await browserPage.WaitForTimeoutAsync(PollMs).ConfigureAwait(false);
        }

        logger.LogWarning("Classic page was still rendering after {Timeout} ms; continuing anyway.", MaxRenderWaitMs);
    }
}
