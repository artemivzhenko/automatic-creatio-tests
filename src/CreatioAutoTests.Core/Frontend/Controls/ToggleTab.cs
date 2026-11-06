using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Controls;

/// <summary>
/// Toggle tab group (crt-button-toggle-group).
///
/// The same idea as <see cref="Tab"/> to a person, but built the other way round in the DOM. A normal
/// tab panel gives every tab its own <c>element-name</c>, so each tab is an element in its own right.
/// A toggle group has one <c>element-name</c> for the whole group and names the tabs inside it only
/// by their captions, so the group is the element and the selected tab is its value.
///
/// That is why it is a value element rather than a clickable one: <c>set X "Tab 2"</c> switches tab
/// and <c>check-value X eq "Tab 2"</c> asserts which one is open, both through commands that already
/// exist.
/// </summary>
[UiElement("ToggleTab",
    HostTag = "crt-button-toggle-group",
    ValueKind = ValueKind.Tab,
    Options = typeof(ToggleTabOptions))]
public sealed class ToggleTab : UiElementBase, IValueElement
{
    /// <summary>
    /// One tab of the group. The caption lives in the marker rather than in the text because Creatio
    /// styles these captions, and the marker is what survives a change of styling.
    /// </summary>
    internal const string ItemSelector = "crt-button[data-item-marker]";

    /// <summary>The wrapper Creatio marks when its tab is the open one.</summary>
    private const string SelectedItemSelector = ".button-toggle.selected " + ItemSelector;

    private const string MarkerAttribute = "data-item-marker";

    /// <summary>Switching a tab swaps the content below it; the next command must not race that.</summary>
    private const int ContentSettleMs = 1000;

    private readonly ToggleTabOptions _options;

    /// <summary>Initializes the group against its configured page control.</summary>
    /// <param name="context">Page, definition and run settings for this element.</param>
    public ToggleTab(ElementRuntimeContext context) : base(context)
    {
        _options = context.Definition.BindOptions<ToggleTabOptions>();
    }

    /// <summary>Tabs declared in the page file, in the order they are expected on screen.</summary>
    public IReadOnlyList<string> ExpectedTabs => _options.Tabs;

    /// <summary>Reads every tab the group currently renders, in display order.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<string>> GetTabsAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await ReadMarkersAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The caption of the open tab, or an empty string when the group has not been touched yet.
    ///
    /// Creatio marks nothing as selected until a tab is clicked, even though one of them is showing,
    /// so an empty answer here is a real state and not a failure to read.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="InvalidOperationException">The group is not on the open page.</exception>
    public async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var selected = Root.Locator(SelectedItemSelector);

        try
        {
            if (await selected.CountAsync().ConfigureAwait(false) == 0)
            {
                return string.Empty;
            }

            var marker = await selected.First.GetAttributeAsync(MarkerAttribute).ConfigureAwait(false);
            return marker?.Trim() ?? string.Empty;
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the open tab of group {Code} failed.", Code);
            return string.Empty;
        }
    }

    /// <summary>Switches to the named tab, failing with the captions the group actually offers.</summary>
    /// <param name="value">Caption of the tab to open.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <exception cref="InvalidOperationException">
    /// The group is not on the open page, or has no tab with that caption.
    /// </exception>
    public async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var wanted = (value ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            throw new InvalidOperationException(
                "Toggle tab group '" + Code + "' needs the caption of the tab to open.");
        }

        var item = Root.Locator(ItemSelector + "[" + MarkerAttribute + "=\"" + wanted + "\"]");

        if (await item.CountAsync().ConfigureAwait(false) == 0)
        {
            var offered = await ReadMarkersAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Toggle tab group '" + Code + "' has no tab '" + wanted + "'. It offers: " +
                (offered.Count == 0 ? "(none)" : string.Join(", ", offered.Select(t => "'" + t + "'"))) + ".");
        }

        // The marker sits on the crt-button host, but only the button it wraps takes the click:
        // clicking the host is accepted by Playwright and quietly changes nothing.
        var inner = item.First.Locator("button");
        var target = await inner.CountAsync().ConfigureAwait(false) > 0 ? inner.First : item.First;

        await target.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(ContentSettleMs).ConfigureAwait(false);

        var opened = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        if (string.Equals(opened, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Creatio marks a tab as selected only once it has been clicked in this session, so a group
        // showing its default tab marks nothing at all. Clicking the tab that is already open
        // therefore leaves no mark, and an unmarked group means the click changed nothing -- which is
        // the right outcome. A different tab being marked is a real failure.
        if (opened.Length == 0)
        {
            Logger.LogDebug("Tab '{Tab}' of group {Code} was already open.", wanted, Code);
            return;
        }

        throw new InvalidOperationException(
            "Toggle tab group '" + Code + "' still shows '" + opened + "' after '" + wanted + "' was clicked.");
    }

    /// <summary>Reads the tab captions in one call, in the order the group renders them.</summary>
    private async Task<IReadOnlyList<string>> ReadMarkersAsync()
    {
        try
        {
            var markers = await Root.EvaluateAsync<string[]>(
                @"(el, selector) => [...el.querySelectorAll(selector)]
                    .map(i => (i.getAttribute('data-item-marker') || '').trim())
                    .filter(m => m.length > 0)",
                ItemSelector).ConfigureAwait(false);

            return markers ?? Array.Empty<string>();
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the tabs of group {Code} failed.", Code);
            return Array.Empty<string>();
        }
    }
}
