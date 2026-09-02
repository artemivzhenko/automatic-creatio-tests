using System.Text.Json;
using System.Text.RegularExpressions;
using CreatioAutoTests.Frontend;
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
    private const int TabSettleMs = 600;
    private const int TabPollMs = 200;
    private const int TabClickTimeoutMs = 10_000;
    private const int TabSwitchTimeoutMs = 10_000;

    private readonly HostTagIndex _index;
    private readonly ILogger<PageAnalyzer> _logger;

    public PageAnalyzer(UiElementRegistry registry, ILogger<PageAnalyzer> logger)
    {
        _index = new HostTagIndex(registry ?? throw new ArgumentNullException(nameof(registry)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
        Collect(raw, includeHidden, elements, unknown, seen);

        if (walkTabs)
        {
            await WalkTabsAsync(page, script, includeHidden, elements, unknown, seen).ConfigureAwait(false);
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
            if (discovered is null || !seen.Add(discovered.Code))
            {
                continue;
            }

            if (includeHidden || discovered.Visible)
            {
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

            var raw = await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
            var before = elements.Count;
            Collect(raw, includeHidden, elements, unknown, seen);

            _logger.LogInformation("Tab {Tab}: {Count} new control(s).", tab, elements.Count - before);
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

        var stages = item.TryGetProperty("stages", out var stageArray) && stageArray.ValueKind == JsonValueKind.Array
            ? stageArray.EnumerateArray().Select(s => s.GetString() ?? string.Empty)
                .Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

        return new DiscoveredElement(
            Code: code,
            Label: Text(item, "label"),
            Type: typeName,
            HostTag: tag,
            Required: Flag(item, "required"),
            ReadOnly: readOnly,
            Visible: Flag(item, "visible"),
            Stages: stages.Length > 0 ? stages : null,
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
      const labelEl = host.querySelector(
        '.crt-input-label, .crt-checkbox-label, .crt-button-caption, .mdc-button__label');
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
        stages: [...host.querySelectorAll('.button-toggle[data-item-marker]')]
                  .map(t => t.getAttribute('data-item-marker'))
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
