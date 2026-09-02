namespace CreatioAutoTests.Configuration;

/// <summary>
/// A Creatio business object used by the 'create' command:
/// section page -> New button -> form page -> fill fields -> Save.
/// </summary>
public sealed class ObjectDefinition
{
    public string Name { get; set; } = string.Empty;

    public string SectionPage { get; set; } = string.Empty;

    public string FormPage { get; set; } = string.Empty;

    public string NewButton { get; set; } = string.Empty;

    public string SaveButton { get; set; } = string.Empty;

    /// <summary>Element code on the form page -> static value name.</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public override string ToString() => Name;
}

/// <summary>Root of objects.config.json.</summary>
public sealed class ObjectsConfig
{
    public List<ObjectDefinition> Objects { get; set; } = new();
}
