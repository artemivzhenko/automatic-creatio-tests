using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Base for every Freedom UI data field. Provides required / read-only detection shared by Freedom UI
/// controls and reduces a field to one pair of string accessors, which is all the command layer ever sees.
/// </summary>
public abstract class FieldElementBase
    : FreedomElementBase, IStateAware, IValueElement, IClearable
{
    /// <summary>Initializes the common field behavior for one configured control.</summary>
    protected FieldElementBase(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>Reads the field in the string representation shared by DSL commands.</summary>
    public abstract Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the string representation supplied by the DSL.</summary>
    public abstract Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default);

    /// <summary>Clears a field by delegating to its own string writer with an empty value.</summary>
    public virtual Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return SetValueFromStringAsync(string.Empty, cancellationToken);
    }

    /// <summary>Detects the required markers Freedom UI may place on an input or its label.</summary>
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

    /// <summary>Detects the different readonly markers emitted by Freedom UI controls.</summary>
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
}
