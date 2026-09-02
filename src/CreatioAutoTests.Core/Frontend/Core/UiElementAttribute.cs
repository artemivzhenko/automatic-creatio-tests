namespace CreatioAutoTests.Frontend;

/// <summary>
/// Marks a class as a UI element implementation and binds it to the "type" value used in
/// elements.config.json. Discovered by <see cref="Registry.UiElementRegistry"/> through reflection:
/// adding a new element type means adding one class, nothing else.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UiElementAttribute : Attribute
{
    public UiElementAttribute(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Element type name must be provided.", nameof(typeName));
        }

        TypeName = typeName.Trim();
    }

    /// <summary>Value of the "type" property in elements.config.json, case-insensitive.</summary>
    public string TypeName { get; }

    /// <summary>
    /// Freedom UI custom tag that hosts this element, for example crt-input.
    /// Declared here as well as used at runtime so that page analysis can map a tag it finds in the
    /// DOM back to an element type without creating one.
    /// </summary>
    public string HostTag { get; set; } = string.Empty;

    /// <summary>
    /// Kind of value the element carries. Declared here rather than on the instance because
    /// <see cref="Validation.ValueCompatibilityPolicy"/> has to reason about it statically.
    /// </summary>
    public ValueKind ValueKind { get; set; } = ValueKind.None;

    /// <summary>
    /// Optional options class the element's extra JSON members are bound to.
    /// Declaring it lets the configuration loader reject unknown members at load time.
    /// </summary>
    public Type? Options { get; set; }
}
