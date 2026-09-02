using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Base for every data field. Provides required / read-only detection shared by Freedom UI controls
/// and reduces a field to one pair of string accessors, which is all the command layer ever sees.
/// </summary>
public abstract class FieldElementBase
    : UiElementBase, IStateAware, IValueElement, IClearable
{
    protected FieldElementBase(ElementRuntimeContext context) : base(context)
    {
    }

    public abstract Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default);

    public abstract Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default);

    public virtual Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return SetValueFromStringAsync(string.Empty, cancellationToken);
    }

    public virtual async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        var input = ValueLocator;

        var requiredAttr = await SafeAttributeAsync(input, "required").ConfigureAwait(false);
        var ariaRequired = await SafeAttributeAsync(input, "aria-required").ConfigureAwait(false);
        var labelClass = await SafeLabelClassAsync().ConfigureAwait(false);

        return IsFlagSet(requiredAttr)
               || string.Equals(ariaRequired, "true", StringComparison.OrdinalIgnoreCase)
               || labelClass.Contains("crt-input-required", StringComparison.OrdinalIgnoreCase);
    }

    public virtual async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        var input = ValueLocator;

        var rootReadonly = await SafeAttributeAsync(Root, "readonly").ConfigureAwait(false);
        var inputReadonly = await SafeAttributeAsync(input, "readonly").ConfigureAwait(false);
        var ariaReadonly = await SafeAttributeAsync(input, "aria-readonly").ConfigureAwait(false);

        var disabled = await SafeIsDisabledAsync(input).ConfigureAwait(false);

        var lockIcons = 0;
        try
        {
            lockIcons = await Root.Locator(".readonly-icon").CountAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
        }

        return IsFlagSet(rootReadonly)
               || IsFlagSet(inputReadonly)
               || string.Equals(ariaReadonly, "true", StringComparison.OrdinalIgnoreCase)
               || disabled
               || lockIcons > 0;
    }

    /// <summary>
    /// Timeout for probing an attribute. These calls ask "is this flag set", so a control that has
    /// no such sub-element must answer quickly rather than block for the full element timeout.
    /// </summary>
    protected const int ProbeTimeoutMs = 1000;

    /// <summary>
    /// Reads an attribute, treating "the element is not there" as "the flag is not set".
    /// Playwright signals a missing element with TimeoutException rather than PlaywrightException,
    /// so both have to be caught: a read-only Creatio field often renders no input at all.
    /// </summary>
    protected static async Task<string?> SafeAttributeAsync(ILocator locator, string name)
    {
        try
        {
            return await locator
                .GetAttributeAsync(name, new LocatorGetAttributeOptions { Timeout = ProbeTimeoutMs })
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>Probes the disabled state, reporting false when the element is not present.</summary>
    protected static async Task<bool> SafeIsDisabledAsync(ILocator locator)
    {
        try
        {
            return await locator
                .IsDisabledAsync(new LocatorIsDisabledOptions { Timeout = ProbeTimeoutMs })
                .ConfigureAwait(false);
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

    private async Task<string> SafeLabelClassAsync()
    {
        try
        {
            if (await LabelLocator.CountAsync().ConfigureAwait(false) == 0)
            {
                return string.Empty;
            }

            return await LabelLocator.First.GetAttributeAsync("class").ConfigureAwait(false) ?? string.Empty;
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }

    /// <summary>Writes a value into an input Creatio keeps hidden and raises the events Angular listens to.</summary>
    protected static Task SetValueThroughDomAsync(ILocator input, string value)
    {
        const string script = @"(el, v) => {
            if (!el) { return; }
            el.value = v ?? '';
            el.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            el.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
        }";

        return input.EvaluateAsync(script, value);
    }
}
