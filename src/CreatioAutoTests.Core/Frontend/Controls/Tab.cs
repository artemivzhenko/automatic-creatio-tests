using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Controls;

/// <summary>
/// Tab header of a Freedom UI tab panel (crt-tab-panel-header-item).
///
/// The class implements only <see cref="IClickable"/> on top of the base element behaviour, which is
/// all it takes for the existing 'click' and 'check' commands to accept it: both declare the
/// capability they need rather than a list of concrete classes.
/// </summary>
[UiElement("Tab", HostTag = "crt-tab-panel-header-item")]
public sealed class Tab : UiElementBase, IClickable
{
    private const string HeaderSelector = ".tab-header";
    private const string LabelSelector = ".tab-label";
    private const int ContentSettleMs = 1000;

    /// <summary>Initializes a clickable tab header for its configured page control.</summary>
    public Tab(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Fallback caption element when the header title attribute is unavailable.</summary>
    protected override ILocator LabelLocator => Root.Locator(LabelSelector);

    private ILocator Header => Root.Locator(HeaderSelector);

    /// <summary>
    /// Reads the caption from the header's title attribute rather than its text.
    /// Creatio upper-cases tab captions in CSS, so the rendered text of a tab called
    /// "Contact info" is "CONTACT INFO"; the attribute keeps the original wording.
    /// </summary>
    public override async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await Header.CountAsync().ConfigureAwait(false) > 0)
            {
                var title = await Header.First.GetAttributeAsync("title").ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return NormalizeLabel(title);
                }
            }
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the title attribute of tab {Code} failed.", Code);
        }

        return await base.GetActualLabelAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Activates the tab for <c>click</c> and waits for its newly visible panel content.</summary>
    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var target = await Header.CountAsync().ConfigureAwait(false) > 0 ? Header.First : Root;

        await target.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        // Switching a tab swaps the panel content; give it a moment before the next command looks at it.
        await Page.WaitForTimeoutAsync(ContentSettleMs).ConfigureAwait(false);
    }
}
