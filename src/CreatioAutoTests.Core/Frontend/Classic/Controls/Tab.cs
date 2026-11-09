using System.Text.Json;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Controls;

/// <summary>
/// Classic tab, addressed by its name in the schema (<c>GeneralInfoTab</c>, <c>Tabdebe3cb6TabLabel</c>).
///
/// The tab headers carry no ids and no names: they are <c>li[data-item-index]</c> inside
/// <c>{Schema}TabsTabPanel-tabpanel-items</c>, and the index is the tab's position in the page's
/// <c>TabsCollection</c>, hidden tabs included. So the name is turned into an index through the view
/// model, and the index into the header. The open tab is the one with <c>ts-tabpanel-active-item</c>.
/// Content of a visited tab stays in the DOM, hidden, when another tab is opened.
/// </summary>
[UiElement("Tab", Generation = UiGeneration.ClassicUI, Controls = new[] { "Tab" }, Options = typeof(ClassicTabOptions))]
public sealed class Tab : ClassicElementBase, IClickable
{
    private readonly ClassicTabOptions _options;
    private int? _index;

    /// <summary>Initializes a Classic tab from its options.</summary>
    public Tab(ElementRuntimeContext context) : this(context, context.Definition.BindOptions<ClassicTabOptions>())
    {
    }

    private Tab(ElementRuntimeContext context, ClassicTabOptions options) : base(context, options)
    {
        _options = options;
    }

    private string PanelComponentId => ClassicIds.TabPanel(Schema, _options.Panel);

    /// <summary>The header list; the tab's own header is found from it by index.</summary>
    protected override string BuildSelector() => ClassicIds.Selector(ClassicIds.TabItems(Schema, _options.Panel));

    /// <inheritdoc />
    protected override string LocatorDescription => PanelComponentId + " tab '" + Name + "'";

    /// <summary>A tab header is its own caption.</summary>
    protected override ILocator LabelLocator => Root;

    /// <summary>The header of this tab, or null when the panel does not list the name.</summary>
    private async Task<ILocator?> HeaderAsync()
    {
        if (_index is null)
        {
            const string script = @"(c, name) => {
                const collection = c.model && typeof c.model.get === 'function' ? c.model.get('TabsCollection') : null;
                const keys = collection && typeof collection.getKeys === 'function' ? collection.getKeys() : [];
                return keys.indexOf(name);
            }";

            var result = await EvaluateOnComponentAsync(PanelComponentId, script, Name).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Number || result.GetInt32() < 0)
            {
                return null;
            }

            _index = result.GetInt32();
        }

        return Root.Locator("li[data-item-index=\"" + _index + "\"]");
    }

    /// <summary>Present when the panel knows the tab and renders its header.</summary>
    public override async Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        var header = await HeaderAsync().ConfigureAwait(false);
        if (header is null)
        {
            return false;
        }

        try
        {
            await header.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>Caption from the header's text content: the rendered text is upper-cased by CSS.</summary>
    public override async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        var header = await HeaderAsync().ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        var text = await TextContentAsync(header).ConfigureAwait(false);
        return text is null ? null : NormalizeLabel(text);
    }

    /// <summary>Opens the tab and waits for its content to render.</summary>
    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var header = (await HeaderAsync().ConfigureAwait(false))!;
        await header.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        if (_options.SettleMs > 0)
        {
            await Page.WaitForTimeoutAsync(_options.SettleMs).ConfigureAwait(false);
        }
    }
}
