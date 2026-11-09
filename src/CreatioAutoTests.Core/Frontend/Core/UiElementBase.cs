using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Shared behaviour of every element whatever its UI generation: resolve the root locator,
/// report presence and read the rendered caption. How the root is found is the one thing the
/// generations disagree on, so <see cref="BuildSelector"/> is left to <see cref="FreedomElementBase"/>
/// and <see cref="Classic.ClassicElementBase"/>; everything above it is identical.
/// </summary>
public abstract class UiElementBase : IUiElement, IExistable, ILabeled
{
    private ILocator? _root;

    /// <summary>Initializes an element against its configured definition and current browser page.</summary>
    protected UiElementBase(ElementRuntimeContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Page-bound dependencies shared by all element operations.</summary>
    protected ElementRuntimeContext Context { get; }

    /// <summary>Page that supplies locators for this element.</summary>
    protected IPage Page => Context.Page;

    /// <summary>Static definition that provides the code and expected label.</summary>
    protected ElementDefinition Definition => Context.Definition;

    /// <summary>Element-scoped diagnostic logger.</summary>
    protected ILogger Logger => Context.Logger;

    /// <summary>Configured wait budget for controls expected to be present.</summary>
    protected int TimeoutMs => Context.Settings.ElementTimeoutMs;

    /// <summary>Code by which the DSL addresses the element on its page.</summary>
    public string Code => Definition.Code;

    /// <summary>Configured caption checked when presence is asserted.</summary>
    public string Label => Definition.Label;

    /// <summary>
    /// CSS selector of the element's root. Elements are addressed strictly by their configured
    /// identity; the caption is verified separately by 'check X exists' so that renaming a caption in
    /// Creatio produces a readable failure instead of "element not found".
    /// </summary>
    protected abstract string BuildSelector();

    /// <summary>How the element is located, for diagnostics: a host tag, a DOM id.</summary>
    protected virtual string LocatorDescription => BuildSelector();

    /// <summary>Host locator, cached because every element operation starts from the same control.</summary>
    protected ILocator Root => _root ??= Page.Locator(BuildSelector()).First;

    /// <summary>Element that carries the value. Overridden per control type.</summary>
    protected abstract ILocator ValueLocator { get; }

    /// <summary>Element that carries the caption. Overridden per control type.</summary>
    protected abstract ILocator LabelLocator { get; }

    /// <summary>Checks presence using the normal configured element timeout.</summary>
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        ExistsAsync(TimeoutMs, cancellationToken);

    /// <summary>Checks presence using a caller-provided budget, primarily for expected absence.</summary>
    public virtual async Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        try
        {
            await Root.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            return true;
        }
        catch (TimeoutException)
        {
            Logger.LogDebug("Element {Code} ({Where}) was not visible within {Timeout} ms.", Code, LocatorDescription, timeoutMs);
            return false;
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Element {Code} ({Where}) lookup failed.", Code, LocatorDescription);
            return false;
        }
    }

    /// <summary>Reads the rendered caption for comparison with the configured label.</summary>
    public virtual async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await LabelLocator.CountAsync().ConfigureAwait(false) == 0)
            {
                return null;
            }

            var raw = await LabelLocator.First.InnerTextAsync().ConfigureAwait(false);
            return NormalizeLabel(raw);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading label of {Code} failed.", Code);
            return null;
        }
    }

    /// <summary>Strips the required marker and trailing punctuation Creatio adds to captions.</summary>
    public static string NormalizeLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Trim().TrimEnd(' ', '*', ':').Trim();
    }

    /// <summary>Treats null, empty and "false" attribute values as not set.</summary>
    protected static bool IsFlagSet(string? attributeValue)
    {
        return !string.IsNullOrEmpty(attributeValue)
               && !string.Equals(attributeValue, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the consistent error used when an expected configured control is absent.</summary>
    protected InvalidOperationException NotFound()
    {
        return new InvalidOperationException(
            "Element '" + Code + "' (" + LocatorDescription + ") was not found on page '" + Definition.Page + "'.");
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

    /// <summary>Writes a value into an input Creatio keeps hidden and raises the events the page listens to.</summary>
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

    /// <summary>Identifies the concrete element and its configuration code in diagnostics.</summary>
    public override string ToString() => GetType().Name + "(" + Code + ")";
}
