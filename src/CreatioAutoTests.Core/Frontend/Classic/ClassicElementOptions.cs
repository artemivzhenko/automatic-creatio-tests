namespace CreatioAutoTests.Frontend.Classic;

/// <summary>
/// Extra members every Classic UI element may declare in its page file.
///
/// The DSL addresses an element by its <c>code</c>, which analysis sets to the bound column
/// (<c>QSTypeCargo</c>) because that is what a test author recognises. The schema, however, may
/// name the same element <c>LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4</c>, and that is what the
/// DOM id is built from. <see cref="Name"/> holds the schema name when it differs from the code.
/// </summary>
public class ClassicElementOptions
{
    /// <summary>Element name in the page schema's <c>diff</c>, when it differs from the code.</summary>
    public string? Name { get; set; }
}

/// <summary>Extra members of a Classic UI button.</summary>
public sealed class ClassicButtonOptions : ClassicElementOptions
{
    /// <summary>
    /// Schema name of the detail the button belongs to. A detail's buttons are prefixed by the
    /// detail schema (<c>QSSchema2d707869DetailAddRecordButtonButton-wrapperEl</c>), not by the page.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>Milliseconds to wait after a click so the page can react.</summary>
    public int SettleMs { get; set; } = 1000;

    /// <summary>
    /// Treat this button as Save: after the click, a message box is read as the reason the save
    /// failed, and the button has to leave the toolbar, which is how Classic shows a clean page.
    /// </summary>
    public bool IsSave { get; set; }

    /// <summary>Milliseconds allowed for a Save button to disappear after a successful save.</summary>
    public int DisappearTimeoutMs { get; set; } = 15000;
}

/// <summary>Extra members of a Classic UI tab.</summary>
public sealed class ClassicTabOptions : ClassicElementOptions
{
    /// <summary>Name of the tab panel in the schema. BasePageV2 calls it <c>Tabs</c>, and nearly every page inherits that.</summary>
    public string Panel { get; set; } = "Tabs";

    /// <summary>Milliseconds to wait after switching so the tab's content can render.</summary>
    public int SettleMs { get; set; } = 1000;
}

/// <summary>Extra members of a Classic UI tab panel, the element whose value is the open tab.</summary>
public sealed class ClassicTabPanelOptions : ClassicElementOptions
{
    /// <summary>Captions of the tabs the panel shows, in display order, as analysis found them.</summary>
    public string[] Tabs { get; set; } = Array.Empty<string>();

    /// <summary>Milliseconds to wait after switching so the tab's content can render.</summary>
    public int SettleMs { get; set; } = 1000;
}

/// <summary>Extra members of a Classic UI detail.</summary>
public sealed class ClassicDetailOptions : ClassicElementOptions
{
    /// <summary>Column captions expected in the detail's grid, left to right; asserted by <c>check X columns</c>.</summary>
    public string[] Columns { get; set; } = Array.Empty<string>();
}

/// <summary>Extra members of the Classic UI stage bar (the DCM bar of the actions dashboard).</summary>
public sealed class ClassicCasesBarOptions : ClassicElementOptions
{
    /// <summary>Stages expected on the bar, in display order.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();

    /// <summary>Stage caption to the sub-stages hidden behind its menu, when the page file knows them.</summary>
    public Dictionary<string, string[]>? Subvalues { get; set; }

    /// <summary>
    /// Component id of the stage control. The actions dashboard is one shared module, so the id is
    /// the same on every page; it is only here for a page that embeds the dashboard elsewhere.
    /// </summary>
    public string Container { get; set; } = "SectionActionsDashboardActionsControlContainer";

    /// <summary>Milliseconds to wait after a stage change for Classic to save and the dashboard to reload.</summary>
    public int SettleMs { get; set; } = 1500;
}
