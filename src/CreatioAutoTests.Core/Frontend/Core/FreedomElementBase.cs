using System.Reflection;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>
/// Shared behaviour of every Freedom UI element: the root is the custom host tag carrying the
/// <c>element-name</c> attribute, the caption is the <c>crt-input-label</c> inside it, and the value
/// is an Angular Material input.
/// </summary>
public abstract class FreedomElementBase : UiElementBase
{
    private static readonly Dictionary<Type, string> HostTagCache = new();

    /// <summary>Initializes a Freedom UI element against its configured definition and current browser page.</summary>
    protected FreedomElementBase(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>
    /// Freedom UI custom tag that hosts this element, taken from the type's [UiElement] attribute.
    /// A base class shared by several types leaves it there; only a type with no attribute of its
    /// own, which cannot be registered anyway, would need to override this.
    /// </summary>
    protected virtual string HostTag => HostTagOf(GetType());

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

    /// <summary>Elements are addressed strictly by element-name on their host tag.</summary>
    protected override string BuildSelector() => HostTag + "[element-name=\"" + Code + "\"]";

    /// <summary>The host tag names the element in diagnostics, as it always has.</summary>
    protected override string LocatorDescription => HostTag;

    /// <summary>
    /// Input rendered by Angular Material. Creatio 8.3 emits <c>mat-mdc-input-element</c>; older
    /// builds emit <c>mat-input-element</c>. Matching both keeps one selector working across
    /// versions, and getting this wrong is silent: the host resolves, the value never does.
    /// </summary>
    protected const string MaterialInputSelector =
        "input.mat-mdc-input-element, input.mat-input-element";

    /// <summary>Element that carries the value. Overridden per control type.</summary>
    protected override ILocator ValueLocator => Root.Locator("input, textarea");

    /// <summary>Element that carries the caption. Overridden per control type.</summary>
    protected override ILocator LabelLocator => Root.Locator(".crt-input-label");
}
