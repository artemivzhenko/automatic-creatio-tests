using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Composite;

/// <summary>
/// Stage progress bar (crt-entity-stage-progress-bar). Exposed as a value element so that
/// 'set X "stage"' and 'check-value X eq "stage"' work on it without any special case in a command.
/// </summary>
[UiElement("CasesBar", HostTag = "crt-entity-stage-progress-bar", ValueKind = ValueKind.Stage, Options = typeof(CasesBarOptions))]
public sealed class CasesBar : UiElementBase, IValueElement
{
    private const string ToggleSelector = ".button-toggle[data-item-marker]";
    private const string SelectedToggleSelector = ".button-toggle.selected[data-item-marker]";
    private const string MenuArrowSelector = "mat-icon[svgicon=\"menu-arrow\"]";
    private const string MenuPanelSelector = ".cdk-overlay-pane .mat-menu-panel";
    private const string BackdropSelector = ".cdk-overlay-backdrop";
    private const int MenuOpenWaitMs = 300;
    private const int OverlayTimeoutMs = 5000;

    private readonly CasesBarOptions _options;

    /// <summary>Initializes the configured stage bar and its declared stage hierarchy.</summary>
    public CasesBar(ElementRuntimeContext context) : base(context)
    {
        _options = context.Definition.BindOptions<CasesBarOptions>();
    }


    /// <summary>Stages declared in configuration, in the order they are expected on screen.</summary>
    public IReadOnlyList<string> ExpectedStages => _options.Values;

    /// <summary>Stage-bar caption element used by the shared label assertion.</summary>
    protected override ILocator LabelLocator => Root.Locator(".crt-input-label");

    /// <summary>Returns the currently selected stage for <c>check-value</c> without command special cases.</summary>
    public async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var selected = Root.Locator(SelectedToggleSelector);
        if (await selected.CountAsync().ConfigureAwait(false) == 0)
        {
            return string.Empty;
        }

        var marker = await selected.First.GetAttributeAsync("data-item-marker").ConfigureAwait(false);
        return marker?.Trim() ?? string.Empty;
    }

    /// <summary>Reads every stage currently rendered, in display order.</summary>
    public async Task<IReadOnlyList<string>> GetAvailableStagesAsync()
    {
        var toggles = Root.Locator(ToggleSelector);
        var count = await toggles.CountAsync().ConfigureAwait(false);
        var result = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var marker = await toggles.Nth(i).GetAttributeAsync("data-item-marker").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(marker))
            {
                result.Add(marker.Trim());
            }
        }

        return result;
    }

    /// <summary>
    /// Selects a top-level stage or menu-backed sub-stage for <c>set</c>, then dismisses the CDK
    /// backdrop that Creatio can leave behind and which would otherwise swallow later clicks.
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
        if (string.Equals(current, stage, StringComparison.Ordinal))
        {
            Logger.LogDebug("Stage bar {Code} is already on '{Stage}'.", Code, stage);
            return;
        }

        var parentStage = FindParentStage(stage);
        if (parentStage is null)
        {
            await ClickStageAsync(stage).ConfigureAwait(false);
        }
        else
        {
            await ClickSubStageAsync(parentStage, stage).ConfigureAwait(false);
        }

        await DismissOverlayAsync().ConfigureAwait(false);
    }

    /// <summary>Returns the stage that hides <paramref name="stage"/> behind its menu, if any.</summary>
    private string? FindParentStage(string stage)
    {
        if (_options.Subvalues is null)
        {
            return null;
        }

        foreach (var pair in _options.Subvalues)
        {
            if (pair.Value.Contains(stage, StringComparer.Ordinal))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private async Task ClickStageAsync(string stage)
    {
        var toggle = Root.Locator(".button-toggle[data-item-marker=\"" + stage + "\"]");
        if (await toggle.CountAsync().ConfigureAwait(false) == 0)
        {
            var available = await GetAvailableStagesAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Stage '" + stage + "' is not on bar '" + Code + "'. Available: " +
                (available.Count == 0 ? "(none)" : string.Join(", ", available)) + ".");
        }

        var button = toggle.Locator("button");
        var clickable = await button.CountAsync().ConfigureAwait(false) > 0 ? button.First : toggle.First;

        await clickable.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
    }

    private async Task ClickSubStageAsync(string parentStage, string stage)
    {
        var parent = Root.Locator(".button-toggle[data-item-marker=\"" + parentStage + "\"]");
        if (await parent.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "Parent stage '" + parentStage + "' of sub-stage '" + stage + "' is not on bar '" + Code + "'.");
        }

        var arrow = parent.Locator(MenuArrowSelector);
        if (await arrow.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "Stage '" + parentStage + "' has no menu arrow, so sub-stage '" + stage + "' cannot be reached.");
        }

        await arrow.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(MenuOpenWaitMs).ConfigureAwait(false);

        var menu = Page.Locator(MenuPanelSelector);
        if (await menu.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException("Sub-stage menu of '" + parentStage + "' did not open.");
        }

        var item = menu.Locator("button[data-item-marker=\"" + stage + "\"]");
        if (await item.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "Sub-stage '" + stage + "' is not in the menu of '" + parentStage + "'.");
        }

        await item.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
    }

    /// <summary>
    /// A stage change can leave a CDK backdrop that swallows later clicks.
    /// Wait for it to go and press Escape if it overstays.
    /// </summary>
    private async Task DismissOverlayAsync()
    {
        var backdrop = Page.Locator(BackdropSelector);

        try
        {
            if (await backdrop.CountAsync().ConfigureAwait(false) == 0)
            {
                return;
            }

            await backdrop.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Detached,
                Timeout = OverlayTimeoutMs
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(500).ConfigureAwait(false);

            if (await backdrop.CountAsync().ConfigureAwait(false) > 0)
            {
                Logger.LogWarning("Overlay above stage bar {Code} is still present after Escape.", Code);
            }
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Overlay check after switching stage on {Code} failed.", Code);
        }
    }
}
