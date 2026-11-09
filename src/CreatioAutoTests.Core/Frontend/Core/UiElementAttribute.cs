namespace CreatioAutoTests.Frontend;

/// <summary>
/// Marks a class as a UI element implementation and binds it to the "type" value used in a page
/// file. Discovered by <see cref="Registry.UiElementRegistry"/> through reflection: adding a new
/// element type means adding one class, nothing else.
///
/// The same type name may exist once per <see cref="UiGeneration"/>: a Freedom UI <c>Text</c> and a
/// Classic UI <c>Text</c> are two classes, and the page's own generation decides which one a page
/// file refers to. A test author therefore writes the same type names whatever the page is built with.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UiElementAttribute : Attribute
{
    /// <summary>Associates an implementation with the configuration type name.</summary>
    public UiElementAttribute(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Element type name must be provided.", nameof(typeName));
        }

        TypeName = typeName.Trim();
    }

    /// <summary>Value of the "type" property in a page file, case-insensitive.</summary>
    public string TypeName { get; }

    /// <summary>
    /// UI generation the element is built for. Defaults to Freedom UI, which is what every element
    /// type was until Classic UI support was added, so existing registrations need no change.
    /// </summary>
    public UiGeneration Generation { get; set; } = UiGeneration.FreedomUI;

    /// <summary>
    /// Freedom UI custom tag that hosts this element, for example crt-input.
    /// Declared here as well as used at runtime so that page analysis can map a tag it finds in the
    /// DOM back to an element type without creating one. Classic UI elements leave it empty.
    /// </summary>
    public string HostTag { get; set; } = string.Empty;

    /// <summary>
    /// Classic UI control class names this element covers, without the <c>Terrasoft.controls.</c>
    /// prefix: <c>TextEdit</c>, <c>LookupEdit</c>, <c>ComboBoxEdit</c>. The control name is the last
    /// part of the DOM id (<c>{Schema}{Name}{Control}-el</c>), so this is to Classic UI what
    /// <see cref="HostTag"/> is to Freedom UI: it locates the element and lets analysis name its type.
    /// Freedom UI elements leave it empty.
    /// </summary>
    public string[] Controls { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Kind of value the element carries. Declared here rather than on the instance because
    /// <see cref="Validation.ValueCompatibilityPolicy"/> has to reason about it statically.
    /// </summary>
    public ValueKind ValueKind { get; set; } = ValueKind.None;

    /// <summary>
    /// Declares that the element has no caption of its own and is named by the expansion panel it
    /// sits in.
    ///
    /// True for a grid, false for everything else -- including the buttons in a grid's own toolbar,
    /// which sit in the same panel and would otherwise all be recorded under the panel's caption.
    /// Declared here so page analysis stays a generic walk over the registry.
    /// </summary>
    public bool LabeledByPanel { get; set; }

    /// <summary>
    /// Optional options class the element's extra JSON members are bound to.
    /// Declaring it lets the configuration loader reject unknown members at load time.
    /// </summary>
    public Type? Options { get; set; }
}
