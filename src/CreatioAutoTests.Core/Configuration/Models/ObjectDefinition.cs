namespace CreatioAutoTests.Configuration;

/// <summary>
/// A Creatio business object used by the 'create' command:
/// section page -> New button -> form page -> fill fields -> Save.
/// </summary>
public sealed class ObjectDefinition
{
    /// <summary>Name supplied to the DSL <c>create</c> command.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Configured section page where the new-record action starts.</summary>
    public string SectionPage { get; set; } = string.Empty;

    /// <summary>Configured form page reached after creating the record.</summary>
    public string FormPage { get; set; } = string.Empty;

    /// <summary>Clickable element code that creates a new record from the section page.</summary>
    public string NewButton { get; set; } = string.Empty;

    /// <summary>Clickable element code that saves the populated record on the form page.</summary>
    public string SaveButton { get; set; } = string.Empty;

    /// <summary>Element code on the form page -> static value name.</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Formats the object by its DSL creation name.</summary>
    public override string ToString() => Name;
}

/// <summary>Root of objects.config.json.</summary>
public sealed class ObjectsConfig
{
    /// <summary>All configured business-object workflows available to <c>create</c>.</summary>
    public List<ObjectDefinition> Objects { get; set; } = new();
}
