using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic;

/// <summary>
/// Shared behaviour of every Classic UI element. The root is the control's <c>-el</c> node, found
/// by the id Classic builds from page schema, element name and control class; the caption is the
/// <c>Label</c> node with the same prefix.
///
/// Classic also exposes the live control through <c>Ext.getCmp</c>, and through it the page's view
/// model: the bound column, its typed value, whether the control is enabled or required. Reading
/// those is more reliable than parsing what the DOM shows in the user's culture, so the element
/// classes do it for anything that is not plain text. <see cref="EvaluateOnComponentAsync(string, object?)"/>
/// is the one door to that.
/// </summary>
public abstract class ClassicElementBase : UiElementBase
{
    private static readonly Dictionary<Type, IReadOnlyList<string>> ControlsCache = new();
    private string? _componentId;

    /// <summary>Initializes a Classic element from its definition and the options its type binds.</summary>
    protected ClassicElementBase(ElementRuntimeContext context, ClassicElementOptions options) : base(context)
    {
        ArgumentNullException.ThrowIfNull(options);

        Schema = context.PageDefinition?.Schema?.Trim() ?? string.Empty;
        if (Schema.Length == 0)
        {
            throw new InvalidOperationException(
                "Classic UI element '" + Code + "' needs the page schema name, but page '" + Definition.Page +
                "' declares no \"schema\".");
        }

        Name = string.IsNullOrWhiteSpace(options.Name) ? Code : options.Name.Trim();
    }

    /// <summary>Page schema name that prefixes every id on the page.</summary>
    protected string Schema { get; }

    /// <summary>Element name in the schema's <c>diff</c>: the code unless the page file says otherwise.</summary>
    protected string Name { get; }

    /// <summary>Control classes this element type covers, from its [UiElement] attribute.</summary>
    protected virtual IReadOnlyList<string> Controls => ControlsOf(GetType());

    /// <summary>Reads and caches the controls declared by a registered Classic element type.</summary>
    internal static IReadOnlyList<string> ControlsOf(Type type)
    {
        lock (ControlsCache)
        {
            if (ControlsCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var controls = type.GetCustomAttribute<UiElementAttribute>()?.Controls ?? Array.Empty<string>();
            if (controls.Length == 0)
            {
                throw new InvalidOperationException(
                    type.FullName + " does not declare Controls on its [UiElement] attribute.");
            }

            ControlsCache[type] = controls;
            return controls;
        }
    }

    /// <summary>Ext component id of one of the declared controls.</summary>
    protected string ComponentId(string control) => ClassicIds.Component(Schema, Name, control);

    /// <summary>One alternative per declared control; the first that exists is the root.</summary>
    protected override string BuildSelector() =>
        string.Join(", ", Controls.Select(c => ClassicIds.Selector(ClassicIds.Element(Schema, Name, c))));

    /// <inheritdoc />
    protected override string LocatorDescription => ClassicIds.Describe(Schema, Name, Controls);

    /// <summary>In Classic the <c>-el</c> node is the input itself.</summary>
    protected override ILocator ValueLocator => Root;

    /// <summary>The <c>Label</c> node sharing the element's prefix.</summary>
    protected override ILocator LabelLocator => Page.Locator(ClassicIds.Selector(ClassicIds.Label(Schema, Name)));

    /// <summary>
    /// Which of the declared controls the page rendered, as its component id. Cached after the
    /// first answer: a control does not change class while the page is open.
    /// </summary>
    protected async Task<string?> ResolveComponentIdAsync()
    {
        if (_componentId is not null)
        {
            return _componentId;
        }

        var candidates = Controls.Select(ComponentId).ToArray();
        try
        {
            var found = await Page.EvaluateAsync<string?>(
                "ids => ids.find(id => document.getElementById(id + '-el')) ?? null",
                candidates).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(found))
            {
                _componentId = found;
            }

            return found;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>Runs <paramref name="function"/> against this element's Ext component.</summary>
    /// <param name="function">JavaScript source of a function <c>(cmp, arg) =&gt; result</c>.</param>
    /// <param name="argument">Serialised and passed as the function's second parameter.</param>
    protected async Task<JsonElement> EvaluateOnComponentAsync(string function, object? argument = null)
    {
        var id = await ResolveComponentIdAsync().ConfigureAwait(false) ?? throw NotFound();
        return await EvaluateOnComponentAsync(id, function, argument).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="function"/> against any Ext component on the page, by id.</summary>
    protected async Task<JsonElement> EvaluateOnComponentAsync(string componentId, string function, object? argument)
    {
        const string wrapper = @"([id, source, arg]) => {
            const cmp = (window.Ext && Ext.getCmp) ? Ext.getCmp(id) : null;
            if (!cmp) { return null; }
            const fn = (0, eval)('(' + source + ')');
            const result = fn(cmp, arg);
            return result === undefined ? null : result;
        }";

        return await Page.EvaluateAsync<JsonElement>(wrapper, new object?[] { componentId, function, argument })
            .ConfigureAwait(false);
    }

    /// <summary>True for a JSON null, an undefined result and a default <see cref="JsonElement"/>.</summary>
    protected static bool IsNull(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>Whether the result is the boolean <c>true</c>.</summary>
    protected static bool IsTrue(JsonElement element) => element.ValueKind == JsonValueKind.True;

    /// <summary>Text content of a node, rather than its rendered text: Classic upper-cases captions in CSS.</summary>
    protected static async Task<string?> TextContentAsync(ILocator locator)
    {
        try
        {
            if (await locator.CountAsync().ConfigureAwait(false) == 0)
            {
                return null;
            }

            return await locator.First.EvaluateAsync<string>("el => el.textContent ?? ''").ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
