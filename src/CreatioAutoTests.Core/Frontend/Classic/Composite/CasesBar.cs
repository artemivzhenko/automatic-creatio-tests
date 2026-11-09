using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Composite;

/// <summary>
/// Classic stage bar: the DCM stages of the actions dashboard. Exposed as a value element so that
/// <c>set X "stage"</c> and <c>check-value X eq "stage"</c> work on it without a special case.
///
/// The bar is <c>Terrasoft.controls.BaseStageControl</c>, one shared module on every page, so its
/// id does not depend on the page schema. Each stage is a <c>.stage-item[data-item-marker]</c>; the
/// current one carries <c>stage-current</c>; a stage with sub-stages carries <c>stage-with-menu</c>
/// and opens a menu of them when clicked. The committed stage is read from the dashboard's view
/// model rather than from the markers, because a sub-stage shows its parent on the bar.
///
/// A stage change in Classic is not an edit: the dashboard validates the page and saves the record
/// at once. <c>set</c> on this element therefore writes to the database.
/// </summary>
[UiElement("CasesBar", Generation = UiGeneration.ClassicUI, Controls = new[] { "StageControl" },
    ValueKind = ValueKind.Stage, Options = typeof(ClassicCasesBarOptions))]
public sealed class CasesBar : ClassicElementBase, IValueElement
{
    private const string StageSelector = ".stage-item[data-item-marker]";
    private const string CurrentStageSelector = ".stage-item.stage-current[data-item-marker]";
    /// <summary>
    /// Classic's message box: the confirmation the page may be configured to ask
    /// (<c>ShowChangeStageConfirmDialog</c>), or the validation message that refuses the move.
    /// Its caption is <c>.ts-messagebox-caption</c>, its buttons <c>.t-btn-wrapper[data-item-marker]</c>
    /// with the caption as marker.
    /// </summary>
    private const string MessageBoxSelector = ".ts-messagebox-box";

    private const string MessageBoxButtonSelector = ".t-btn-wrapper[data-item-marker]";

    /// <summary>
    /// Any dialog that may stand in the way of a stage change: Classic's message box, or a Freedom UI
    /// modal a page's own logic opens from the Shell (a prerequisite the record does not meet).
    /// </summary>
    private const string AnyDialogSelector =
        ".ts-messagebox-box, mat-dialog-container, .cdk-overlay-pane.modal-container";

    private const int MenuWaitMs = 4000;
    private const int DialogWaitMs = 1500;

    private readonly ClassicCasesBarOptions _options;

    /// <summary>Initializes the Classic stage bar from its options.</summary>
    public CasesBar(ElementRuntimeContext context) : this(context, context.Definition.BindOptions<ClassicCasesBarOptions>())
    {
    }

    private CasesBar(ElementRuntimeContext context, ClassicCasesBarOptions options) : base(context, options)
    {
        _options = options;
    }

    /// <summary>Stages declared in configuration, in the order they are expected on screen.</summary>
    public IReadOnlyList<string> ExpectedStages => _options.Values;

    private string ContainerId => _options.Container.Trim();

    /// <summary>The stage control's wrapper, shared by every page that shows the actions dashboard.</summary>
    protected override string BuildSelector() => ClassicIds.Selector(ContainerId + "-wrap");

    /// <inheritdoc />
    protected override string LocatorDescription => ContainerId;

    /// <summary>A stage bar has no caption.</summary>
    protected override ILocator LabelLocator => Root;

