using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Controls;

/// <summary>
/// Freedom UI button (crt-button). Save behaviour is data driven through <see cref="ButtonOptions"/>
/// rather than a hard-coded type enum, so a project can describe its own buttons in configuration.
/// </summary>
[UiElement("Button", HostTag = "crt-button", Options = typeof(ButtonOptions))]
public sealed class Button : UiElementBase, IClickable
{
    private const string NotificationSelector = "output[data-qa=\"notificationPopup\"].crt-notify-message";

    private readonly ButtonOptions _options;

    /// <summary>Initializes a button using behavior supplied by its configuration options.</summary>
    public Button(ElementRuntimeContext context) : base(context)
    {
        _options = context.Definition.BindOptions<ButtonOptions>();
    }


    /// <summary>Supports the caption variants used by Freedom UI button implementations.</summary>
    protected override ILocator LabelLocator =>
        Root.Locator(".crt-button-caption, .btn-reversible-content, .mdc-button__label");

    private ILocator Clickable => Root.Locator("button, .mdc-button, .mat-mdc-unelevated-button");

    /// <summary>
    /// Performs the <c>click</c> command and applies configured save confirmation behavior. A
    /// configured second Save click is a workaround for Creatio dropping the first click, not a
    /// timing guess.
    /// </summary>
    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        await EnsureEnabledAsync().ConfigureAwait(false);

        var target = await ResolveClickTargetAsync().ConfigureAwait(false);
        await target.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        if (_options.SettleMs > 0)
        {
            await Page.WaitForTimeoutAsync(_options.SettleMs).ConfigureAwait(false);
        }

        if (!_options.IsSave)
        {
            return;
        }

        await CompleteSaveAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ILocator> ResolveClickTargetAsync()
    {
        return await Clickable.CountAsync().ConfigureAwait(false) > 0 ? Clickable.First : Root;
    }

    private async Task EnsureEnabledAsync()
    {
        var ariaDisabled = await SafeAttributeAsync(Root, "aria-disabled").ConfigureAwait(false);
        var disabled = await SafeAttributeAsync(Root, "disabled").ConfigureAwait(false);

        if (string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase) || disabled is not null)
        {
            throw new InvalidOperationException("Button '" + Code + "' is disabled and cannot be clicked.");
        }
    }

    /// <summary>Verifies a save actually went through: no error popup and the button is gone.</summary>
    private async Task CompleteSaveAsync(ILocator target, CancellationToken cancellationToken)
    {
        var error = await ReadErrorNotificationAsync().ConfigureAwait(false);
        if (error is not null)
        {
            throw new InvalidOperationException("Save failed. Creatio reported: " + error);
        }

        if (_options.DoubleClick)
        {
            try
            {
                await target.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                // Expected when the first click already saved and the button went away.
                Logger.LogDebug(ex, "Second Save click on {Code} was not needed.", Code);
            }
        }

        try
        {
            // Hidden rather than detached: some pages remove the Save button from the DOM once the
            // record is stored, others keep the host and collapse it. Waiting for removal reports a
            // save that plainly worked as a failure on every page of the second kind.
            await Root.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = _options.DisappearTimeoutMs
            }).ConfigureAwait(false);

            return;
        }
        catch (TimeoutException)
        {
        }

        var lateError = await ReadErrorNotificationAsync().ConfigureAwait(false);
        if (lateError is not null)
        {
            throw new InvalidOperationException("Save failed. Creatio reported: " + lateError);
        }

        throw new InvalidOperationException(
            "Save button '" + Code + "' was still showing after " + _options.DisappearTimeoutMs +
            " ms and no error was shown; the record was probably not saved.");
    }

    private async Task<string?> ReadErrorNotificationAsync()
    {
        try
        {
            var popup = Page.Locator(NotificationSelector);
            if (await popup.CountAsync().ConfigureAwait(false) == 0)
            {
                return null;
            }

            var text = await popup.First.InnerTextAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<string?> SafeAttributeAsync(ILocator locator, string name)
    {
        try
        {
            return await locator.GetAttributeAsync(name).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
