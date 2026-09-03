using CreatioAutoTests.Configuration;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Shared behaviour of every Freedom UI element: resolve the host tag by element-name,
/// report presence and read the rendered caption.
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

    /// <summary>Static definition that provides the element-name and expected label.</summary>
    protected ElementDefinition Definition => Context.Definition;

    /// <summary>Element-scoped diagnostic logger.</summary>
    protected ILogger Logger => Context.Logger;

    /// <summary>Configured wait budget for controls expected to be present.</summary>
    protected int TimeoutMs => Context.Settings.ElementTimeoutMs;

    /// <summary>Value of the Freedom UI host's <c>element-name</c> attribute.</summary>
    public string Code => Definition.Code;

    /// <summary>Configured caption checked when presence is asserted.</summary>
    public string Label => Definition.Label;

    /// <summary>
    /// Freedom UI custom tag that hosts this element, taken from the type's [UiElement] attribute.
    /// A base class shared by several types leaves it there; only a type with no attribute of its
    /// own, which cannot be registered anyway, would need to override this.
    /// </summary>
    protected virtual string HostTag => HostTagOf(GetType());

    private static readonly Dictionary<Type, string> HostTagCache = new();

    /// <summary>Reads and caches the host tag declared by a registered element implementation.</summary>
    internal static string HostTagOf(Type type)
    {
        lock (HostTagCache)
        {
            if (HostTagCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var attribute = type.GetCustomAttribute<UiElementAttribute>();
            var tag = attribute?.HostTag ?? string.Empty;

            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidOperationException(
                    type.FullName + " does not declare a HostTag on its [UiElement] attribute.");
            }

            HostTagCache[type] = tag;
            return tag;
        }
    }

    /// <summary>
    /// Elements are addressed strictly by element-name. The caption is verified separately by
    /// 'check X exists' so that renaming a caption in Creatio produces a readable failure
    /// instead of "element not found".
    /// </summary>
    protected virtual string BuildSelector() => HostTag + "[element-name=\"" + Code + "\"]";

    /// <summary>Host locator, cached because every element operation starts from the same control.</summary>
    protected ILocator Root => _root ??= Page.Locator(BuildSelector()).First;

    /// <summary>
    /// Input rendered by Angular Material. Creatio 8.3 emits <c>mat-mdc-input-element</c>; older
    /// builds emit <c>mat-input-element</c>. Matching both keeps one selector working across
    /// versions, and getting this wrong is silent: the host resolves, the value never does.
    /// </summary>
    protected const string MaterialInputSelector =
        "input.mat-mdc-input-element, input.mat-input-element";

    /// <summary>Element that carries the value. Overridden per control type.</summary>
    protected virtual ILocator ValueLocator => Root.Locator("input, textarea");

    /// <summary>Element that carries the caption. Overridden per control type.</summary>
    protected virtual ILocator LabelLocator => Root.Locator(".crt-input-label");

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
            Logger.LogDebug("Element {Code} ({Tag}) was not visible within {Timeout} ms.", Code, HostTag, timeoutMs);
            return false;
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Element {Code} ({Tag}) lookup failed.", Code, HostTag);
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
            "Element '" + Code + "' (" + HostTag + ") was not found on page '" + Definition.Page + "'.");
    }

    /// <summary>Identifies the concrete element and its configuration code in diagnostics.</summary>
    public override string ToString() => GetType().Name + "(" + Code + ")";
}