    /// <inheritdoc />
    public override Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// The actions dashboard is its own module and finishes loading after the page's own controls,
    /// which is what the page runtime waits for. So the bar is present only once it shows stages.
    /// </summary>
    public override async Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (!await base.ExistsAsync(timeoutMs, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await Root.Locator(StageSelector).First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            return true;
        }
        catch (TimeoutException)
        {
            Logger.LogDebug("Stage bar {Code} rendered no stages within {Timeout} ms.", Code, timeoutMs);
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// The committed stage: the display value of the page's stage column as the dashboard holds it,
    /// which is the sub-stage when one is selected; the bar's current marker is the fallback.
    /// </summary>
    public async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        const string script = @"c => {
            const m = c.model;
            if (!m || typeof m.get !== 'function') { return null; }
            const column = m.get('ActionsColumnName');
            const value = (column && typeof m.getMasterEntityParameterValue === 'function') ? m.getMasterEntityParameterValue(column) : null;
            return value && value.displayValue != null ? String(value.displayValue) : null;
        }";

        var fromModel = await EvaluateOnComponentAsync(ContainerId, script, null).ConfigureAwait(false);
        if (fromModel.ValueKind == JsonValueKind.String)
        {
            return fromModel.GetString()!.Trim();
        }

        var current = Root.Locator(CurrentStageSelector);
        if (await current.CountAsync().ConfigureAwait(false) == 0)
        {
            return string.Empty;
        }

        var marker = await current.First.GetAttributeAsync("data-item-marker").ConfigureAwait(false);
        return marker?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// JavaScript that turns whatever holds the bar's items into an array. The control keeps them in
    /// a Terrasoft collection, Ext keeps a menu's in a MixedCollection, and either may be a plain
    /// array on another build.
    /// </summary>
    private const string ItemsHelper = @"
        const toArray = col => {
            if (!col) { return []; }
            if (Array.isArray(col)) { return col; }
            if (typeof col.getItems === 'function') { return col.getItems() || []; }
            if (Array.isArray(col.items)) { return col.items; }
            if (col.map && typeof col.map === 'object') { return Object.values(col.map); }
            return [];
        };";

    /// <summary>Every stage the bar offers, top-level ones and the sub-stages behind their menus.</summary>
    public async Task<IReadOnlyList<string>> GetAvailableStagesAsync()
    {
        const string script = @"c => {" + ItemsHelper + @"
            const result = [];
            for (const i of toArray(c.items)) {
                result.push(i.caption);
                for (const s of toArray(i.menu && i.menu.items)) { if (s.caption && s.caption !== i.caption) { result.push(s.caption); } }
            }
            return result;
        }";

        var result = await EvaluateOnComponentAsync(ContainerId, script, null).ConfigureAwait(false);
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray();
        }

