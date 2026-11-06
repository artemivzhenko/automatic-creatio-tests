using System.Text.Json;
using System.Text.RegularExpressions;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Composite;
using CreatioAutoTests.Frontend.Controls;
using CreatioAutoTests.Frontend.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Analysis;

/// <summary>
/// Reads an open Freedom UI page and reports the controls on it.
///
/// The set of tags it looks for comes from the element registry, so a new element type becomes
/// discoverable by declaring its HostTag and nothing else.
/// </summary>
public sealed partial class PageAnalyzer
{
    private const string TabHostTag = "crt-tab-panel-header-item";
    private const string ToggleTabHostTag = "crt-button-toggle-group";
    private const int ToggleClickTimeoutMs = 10_000;
    private const int ToggleSettleMs = 900;
    private const int ScrollSteps = 10;
    private const int ScrollStepPx = 1200;
    private const int ScrollSettleMs = 350;
    private const int TabSettleMs = 600;
    private const int TabPollMs = 200;
    private const int TabClickTimeoutMs = 10_000;
    private const int TabSwitchTimeoutMs = 10_000;
    private const int PanelClickTimeoutMs = 10_000;
    private const int PanelSettleMs = 1200;
    private const int PanelContentTimeoutMs = 10_000;
    private const int PanelPollMs = 400;

    private readonly HostTagIndex _index;
    private readonly ILogger<PageAnalyzer> _logger;

