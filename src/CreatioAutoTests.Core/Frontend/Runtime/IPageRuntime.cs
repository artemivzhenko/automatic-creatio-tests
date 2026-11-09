using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Runtime;

/// <summary>
/// What differs between the UI generations at the page level: how to tell that a page has finished
/// rendering. Element lookup is the element classes' business; this is the session's.
///
/// Both generations share the Shell, so the address of a record is built the same way and the
/// loading overlay is the same element. What differs is the signal that the form itself is built,
/// because the overlay clears long before that.
/// </summary>
public interface IPageRuntime
{
    /// <summary>Generation this runtime drives.</summary>
    UiGeneration Generation { get; }

    /// <summary>Address of a page, optionally of one record of it, relative to the base URL.</summary>
    string BuildUrl(string pageUrl, string? recordId);

    /// <summary>
    /// Waits until the page is ready for commands. <paramref name="page"/> is the configured page
    /// when one is known; a raw address opened for analysis has none.
    /// </summary>
    Task WaitForReadyAsync(
        IPage browserPage,
        PageDefinition? page,
        RunSettings settings,
        ILogger logger,
        CancellationToken cancellationToken = default);
}

/// <summary>Picks the runtime for a page or for a raw address.</summary>
public static class PageRuntimes
{
    private static readonly IPageRuntime Freedom = new FreedomUiRuntime();
    private static readonly IPageRuntime Classic = new ClassicUiRuntime();

    /// <summary>The runtime of a generation.</summary>
    public static IPageRuntime For(UiGeneration generation) =>
        generation == UiGeneration.ClassicUI ? Classic : Freedom;

    /// <summary>The runtime of a configured page.</summary>
    public static IPageRuntime For(PageDefinition? page) =>
        For(page?.Generation ?? UiGeneration.FreedomUI);

    /// <summary>
    /// The runtime for an address that is not a configured page. The Shell routes Classic UI pages
    /// through <c>CardModuleV2</c> and <c>SectionModuleV2</c>, and the pre-Shell host is <c>Nui</c>;
    /// anything else is Freedom UI.
    /// </summary>
    public static IPageRuntime ForAddress(string address)
    {
        var text = address ?? string.Empty;
        var classic = text.Contains("CardModuleV2", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("SectionModuleV2", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("/Nui/", StringComparison.OrdinalIgnoreCase);
        return classic ? Classic : Freedom;
    }
}
