using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic;

/// <summary>
/// Base for every Classic UI data field: required and read-only detection from the markers Classic
/// renders, the view-model read every typed field relies on, and the commit gesture a value needs.
///
/// Classic commits an edit on the input's <c>change</c> event, which fires when focus leaves the
/// control. Filling the input is not enough; <see cref="FillAndCommitAsync"/> tabs out afterwards.
/// </summary>
public abstract class ClassicFieldBase : ClassicElementBase, IStateAware, IValueElement, IClearable
{
    /// <summary>Initializes a Classic field, binding the shared options every field accepts.</summary>
    protected ClassicFieldBase(ElementRuntimeContext context)
        : base(context, context.Definition.BindOptions<ClassicElementOptions>())
    {
    }

    /// <summary>Reads the field in the string representation shared by DSL commands.</summary>
    public abstract Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the string representation supplied by the DSL.</summary>
    public abstract Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default);

    /// <summary>Clears a field by delegating to its own string writer with an empty value.</summary>
    public virtual Task ClearAsync(CancellationToken cancellationToken = default) =>
        SetValueFromStringAsync(string.Empty, cancellationToken);

    /// <summary>
    /// Required: the caption carries <c>t-label-is-required</c>, or the control says so. The caption
    /// is asked first because it is what the user sees; the control covers a field whose caption is
    /// hidden by layout.
    /// </summary>
    public virtual async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        var labelClass = await SafeAttributeAsync(LabelLocator, "class").ConfigureAwait(false) ?? string.Empty;
        if (labelClass.Contains("t-label-is-required", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (await ResolveComponentIdAsync().ConfigureAwait(false) is null)
        {
            throw NotFound();
        }

        return IsTrue(await EvaluateOnComponentAsync("c => c.isRequired === true").ConfigureAwait(false));
    }

    /// <summary>
    /// Read-only: the box carries <c>base-edit-disabled</c>, the input is <c>readonly</c> or
    /// disabled, or the control's <c>enabled</c> is false. Classic sets all of them together when a
    /// business rule locks a field, so any one of them is the answer.
    /// </summary>
    public virtual async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        var id = await ResolveComponentIdAsync().ConfigureAwait(false) ?? throw NotFound();

        var wrapClass = await SafeAttributeAsync(Page.Locator(ClassicIds.Selector(id + "-wrap")), "class").ConfigureAwait(false)
                        ?? string.Empty;
        if (wrapClass.Contains("base-edit-disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsFlagSet(await SafeAttributeAsync(Root, "readonly").ConfigureAwait(false))
            || await SafeIsDisabledAsync(Root).ConfigureAwait(false))
        {
            return true;
        }

        return IsTrue(await EvaluateOnComponentAsync(id, "c => c.enabled === false", null).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads the bound column from the page's view model, through the control's <c>bindings</c>.
    /// Dates come back as local wall-clock ISO text, built in the browser so that the user's time
    /// zone is applied once, there, and never again here.
    /// </summary>
    private const string ReadModelScript = @"c => {
        const b = c.bindings || {};
        const pick = k => b[k] && b[k].modelItem;
        const col = pick('value') || pick('checked') || pick('dateValue') || null;
        const m = c.model;
        let v = (m && col && typeof m.get === 'function') ? m.get(col) : (c.checked !== undefined ? c.checked : c.value);
        const pad = n => String(n).padStart(2, '0');
        if (v instanceof Date) {
            return { kind: 'date', text: v.getFullYear() + '-' + pad(v.getMonth() + 1) + '-' + pad(v.getDate()) +
                'T' + pad(v.getHours()) + ':' + pad(v.getMinutes()) + ':' + pad(v.getSeconds()) };
        }
        if (v !== null && typeof v === 'object') {
            return { kind: 'lookup', text: v.displayValue == null ? '' : String(v.displayValue), id: v.value == null ? null : String(v.value) };
        }
        if (typeof v === 'number') { return { kind: 'number', text: String(v) }; }
        if (typeof v === 'boolean') { return { kind: 'boolean', text: v ? 'true' : 'false' }; }
        return { kind: 'text', text: v == null ? '' : String(v) };
    }";

    /// <summary>Reads the view-model value behind the control.</summary>
    protected async Task<ModelValue> ReadModelValueAsync()
    {
        var result = await EvaluateOnComponentAsync(ReadModelScript).ConfigureAwait(false);
        if (IsNull(result) || result.ValueKind != JsonValueKind.Object)
        {
            return new ModelValue("text", string.Empty, null);
        }

        var kind = result.TryGetProperty("kind", out var k) ? k.GetString() ?? "text" : "text";
        var text = result.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var id = result.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        return new ModelValue(kind, text, id);
    }

    /// <summary>Clicks into the input, replaces its text and tabs out so Classic commits the change.</summary>
    protected async Task FillAndCommitAsync(ILocator input, string text)
    {
        await input.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await input.FillAsync(text ?? string.Empty).ConfigureAwait(false);
        await input.PressAsync("Tab").ConfigureAwait(false);
    }

    /// <summary>Invariant text of a number, the form the DSL compares.</summary>
    protected static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A view-model value as the browser reported it: its kind, its text and, for a lookup, the record id.</summary>
/// <param name="Kind"><c>text</c>, <c>number</c>, <c>boolean</c>, <c>date</c> (local ISO) or <c>lookup</c>.</param>
/// <param name="Text">The value as text: invariant number, <c>true</c>/<c>false</c>, ISO date, display value.</param>
/// <param name="Id">The referenced record id of a lookup, else null.</param>
public sealed record ModelValue(string Kind, string Text, string? Id)
{
    /// <summary>True when the model holds nothing: empty text, no lookup, no date.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);
}
