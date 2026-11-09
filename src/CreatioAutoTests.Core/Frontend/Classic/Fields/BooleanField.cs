using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic checkbox (<c>CheckBoxEdit</c>). The <c>-el</c> node is the native checkbox, styled out of
/// sight, so Playwright's visibility-checked click cannot reach it: the click is forced, and the
/// result is read back from the view model rather than trusted.
/// </summary>
[UiElement("Boolean", Generation = UiGeneration.ClassicUI, Controls = new[] { "CheckBoxEdit" },
    ValueKind = ValueKind.Boolean, Options = typeof(ClassicElementOptions))]
public sealed class BooleanField : ClassicFieldBase
{
    /// <summary>Initializes a Classic checkbox.</summary>
    public BooleanField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>A styled-away checkbox is present when it is attached, not when it is visible.</summary>
    public override async Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        try
        {
            await Root.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
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

    /// <summary>Returns the checked state in the exact <c>true</c> or <c>false</c> DSL form.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var model = await ReadModelValueAsync().ConfigureAwait(false);
        if (model.Kind == "boolean")
        {
            return model.Text;
        }

        return await Root.IsCheckedAsync().ConfigureAwait(false) ? "true" : "false";
    }

    /// <summary>Toggles the box when needed and confirms the change through the view model.</summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        var target = value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException(
                "Field '" + Code + "' expects 'true' or 'false' but received '" + value + "'.")
        };

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (await IsCheckedAsync().ConfigureAwait(false) == target)
        {
            return;
        }

        try
        {
            await Root.ClickAsync(new LocatorClickOptions { Force = true, Timeout = TimeoutMs }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            Logger.LogDebug(ex, "Forced click on checkbox {Code} failed; dispatching the event instead.", Code);
            await Root.DispatchEventAsync("click").ConfigureAwait(false);
        }

        await Page.WaitForTimeoutAsync(200).ConfigureAwait(false);

        if (await IsCheckedAsync().ConfigureAwait(false) != target)
        {
            await Root.DispatchEventAsync("click").ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(200).ConfigureAwait(false);
        }

        if (await IsCheckedAsync().ConfigureAwait(false) != target)
        {
            throw new InvalidOperationException(
                "Clicking checkbox '" + Code + "' did not change its state to '" + value + "'.");
        }
    }

    /// <summary>Clears the checkbox by setting it to <c>false</c>.</summary>
    public override Task ClearAsync(CancellationToken cancellationToken = default) =>
        SetValueFromStringAsync("false", cancellationToken);

    /// <summary>A checkbox is never required: it always holds a value.</summary>
    public override Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>A checkbox has no box with a disabled marker; the control's <c>enabled</c> and the input's state decide.</summary>
    public override async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        if (await ResolveComponentIdAsync().ConfigureAwait(false) is null)
        {
            throw NotFound();
        }

        if (await SafeIsDisabledAsync(Root).ConfigureAwait(false))
        {
            return true;
        }

        return IsTrue(await EvaluateOnComponentAsync("c => c.enabled === false").ConfigureAwait(false));
    }

    private async Task<bool> IsCheckedAsync()
    {
        var model = await ReadModelValueAsync().ConfigureAwait(false);
        return model.Kind == "boolean"
            ? model.Text == "true"
            : await Root.IsCheckedAsync().ConfigureAwait(false);
    }
}
