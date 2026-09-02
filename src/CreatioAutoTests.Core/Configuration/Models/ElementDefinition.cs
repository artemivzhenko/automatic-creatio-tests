using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreatioAutoTests.Configuration;

/// <summary>
/// One UI element declared in elements.config.json.
/// Known keys are Code/Page/Label/Type; anything else is captured into <see cref="Extra"/>
/// and bound to the element type's options class on demand.
/// </summary>
public sealed class ElementDefinition
{
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Page this element belongs to. Filled in by the loader from the file the element lives in,
    /// so it is never written into a page file: repeating it there would be a second source of truth.
    /// </summary>
    [JsonIgnore]
    public string Page { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Marks the entry as decided by a person, so analysis leaves it alone.
    ///
    /// Some things cannot be read from a page: an empty date control is identical whether it holds
    /// a date, a time or both, and a Save button's behaviour is not in the DOM at all. Without this
    /// flag the next analysis would quietly replace the answer with its own guess.
    /// </summary>
    public bool Pinned { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Identity of an element inside the catalog: page plus code.</summary>
    [JsonIgnore]
    public ElementKey Key => new(Page, Code);

    /// <summary>
    /// Binds the extra JSON members onto <typeparamref name="TOptions"/>.
    /// Returns a default instance when no extra members are present.
    /// </summary>
    public TOptions BindOptions<TOptions>(JsonSerializerOptions? serializerOptions = null)
        where TOptions : new()
    {
        if (Extra is null || Extra.Count == 0)
        {
            return new TOptions();
        }

        var buffer = new Dictionary<string, JsonElement>(Extra, StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(buffer);
        return JsonSerializer.Deserialize<TOptions>(json, serializerOptions ?? DefaultOptions) ?? new TOptions();
    }

    internal static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public override string ToString() => $"{Type} '{Code}' on page '{Page}'";
}

/// <summary>Composite key that makes an element unique: the same code may live on several pages.</summary>
public readonly record struct ElementKey(string Page, string Code)
{
    public bool Equals(ElementKey other) =>
        string.Equals(Page, other.Page, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Code, other.Code, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(
        Page.ToLowerInvariant(),
        Code);

    public override string ToString() => $"{Page}/{Code}";
}