        var markers = new List<string>();
        var stages = Root.Locator(StageSelector);
        var count = await stages.CountAsync().ConfigureAwait(false);
        for (var i = 0; i < count; i++)
        {
            var marker = await stages.Nth(i).GetAttributeAsync("data-item-marker").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(marker))
            {
                markers.Add(marker.Trim());
            }
        }

        return markers;
    }

    /// <summary>
    /// Switches to a stage or a sub-stage, answers the confirmation Classic may ask, waits for the
    /// save the dashboard performs and confirms the stage column took the value.
    /// </summary>
    public async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A stage bar cannot be cleared; name the stage to switch to.");
        }

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var stage = value.Trim();
        var current = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current, stage, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("Stage bar {Code} is already on '{Stage}'.", Code, stage);
            return;
        }

        var parent = await FindParentStageAsync(stage).ConfigureAwait(false);
        if (parent is null)
        {
            await ClickStageAsync(stage).ConfigureAwait(false);
        }
        else
        {
            await ClickSubStageAsync(parent, stage).ConfigureAwait(false);
        }

        var message = await AnswerConfirmationAsync().ConfigureAwait(false);
        await WaitForSaveAsync().ConfigureAwait(false);

        var committed = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(committed, stage, StringComparison.OrdinalIgnoreCase))
        {
            if (message is not null)
            {
                Logger.LogInformation("Switching {Code} to '{Stage}' showed: {Message}", Code, stage, message);
            }

            return;
        }

        // The page may have refused the move with a dialog of its own: a prerequisite the record
        // does not meet, a form to fill first. Its text is the useful failure, and it has to be closed
        // or every later click on the page is swallowed by the overlay.
        var dialog = message ?? await ReadAndCloseDialogAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            "Stage bar '" + Code + "' holds '" + committed + "' after switching to '" + stage + "'" +
            (dialog is null ? "." : ": the page answered \"" + dialog + "\"."));
    }

    /// <summary>Text of a dialog left open by the page, with the dialog then dismissed; null when there is none.</summary>
    private async Task<string?> ReadAndCloseDialogAsync()
    {
        var dialog = Page.Locator(AnyDialogSelector);
        try
        {
            if (await dialog.CountAsync().ConfigureAwait(false) == 0 || !await dialog.First.IsVisibleAsync().ConfigureAwait(false))
            {
                return null;
            }

            var text = (await dialog.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
            var firstLines = string.Join(" / ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3));

            await Page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(500).ConfigureAwait(false);

            if (await dialog.CountAsync().ConfigureAwait(false) > 0 && await dialog.First.IsVisibleAsync().ConfigureAwait(false))
            {
                Logger.LogWarning("The dialog shown after switching stage on {Code} did not close on Escape.", Code);
            }

            return firstLines.Length > 300 ? firstLines.Substring(0, 300) + "..." : firstLines;
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the dialog after switching stage on {Code} failed.", Code);
            return null;
        }
    }

    /// <summary>
    /// The top-level stage whose menu hides <paramref name="stage"/>: from the page file when it says,
    /// else from the control, which knows which items it folded into menus.
    /// </summary>
    private async Task<string?> FindParentStageAsync(string stage)
    {
        if (_options.Subvalues is not null)
        {
            foreach (var pair in _options.Subvalues)
            {
                if (pair.Value.Contains(stage, StringComparer.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, stage, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }
        }

        const string script = @"(c, target) => {" + ItemsHelper + @"
            const items = toArray(c.items);
            for (const i of items) {
                if ((i.caption || '').toLowerCase() === target.toLowerCase()) { return null; }
            }
            for (const i of items) {
                for (const s of toArray(i.menu && i.menu.items)) {
                    if ((s.caption || '').toLowerCase() === target.toLowerCase()) { return i.caption; }
                }
            }
            return null;
        }";

        var result = await EvaluateOnComponentAsync(ContainerId, script, stage).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    }

    private async Task ClickStageAsync(string stage)
    {
        var item = Root.Locator(".stage-item[data-item-marker=\"" + stage + "\"]");
        if (await item.CountAsync().ConfigureAwait(false) == 0)
        {
            var available = await GetAvailableStagesAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Stage '" + stage + "' is not on bar '" + Code + "'. Available: " +
                (available.Count == 0 ? "(none)" : string.Join(", ", available)) + ".");
        }

        var classes = await SafeAttributeAsync(item.First, "class").ConfigureAwait(false) ?? string.Empty;
        if (!classes.Contains("stage-enabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Stage '" + stage + "' on bar '" + Code + "' is not enabled: the case does not allow moving there from the current stage.");
        }

        await item.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
    }

    /// <summary>Opens the parent's menu and picks the sub-stage; the menu is rendered into body on first open.</summary>
    private async Task ClickSubStageAsync(string parent, string stage)
    {
        var parentItem = Root.Locator(".stage-item[data-item-marker=\"" + parent + "\"]");
        if (await parentItem.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "Parent stage '" + parent + "' of sub-stage '" + stage + "' is not on bar '" + Code + "'.");
        }

        await parentItem.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        // The menu item carries the sub-stage caption as its marker; the stage item itself is excluded
        // so that a sub-stage sharing its parent's caption resolves to the menu entry.
        var menuItem = Page.Locator("[data-item-marker=\"" + stage + "\"]:not(.stage-item)");
        try
        {
            await menuItem.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = MenuWaitMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "Sub-stage '" + stage + "' did not appear in the menu of '" + parent + "' on bar '" + Code + "'.");
        }

        await menuItem.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
    }

    /// <summary>
    /// Deals with the message box Classic may show right after the click. Two buttons is the
    /// confirmation the page is configured to ask, and the first button is its "yes". One button is a
    /// message the page wants read -- a validation error that stops the move, or a note that it went
    /// through ("the order was cancelled") -- so it is dismissed and its caption handed back: whether
    /// it was a refusal is decided by the stage column afterwards, not by the box.
    /// </summary>
    /// <returns>The caption of a one-button message, or null when no message box appeared.</returns>
    private async Task<string?> AnswerConfirmationAsync()
    {
        var dialog = Page.Locator(MessageBoxSelector);
        try
        {
            await dialog.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = DialogWaitMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (PlaywrightException)
        {
            return null;
        }

        var buttons = dialog.First.Locator(MessageBoxButtonSelector);
        var count = await buttons.CountAsync().ConfigureAwait(false);
        var caption = (await dialog.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
        var firstLine = caption.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? caption;

        if (count >= 2)
        {
            Logger.LogDebug("Answering the stage-change confirmation on {Code}: {Caption}", Code, firstLine);
            await buttons.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
            return null;
        }

        if (count == 1)
        {
            await buttons.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        }
        else
        {
            await Page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
        }

        return firstLine;
    }

    /// <summary>The dashboard saves the record and reloads itself; wait for the network to go quiet, then settle.</summary>
    private async Task WaitForSaveAsync()
    {
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = Context.Settings.PageLoadTimeoutMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Logger.LogWarning("The page did not go quiet after switching stage on {Code}.", Code);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Waiting for the save after switching stage on {Code} failed.", Code);
        }

        if (_options.SettleMs > 0)
        {
            await Page.WaitForTimeoutAsync(_options.SettleMs).ConfigureAwait(false);
        }
    }
}