    /// <summary>Initializes registry-driven DOM tag discovery and analysis diagnostics.</summary>
    public PageAnalyzer(UiElementRegistry registry, ILogger<PageAnalyzer> logger)
    {
        _index = new HostTagIndex(registry ?? throw new ArgumentNullException(nameof(registry)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts controls from an open page and, when requested, visits every tab because Freedom UI
    /// renders only the open tab while retaining hidden containers for those already visited.
    /// </summary>
    public async Task<PageAnalysis> AnalyzeAsync(
        IPage page,
        string pageName,
        string url,
        string? recordId,
        bool includeHidden = false,
        bool walkTabs = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var script = BuildScript(_index.Tags.ToArray());

        var elements = new List<DiscoveredElement>();
        var unknown = new List<UnknownControl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await RevealLazyContentAsync(page).ConfigureAwait(false);

        var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
        Collect(raw, includeHidden, elements, unknown, seen);

        await WalkToggleTabsAsync(page, script, includeHidden, elements, unknown, seen).ConfigureAwait(false);

        if (walkTabs)
        {
            await WalkTabsAsync(page, script, includeHidden, elements, unknown, seen).ConfigureAwait(false);
        }

        await OpenPanelsAsync(page, script, includeHidden, elements, unknown, seen).ConfigureAwait(false);

        // One last look. Restoring the tab the page arrived on, and opening panels, both change what
        // is rendered, and whatever that revealed has not been collected yet.
        await RevealLazyContentAsync(page).ConfigureAwait(false);

        var final = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
        var beforeFinal = elements.Count;
        Collect(final, includeHidden, elements, unknown, seen);

        if (elements.Count > beforeFinal)
        {
            _logger.LogInformation("Final pass: {Count} more control(s).", elements.Count - beforeFinal);
        }

        _logger.LogInformation(
            "Analysed {Page}: {Known} addressable control(s), {Unknown} without an element type.",
            pageName, elements.Count, unknown.Count);

        return new PageAnalysis
        {
            PageName = pageName,
            Url = url,
            RecordId = recordId,
            Elements = elements
                .OrderBy(e => e.Type, StringComparer.Ordinal)
                .ThenBy(e => e.Code, StringComparer.Ordinal)
                .ToArray(),
            UnknownControls = unknown
                .DistinctBy(u => (u.HostTag, u.Code))
                .OrderBy(u => u.HostTag, StringComparer.Ordinal)
                .ThenBy(u => u.Code, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    /// <summary>Adds what one extraction pass found, ignoring controls already recorded.</summary>
    private void Collect(
        JsonElement raw,
        bool includeHidden,
        List<DiscoveredElement> elements,
        List<UnknownControl> unknown,
        HashSet<string> seen)
    {
        foreach (var item in raw.GetProperty("known").EnumerateArray())
        {
            var discovered = Map(item);
            if (discovered is null || seen.Contains(discovered.Code))
            {
                continue;
            }

            // Recorded, not merely met: a control the first pass finds on a tab that is not open is
            // hidden and must stay collectable, or whichever tab the page arrived on would silently
            // decide what the rest of the walk is allowed to find.
            if (includeHidden || discovered.Visible)
            {
                seen.Add(discovered.Code);
                elements.Add(discovered);
            }
        }

        foreach (var item in raw.GetProperty("unknown").EnumerateArray())
        {
            unknown.Add(new UnknownControl(Text(item, "tag"), Text(item, "code")));
        }
    }

    /// <summary>
    /// Visits every tab and analyses each one.
    ///
    /// Freedom UI renders only the open tab, so a single pass sees a fraction of the page: a form
    /// with its numbers on a second tab reports no numbers at all. Which tab is open is also
    /// remembered by Creatio between visits, so the starting point cannot be assumed.
    ///
    /// The walk keeps an explicit list of tabs and of the ones it has been on, verifies after every
    /// click that the tab actually changed, and stops when nothing new can be reached. The tab that
    /// was open at the start is restored at the end.
    /// </summary>
    private async Task WalkTabsAsync(
        IPage page,
        string script,
        bool includeHidden,
        List<DiscoveredElement> elements,
        List<UnknownControl> unknown,
        HashSet<string> seen)
    {
        var tabs = elements
            .Where(e => string.Equals(e.HostTag, TabHostTag, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tabs.Count < 2)
        {
            return;
        }

        var startedOn = await ActiveTabAsync(page).ConfigureAwait(false);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        if (startedOn is not null)
        {
            visited.Add(startedOn);
        }

        _logger.LogInformation("Page has {Count} tab(s): {Tabs}. Open on arrival: {Open}.",
            tabs.Count, string.Join(", ", tabs), startedOn ?? "unknown");

        foreach (var tab in tabs)
        {
            if (visited.Contains(tab))
            {
                _logger.LogDebug("Tab {Tab} was already read.", tab);
                continue;
            }

            if (!await OpenTabAsync(page, tab).ConfigureAwait(false))
            {
                _logger.LogWarning("Tab {Tab} would not open; anything only on it was not seen.", tab);
                visited.Add(tab);
                continue;
            }

            visited.Add(tab);

            await RevealLazyContentAsync(page).ConfigureAwait(false);

            var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
            var before = elements.Count;
            Collect(raw, includeHidden, elements, unknown, seen);

            _logger.LogInformation("Tab {Tab}: {Count} new control(s).", tab, elements.Count - before);

            // A toggle group is rendered only while its own tab is open, so its tabs are walked here
            // rather than once at the end.
            await WalkToggleTabsAsync(page, script, includeHidden, elements, unknown, seen)
                .ConfigureAwait(false);
        }

        var unreached = tabs.Where(t => !visited.Contains(t)).ToArray();
        if (unreached.Length > 0)
        {
            _logger.LogWarning("Tabs never read: {Tabs}.", string.Join(", ", unreached));
        }

        if (startedOn is not null && !string.Equals(await ActiveTabAsync(page).ConfigureAwait(false), startedOn))
        {
            await OpenTabAsync(page, startedOn).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Scrolls the open tab from top to bottom and back.
    ///
    /// Creatio renders a good deal of a page only once it comes near the viewport -- grids and the
    /// contents of expansion panels among them -- so how much of a page a single pass finds depends
    /// on the size of the window it is read in. Scrolling first makes the answer the same in a small
    /// window as in a large one.
    ///
    /// Driven by the wheel rather than by setting scrollTop: the page scrolls through a virtual
    /// viewport that reacts to wheel events, and moving its scroll position directly does nothing.
    /// </summary>
    private async Task RevealLazyContentAsync(IPage page)
    {
        try
        {
            var size = await page.EvaluateAsync<int[]>(
                "() => [window.innerWidth || 1280, window.innerHeight || 800]").ConfigureAwait(false);

            await page.Mouse.MoveAsync(size[0] / 2f, size[1] / 2f).ConfigureAwait(false);

            for (var i = 0; i < ScrollSteps; i++)
            {
                await page.Mouse.WheelAsync(0, ScrollStepPx).ConfigureAwait(false);
                await page.WaitForTimeoutAsync(ScrollSettleMs).ConfigureAwait(false);
            }

            // Back to the top, so the next tab is read from the same place this one started at.
            for (var i = 0; i < ScrollSteps; i++)
            {
                await page.Mouse.WheelAsync(0, -ScrollStepPx).ConfigureAwait(false);
            }

            await page.WaitForTimeoutAsync(ScrollSettleMs).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _logger.LogDebug(ex, "Scrolling the page to reveal lazily rendered content failed.");
        }
    }

    /// <summary>
    /// Visits every tab of every toggle group currently on screen.
    ///
    /// A toggle group hides the tabs it is not showing exactly as a tab panel does, so a page whose
    /// controls sit behind one is read only in part. Unlike a tab panel it marks nothing as open
    /// until a tab has been clicked, so there is no starting point to restore: the walk ends on the
    /// first tab, which is what the page shows on arrival.
    ///
    /// Called once for each tab of the page, because a group lives on a tab and is only rendered
    /// while that tab is open.
    /// </summary>
    private async Task WalkToggleTabsAsync(
        IPage page,
        string script,
        bool includeHidden,
        List<DiscoveredElement> elements,
        List<UnknownControl> unknown,
        HashSet<string> seen)
    {
        string[] groups;
        try
        {
            groups = await page.EvaluateAsync<string[]>(
                @"() => [...document.querySelectorAll('" + ToggleTabHostTag + @"[element-name]')]
                    .filter(g => g.offsetParent !== null || g.getClientRects().length > 0)
                    .map(g => g.getAttribute('element-name'))").ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Looking for toggle tab groups failed.");
            return;
        }

        foreach (var group in groups)
        {
            var tabs = await ToggleTabsOfAsync(page, group).ConfigureAwait(false);
            if (tabs.Length < 2)
            {
                continue;
            }

            foreach (var tab in tabs)
            {
                if (!await OpenToggleTabAsync(page, group, tab).ConfigureAwait(false))
                {
                    _logger.LogWarning(
                        "Toggle tab '{Tab}' of {Group} would not open; anything only on it was not seen.",
                        tab, group);
                    continue;
                }

                var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
                var before = elements.Count;
                Collect(raw, includeHidden, elements, unknown, seen);

                _logger.LogInformation("Toggle tab {Group}/{Tab}: {Count} new control(s).",
                    group, tab, elements.Count - before);
            }

            // Back to the tab the group shows by default, so the rest of the analysis sees the page
            // the way it was found.
            await OpenToggleTabAsync(page, group, tabs[0]).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the captions a toggle group offers, in display order.</summary>
    private static Task<string[]> ToggleTabsOfAsync(IPage page, string group) =>
        page.EvaluateAsync<string[]>(
            @"(name) => {
                const g = document.querySelector('" + ToggleTabHostTag + @"[element-name=""' + name + '""]');
                if (!g) return [];
                return [...g.querySelectorAll('" + ToggleTab.ItemSelector + @"')]
                    .map(i => (i.getAttribute('data-item-marker') || '').trim())
                    .filter(m => m.length > 0);
            }", group);

    /// <summary>Clicks one tab of a toggle group, reporting whether the click could be made.</summary>
    private async Task<bool> OpenToggleTabAsync(IPage page, string group, string tab)
    {
        try
        {
            // The marker sits on the crt-button host, but only the button it wraps takes the click.
            var item = page.Locator(
                ToggleTabHostTag + "[element-name=\"" + group + "\"] " +
                ToggleTab.ItemSelector + "[data-item-marker=\"" + tab + "\"] button");

            if (await item.CountAsync().ConfigureAwait(false) == 0)
            {
                return false;
            }

            await item.First.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
            await item.First.ClickAsync(new LocatorClickOptions { Timeout = ToggleClickTimeoutMs })
                .ConfigureAwait(false);
            await page.WaitForTimeoutAsync(ToggleSettleMs).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _logger.LogDebug(ex, "Opening toggle tab '{Tab}' of {Group} failed.", tab, group);
            return false;
        }
    }

    /// <summary>
    /// Which tab is open.
    ///
    /// The tab headers carry no marker at all -- no aria-selected, no class -- so the signal has to
    /// come from the content containers. Freedom UI keeps the container of every tab that has been
    /// visited and merely hides the inactive ones with display:none, so the open tab is the one
    /// whose container is still laid out. Taking the first container instead would report whichever
    /// tab was visited earliest.
    /// </summary>
    private static async Task<string?> ActiveTabAsync(IPage page)
    {
        return await page.EvaluateAsync<string?>(
            @"() => {
                const open = [...document.querySelectorAll('crt-toggle-container-item[element-name]')]
                    .find(e => e.offsetParent !== null || e.getClientRects().length > 0);
                return open ? open.getAttribute('element-name') : null;
            }").ConfigureAwait(false);
    }

    /// <summary>Clicks a tab and confirms it really became the open one.</summary>
    private async Task<bool> OpenTabAsync(IPage page, string code)
    {
        try
        {
            var header = page.Locator(TabHostTag + "[element-name=\"" + code + "\"] .tab-header");
            if (await header.CountAsync().ConfigureAwait(false) == 0)
            {
                return false;
            }

            await header.First.ClickAsync(new LocatorClickOptions { Timeout = TabClickTimeoutMs })
                .ConfigureAwait(false);

            // The click is asynchronous; poll for the content container rather than guessing a delay.
            var deadline = DateTime.UtcNow.AddMilliseconds(TabSwitchTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (string.Equals(await ActiveTabAsync(page).ConfigureAwait(false), code, StringComparison.Ordinal))
                {
                    await page.WaitForTimeoutAsync(TabSettleMs).ConfigureAwait(false);
                    return true;
                }

                await page.WaitForTimeoutAsync(TabPollMs).ConfigureAwait(false);
            }

            return false;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _logger.LogDebug(ex, "Opening tab {Tab} failed.", code);
            return false;
        }
    }

    /// <summary>
    /// Opens every collapsed expansion panel and analyses what appears.
    ///
    /// A collapsed panel renders none of its contents, so a grid inside one is simply not in the DOM
    /// — the same behaviour as a closed tab. A panel that already holds content is left alone rather
    /// than clicked, because a click on an open panel would close it and lose what was found.
    /// </summary>
    private async Task OpenPanelsAsync(
        IPage page,
        string script,
        bool includeHidden,
        List<DiscoveredElement> elements,
        List<UnknownControl> unknown,
        HashSet<string> seen)
    {
        // Creatio fills a panel only once it comes into view, so an expanded panel further down the
        // page reports itself as open while holding nothing at all. Scrolling each one into view
        // triggers that load and, unlike clicking, cannot close a panel that is already open.
        await ScrollPanelsIntoViewAsync(page).ConfigureAwait(false);

        // The toggle icon carries the state explicitly: toggle-icon-collapsed while closed,
        // toggle-icon-expanded while open. That is also what drives the colour a person sees, and
        // reading the class survives a theme change in a way that sampling the colour would not.
        // The toggle button is additionally labelled Expand or Collapse, which is used as a fallback
        // because it follows the interface language.
        var collapsed = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('crt-expansion-panel[element-name]')]
                .filter(p => {
                    if (p.querySelector('.toggle-icon-collapsed')) return true;
                    if (p.querySelector('.toggle-icon-expanded')) return false;
                    const toggle = p.querySelector('.expansion-panel-header [aria-label]');
                    return toggle ? /expand/i.test(toggle.getAttribute('aria-label')) : false;
                })
                .map(p => p.getAttribute('element-name'))").ConfigureAwait(false);

        if (collapsed.Length > 0)
        {
            _logger.LogInformation("Opening {Count} collapsed panel(s): {Panels}.",
                collapsed.Length, string.Join(", ", collapsed));
        }

        foreach (var panel in collapsed)
        {
            try
            {
                var header = page.Locator(
                    "crt-expansion-panel[element-name=\"" + panel + "\"] .expansion-panel-header");

                if (await header.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await header.First.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                await header.First.ClickAsync(new LocatorClickOptions { Timeout = PanelClickTimeoutMs })
                    .ConfigureAwait(false);
                await page.WaitForTimeoutAsync(PanelSettleMs).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
            {
                _logger.LogWarning("Panel {Panel} would not open; anything only inside it was not seen.", panel);
                continue;
            }

            var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
            var before = elements.Count;
            Collect(raw, includeHidden, elements, unknown, seen);

            _logger.LogInformation("Panel {Panel}: {Count} new control(s).", panel, elements.Count - before);
        }

        await WaitForPanelContentAsync(page).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings every expansion panel into view, which is what makes Creatio render its contents.
    /// </summary>
    private async Task ScrollPanelsIntoViewAsync(IPage page)
    {
        var panels = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('crt-expansion-panel[element-name]')]
                .map(p => p.getAttribute('element-name'))").ConfigureAwait(false);

        foreach (var panel in panels)
        {
            try
            {
                var header = page.Locator(
                    "crt-expansion-panel[element-name=\"" + panel + "\"] .expansion-panel-header");

                if (await header.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await header.First.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                await page.WaitForTimeoutAsync(PanelSettleMs).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
            {
                _logger.LogDebug(ex, "Could not bring panel {Panel} into view.", panel);
            }
        }
    }

    /// <summary>
    /// Waits for panels that are open but still empty.
    ///
    /// Creatio fills an expansion panel lazily: the panel reports itself as expanded well before the
    /// grid inside it reaches the DOM. Without this wait an open panel looks like an empty one and
    /// its contents are missed entirely.
    /// </summary>
    private async Task WaitForPanelContentAsync(IPage page)
    {
        const string pendingScript =
            @"() => [...document.querySelectorAll('crt-expansion-panel[element-name]')]
                .filter(p => {
                    if (p.querySelector('.toggle-icon-collapsed')) return false;
                    const body = p.querySelector('.mat-expansion-panel-content');
                    // The header's toolbar buttons carry element-name too, so only the body counts.
                    return body ? body.querySelectorAll('crt-data-grid[element-name]').length === 0
                                  && body.querySelectorAll('[element-name]').length <= 1
                                : false;
                })
                .map(p => p.getAttribute('element-name'))";

        var deadline = DateTime.UtcNow.AddMilliseconds(PanelContentTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var pending = await page.EvaluateAsync<string[]>(pendingScript).ConfigureAwait(false);
            if (pending.Length == 0)
            {
                return;
            }

            await page.WaitForTimeoutAsync(PanelPollMs).ConfigureAwait(false);
        }

        var stillEmpty = await page.EvaluateAsync<string[]>(pendingScript).ConfigureAwait(false);
        if (stillEmpty.Length > 0)
        {
            _logger.LogWarning("Panel(s) {Panels} are open but stayed empty; they may simply have no content.",
                string.Join(", ", stillEmpty));
        }
    }

    private DiscoveredElement? Map(JsonElement item)
    {
        var code = Text(item, "code");
        var tag = Text(item, "tag");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var value = Text(item, "value");
        var readOnly = Flag(item, "readonly");
        var (typeName, uncertainty, undecidable) =
            ResolveType(tag, value, Text(item, "pickerHint"), readOnly);

        // What the DOM offers is filtered by what the element type can actually use, so a page file
        // never records a member the catalog would then refuse to bind.
        var registration = _index.For(tag)
            .FirstOrDefault(r => string.Equals(r.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        var stages = Accepts(registration, "values") ? Strings(item, "stages") : Array.Empty<string>();
        var columns = Accepts(registration, "columns") ? Strings(item, "columns") : Array.Empty<string>();
        var tabs = Accepts(registration, "tabs") ? Strings(item, "tabs") : Array.Empty<string>();

        // A grid carries no caption of its own; the panel it sits in is what names it on screen, and
        // that name is also how a test asks for the panel to be opened. Only element types that say
        // so borrow it: a grid's toolbar buttons sit in the same panel and are not named by it.
        var label = Text(item, "label");
        if (label.Length == 0 && registration is { LabeledByPanel: true })
        {
            label = Text(item, "panelCaption");
        }

        return new DiscoveredElement(
            Code: code,
            Label: label,
            Type: typeName,
            HostTag: tag,
            Required: Flag(item, "required"),
            ReadOnly: readOnly,
            Visible: Flag(item, "visible"),
            Stages: stages.Length > 0 ? stages : null,
            Columns: columns.Length > 0 ? columns : null,
            Tabs: tabs.Length > 0 ? tabs : null,
            Uncertainty: uncertainty,
            TypeUndecidable: undecidable);
    }

    /// <summary>
    /// Picks the element type for a tag. Most tags map to exactly one; the two that do not are
    /// resolved from the picker icon first and the rendered value second, and the caller is told
    /// when neither was enough.
    /// </summary>
    internal (string TypeName, string? Uncertainty, bool Undecidable) ResolveType(
        string tag,
        string value,
        string pickerHint,
        bool isReadOnly = false)
    {
        var candidates = _index.For(tag);
        if (candidates.Count == 1)
        {
            return (candidates[0].TypeName, null, false);
        }

        var text = value.Trim();
        var kind = candidates[0].ValueKind;

        if (kind == ValueKind.DateTime)
        {
            // The icon is definitive when it is there. It is absent on a read-only control, which
            // renders a padlock instead, and its wording follows the interface language, so an
            // unrecognised label falls through to the value rather than being guessed at.
            var byIcon = pickerHint.Trim() switch
            {
                "Calendar" => "Date",
                "Time" => "Time",
                "Date/Time" => "DateTime",
                _ => null,
            };

            if (byIcon is not null)
            {
                return (PreferOr(candidates, byIcon), null, false);
            }

            if (TimeOnly().IsMatch(text))
            {
                return (PreferOr(candidates, "Time"), null, false);
            }

            if (DateOnly().IsMatch(text))
            {
                return (PreferOr(candidates, "Date"), null, false);
            }

            if (text.Length > 0)
            {
                return (PreferOr(candidates, "DateTime"), null, false);
            }

            // Read-only and empty: no picker icon is rendered and there is no value, so the exact
            // type is beyond anyone, not just beyond the analyser. Record the control so its
            // presence and caption can still be asserted, and say plainly that the type is a
            // placeholder rather than asking for a decision that cannot be made.
            if (isReadOnly)
            {
                return (PreferOr(candidates, "DateTime"),
                    "read-only and empty, so the exact type cannot be established; " +
                    "presence and caption are still assertable",
                    true);
            }

            return (PreferOr(candidates, "DateTime"),
                "the control is empty and shows no picker icon; open the picker to see which it is, " +
                "then set the type and mark the entry \"pinned\": true",
                false);
        }

        if (kind == ValueKind.Number)
        {
            if (text.Contains('.') || text.Contains(','))
            {
                return (PreferOr(candidates, "Decimal"), null, false);
            }

            if (text.Length == 0)
            {
                return (PreferOr(candidates, "Integer"),
                    isReadOnly
                        ? "read-only and empty, so Integer against Decimal cannot be established; " +
                          "presence and caption are still assertable"
                        : "the control is empty, so Integer was assumed; switch to Decimal if it takes fractions",
                    isReadOnly);
            }

            return (PreferOr(candidates, "Integer"), null, false);
        }

        return (candidates[0].TypeName,
            "tag '" + tag + "' maps to " + candidates.Count + " element types; the first was taken",
            false);
    }

    private static string PreferOr(IReadOnlyList<UiElementRegistration> candidates, string preferred) =>
        candidates.FirstOrDefault(c =>
            string.Equals(c.TypeName, preferred, StringComparison.OrdinalIgnoreCase))?.TypeName
        ?? candidates[0].TypeName;

    /// <summary>
    /// Whether an element type can take a discovered option list at all.
    ///
    /// Asked of the options class the type declares, so the rule is the same one the catalog applies
    /// when it binds a page file. Without it a control would be recorded with a member its own type
    /// rejects, and the next run would refuse to load the configuration analysis had just written.
    /// </summary>
    private static bool Accepts(UiElementRegistration? registration, string member) =>
        registration?.OptionsType?.GetProperty(
            member,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase) is not null;

    /// <summary>Reads a string array member, dropping the blanks the DOM occasionally yields.</summary>
    private static string[] Strings(JsonElement item, string name) =>
        item.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Where(e => e.Length > 0)
                .ToArray()
            : Array.Empty<string>();

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^\d{1,2}:\d{2}(\s*[AaPp][Mm])?$")]
    private static partial Regex TimeOnly();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{4}$")]
    private static partial Regex DateOnly();

    /// <summary>
    /// Builds the DOM query. Everything is read in one pass in the browser: walking the tree from
    /// C# would cost a round trip per attribute and take minutes on a page this size.
    /// </summary>
    private static string BuildScript(IReadOnlyCollection<string> tags)
    {
        var tagList = JsonSerializer.Serialize(tags);

        return @"() => {
  const known = " + tagList + @";
  const layout = new Set(['crt-flex','crt-grid','crt-router-outlet','crt-app-background',
    'crt-skip-links','crt-app-toolbar','crt-navigation-panel','crt-7x-module',
    'crt-expansion-panel','crt-toggle-container-item','crt-tab-panel-container']);
  const norm = t => (t || '').trim().replace(/[\s*:]+$/, '').trim();

  const result = { known: [], unknown: [] };

  for (const tag of known) {
    for (const host of document.querySelectorAll(tag + '[element-name]')) {
      const input = host.querySelector('input, textarea');
      // A toggle tab group is full of buttons, and the first of them would otherwise be read as the
      // group's own caption. A group has none: its tabs are its content, not its name.
      const labelEl = [...host.querySelectorAll(
        '.crt-input-label, .crt-checkbox-label, .crt-button-caption, .mdc-button__label')]
        .find(e => {
          const item = e.closest('crt-button-toggle-group-item');
          return !item || !host.contains(item);
        }) || null;
      const tabHeader = host.querySelector('.tab-header[title]');

      result.known.push({
        tag,
        code: host.getAttribute('element-name'),
        label: norm(tabHeader ? tabHeader.getAttribute('title')
                              : (labelEl ? labelEl.textContent : '')),
        value: input ? (input.value || '') : '',
        required: !!(input && (input.hasAttribute('required')
                   || input.getAttribute('aria-required') === 'true'))
                  || !!(labelEl && labelEl.className.includes('crt-input-required')),
        readonly: !!(input && (input.hasAttribute('readonly')
                   || input.getAttribute('aria-readonly') === 'true'
                   || input.disabled))
                  || !!host.querySelector('.readonly-icon'),
        visible: !!host.offsetParent || host.getClientRects().length > 0,
        // The picker icon announces which picker the control opens -- Calendar, Time or Date/Time --
        // and it is the only thing that tells an empty date control from an empty time one.
        pickerHint: (host.querySelector('mat-icon[aria-label]') || {}).ariaLabel || '',
        panelCaption: (() => {
          const panel = host.closest('crt-expansion-panel[element-name]');
          if (!panel) return '';
          const head = panel.querySelector('.expansion-panel-header');
          return head ? norm(head.textContent) : '';
        })(),
        stages: [...host.querySelectorAll('.button-toggle[data-item-marker]')]
                  .map(t => t.getAttribute('data-item-marker')),
        // Column captions of a grid, in display order. The selector is the grid element's own, so
        // what analysis records and what 'check X columns' reads can never drift apart.
        columns: [...host.querySelectorAll('" + DataGrid.ColumnHeaderSelector + @"')]
                   .map(h => norm(h.innerText || h.textContent)),
        // Captions of a toggle tab group. A tab inside such a group has no element-name of its own,
        // so the marker is the only thing that names it.
        tabs: [...host.querySelectorAll('" + ToggleTab.ItemSelector + @"')]
                .map(i => (i.getAttribute('data-item-marker') || '').trim())
      });
    }
  }

  for (const el of document.querySelectorAll('[element-name]')) {
    const tag = el.tagName.toLowerCase();
    if (!tag.startsWith('crt-') || known.includes(tag) || layout.has(tag)) continue;
    result.unknown.push({ tag, code: el.getAttribute('element-name') });
  }

  return result;
}";
    }
}
