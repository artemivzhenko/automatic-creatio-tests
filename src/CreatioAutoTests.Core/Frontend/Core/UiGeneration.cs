namespace CreatioAutoTests.Frontend;

/// <summary>
/// The two generations of Creatio's user interface. They share nothing in the DOM: Freedom UI (8.x)
/// renders Angular components addressed by <c>element-name</c>, Classic UI (7.x) renders Ext JS
/// controls addressed by a predictable DOM id. An element type belongs to exactly one generation,
/// a page declares which generation it is, and the catalog only pairs the two when they agree.
/// </summary>
public enum UiGeneration
{
    /// <summary>Freedom UI, Creatio 8.x: <c>crt-*</c> host tags and <c>element-name</c> attributes.</summary>
    FreedomUI = 0,

    /// <summary>Classic UI, Creatio 7.x pages served inside the 8.x Shell: <c>{Schema}{Name}{Control}-el</c> ids.</summary>
    ClassicUI = 1
}

/// <summary>Names the generations the way page files spell them, and parses them back.</summary>
public static class UiGenerations
{
    /// <summary>The <c>type</c> value of a Freedom UI page file.</summary>
    public const string FreedomUIName = "FreedomUI";

    /// <summary>The <c>type</c> value of a Classic UI page file.</summary>
    public const string ClassicUIName = "ClassicUI";

    /// <summary>Every accepted page type name, for error messages.</summary>
    public static IReadOnlyList<string> KnownNames { get; } = new[] { FreedomUIName, ClassicUIName };

    /// <summary>Reads a page file's <c>type</c>. Case-insensitive; a missing value means Freedom UI.</summary>
    public static bool TryParse(string? text, out UiGeneration generation)
    {
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), FreedomUIName, StringComparison.OrdinalIgnoreCase))
        {
            generation = UiGeneration.FreedomUI;
            return true;
        }

        if (string.Equals(text.Trim(), ClassicUIName, StringComparison.OrdinalIgnoreCase))
        {
            generation = UiGeneration.ClassicUI;
            return true;
        }

        generation = default;
        return false;
    }

    /// <summary>The page-file spelling of a generation.</summary>
    public static string Name(this UiGeneration generation) =>
        generation == UiGeneration.ClassicUI ? ClassicUIName : FreedomUIName;
}
