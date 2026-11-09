using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Controls;

/// <summary>
/// Classic tab panel as one element whose value is the open tab, by caption. The counterpart of a
/// Freedom UI toggle group: <c>set Tabs "Services"</c> switches, <c>check-value Tabs eq "Services"</c>
/// asserts, where <see cref="Tab"/> addresses one tab by its schema name for <c>click</c>.
/// The open tab is the header carrying <c>ts-tabpanel-active-item</c>.
/// </summary>
[UiElement("TabPanel", Generation = UiGeneration.ClassicUI, Controls = new[] { "TabPanel" },
    ValueKind = ValueKind.Tab, Options = typeof(ClassicTabPanelOptions))]
public sealed class TabPanel : ClassicElementBase, IValueElement
{
    private const string ActiveItemSelector = "li.ts-tabpanel-active-item";
    private readonly ClassicTabPanelOptions _options;

    /// <summary>Initializes a Classic tab panel from its options.</summary>
    public TabPanel(ElementRuntimeContext context) : this(context, context.Definition.BindOptions<ClassicTabPanelOptions>())
    {
    }

    private TabPanel(ElementRuntimeContext context, ClassicTabPanelOptions options) : base(context, options)
    {
        _options = options;
    }

    /// <summary>Captions declared in configuration, in display order.</summary>
    public IReadOnlyList<string> ExpectedTabs => _options.Tabs;

    /// <summary>The header list of the panel named by the code (<c>Tabs</c> on nearly every page).</summary>
    protected override string BuildSelector() => ClassicIds.Selector(ClassicIds.TabItems(Schema, Name));

    /// <inheritdoc />
    protected override string LocatorDescription => ClassicIds.TabPanel(Schema, Name);

    /// <summary>A tab panel has no caption of its own.</summary>
    protected override ILocator LabelLocator => Root;

    /// <inheritdoc />
    public override Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <summary>Caption of the open tab, from text content: the rendered text is upper-cased by CSS.</summary>
    public async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var text = await TextContentAsync(Root.Locator(ActiveItemSelector)).ConfigureAwait(false);
        return text is null ? string.Empty : NormalizeLabel(text);
    }

    /// <summary>Every tab caption the panel shows, in display order.</summary>
    public async Task<IReadOnlyList<string>> GetAvailableTabsAsync()
    {
        try
        {
            var captions = await Root.EvaluateAsync<string[]>(
                "el => [...el.querySelectorAll('li[data-item-index]')].map(li => (li.textContent || '').trim()).filter(t => t.length > 0)")
                .ConfigureAwait(false);
            return captions ?? Array.Empty<string>();
        }
        catch (PlaywrightException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Opens the tab with the given caption and confirms it became the open one.</summary>
    public async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A tab panel cannot be cleared; name the tab to open.");
        }

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var target = NormalizeLabel(value);
        var current = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var index = await Root.EvaluateAsync<int>(
            @"(el, target) => {
                const items = [...el.querySelectorAll('li[data-item-index]')];
                const hit = items.find(li => (li.textContent || '').trim().toLowerCase() === target.toLowerCase());
                return hit ? Number(hit.getAttribute('data-item-index')) : -1;
            }", target).ConfigureAwait(false);

        if (index < 0)
        {
            var available = await GetAvailableTabsAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Tab '" + target + "' is not on panel '" + Code + "'. Available: " +
                (available.Count == 0 ? "(none)" : string.Join(", ", available)) + ".");
        }

        await Root.Locator("li[data-item-index=\"" + index + "\"]")
            .ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        if (_options.SettleMs > 0)
        {
            await Page.WaitForTimeoutAsync(_options.SettleMs).ConfigureAwait(false);
        }

        var opened = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(opened, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Panel '" + Code + "' shows '" + opened + "' after opening '" + target + "'.");
        }
    }
}
