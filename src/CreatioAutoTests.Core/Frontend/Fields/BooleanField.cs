using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>Checkbox field with Creatio-specific toggle and disabled-state handling.</summary>
[UiElement("Boolean", HostTag = "crt-checkbox", ValueKind = ValueKind.Boolean)]
public sealed class BooleanField : FieldElementBase
{
    /// <summary>Initializes the checkbox against a configured Creatio control.</summary>
    public BooleanField(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Checkbox-specific caption rather than the generic input-label element.</summary>
    protected override ILocator LabelLocator => Root.Locator(".crt-checkbox-label");

    /// <summary>
    /// Creatio 8.3 renders the checkbox through Material MDC, whose native input is
    /// <c>mdc-checkbox__native-control</c>; older builds used <c>mat-checkbox-input</c>. The plain
    /// type selector is the last resort so a further rename does not break reading the value.
    /// </summary>
    protected override ILocator ValueLocator => Root.Locator(
        "input.mdc-checkbox__native-control, input.mat-checkbox-input, input[type=\"checkbox\"]");

    /// <summary>Returns the checked state in the exact <c>true</c> or <c>false</c> DSL form.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await ValueLocator.IsCheckedAsync().ConfigureAwait(false) ? "true" : "false";
    }

    /// <summary>
    /// Changes the checked state using visible MDC targets before dispatching on the native input.
    /// Creatio builds disagree about which target receives a click, while clicking the host does not
    /// toggle an MDC checkbox at all.
    /// </summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        bool target;
        if (value == "true")
        {
            target = true;
        }
        else if (value == "false")
        {
            target = false;
        }
        else
        {
            throw new InvalidOperationException(
                "Field '" + Code + "' expects 'true' or 'false' but received '" + value + "'.");
        }

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var input = ValueLocator;
        if (await input.IsCheckedAsync().ConfigureAwait(false) == target)
        {
            return;
        }

        // Material MDC hides the native input behind a styled box and overlays a touch target.
        // Which of them accepts a click varies with the build, so try the visible ones and fall back
        // to dispatching the event on the input itself, which Angular listens to either way.
        var clicked = await TryClickAsync(Root.Locator(".mat-mdc-checkbox-touch-target")).ConfigureAwait(false)
                      || await TryClickAsync(Root.Locator(".mdc-checkbox")).ConfigureAwait(false)
                      || await TryClickAsync(Root.Locator("label.mat-checkbox-layout")).ConfigureAwait(false);

        if (!clicked || await input.IsCheckedAsync().ConfigureAwait(false) != target)
        {
            await input.DispatchEventAsync("click").ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(200).ConfigureAwait(false);
        }

        if (await input.IsCheckedAsync().ConfigureAwait(false) != target)
        {
            throw new InvalidOperationException(
                "Clicking checkbox '" + Code + "' did not change its state to '" + target.ToString().ToLowerInvariant() + "'.");
        }
    }

    /// <summary>Clicks a candidate if it is there and clickable, reporting whether it worked.</summary>
    private static async Task<bool> TryClickAsync(ILocator candidate)
    {
        try
        {
            if (await candidate.CountAsync().ConfigureAwait(false) == 0)
            {
                return false;
            }

            await candidate.First.ClickAsync(new LocatorClickOptions { Timeout = 2000 }).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>Clears the checkbox by setting it to <c>false</c>.</summary>
    public override Task ClearAsync(CancellationToken cancellationToken = default) =>
        SetValueFromStringAsync("false", cancellationToken);

    /// <summary>Returns false because Creatio checkboxes are never required controls.</summary>
    public override Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>Recognizes disabled markers from both older Material and MDC checkbox markup.</summary>
    public override async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        var input = ValueLocator;
        var disabledAttribute = await SafeAttributeAsync(input, "disabled").ConfigureAwait(false);
        var classAttribute = await SafeAttributeAsync(input, "class").ConfigureAwait(false);

        var disabled = await SafeIsDisabledAsync(input).ConfigureAwait(false);

        return disabled
               || IsFlagSet(disabledAttribute)
               || (classAttribute?.Contains("mat-checkbox-disabled", StringComparison.OrdinalIgnoreCase) ?? false)
               || (classAttribute?.Contains("mdc-checkbox--disabled", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}