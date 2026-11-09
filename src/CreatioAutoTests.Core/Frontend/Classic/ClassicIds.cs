namespace CreatioAutoTests.Frontend.Classic;

/// <summary>
/// How Classic UI names things in the DOM. Every id is built from the page schema name, the
/// element's name in the schema's <c>diff</c>, and the control that renders it:
/// <c>ContactPageV2NameTextEdit-el</c> is the input, <c>-wrap</c> its box,
/// <c>ContactPageV2NameLabel</c> its caption. Pure functions, so the rule is testable on its own.
/// </summary>
public static class ClassicIds
{
    /// <summary>Id of the Ext component: what <c>Ext.getCmp</c> takes.</summary>
    public static string Component(string schema, string name, string control) => schema + name + control;

    /// <summary>Id of the DOM node that carries the value: the input, textarea or checkbox.</summary>
    public static string Element(string schema, string name, string control) => Component(schema, name, control) + "-el";

    /// <summary>Id of the box around the control; it carries the disabled marker.</summary>
    public static string Wrap(string schema, string name, string control) => Component(schema, name, control) + "-wrap";

    /// <summary>Id of the caption; it carries the required marker.</summary>
    public static string Label(string schema, string name) => schema + name + "Label";

    /// <summary>Id of a button's text span. A text-only button has only this node.</summary>
    public static string ButtonText(string prefix, string name) => prefix + name + "Button-textEl";

    /// <summary>Id of a button's outer span, present when the button has an icon or a menu.</summary>
    public static string ButtonWrapper(string prefix, string name) => prefix + name + "Button-wrapperEl";

    /// <summary>Id of the container a detail renders into; the prefix is the detail schema, not the page.</summary>
    public static string DetailContainer(string detailSchema) => detailSchema + "Container";

    /// <summary>Id of a detail's caption node.</summary>
    public static string DetailCaption(string detailSchema) => detailSchema + "DetailControlGroup-caption";

    /// <summary>Id of the DOM wrapper of a detail's grid: captions, rows and the spinner space.</summary>
    public static string DetailGrid(string detailSchema) => "grid-" + detailSchema + "DataGridGrid-wrap";

    /// <summary>Id of a detail's grid component, which holds the loaded row collection.</summary>
    public static string DetailGridComponent(string detailSchema) => detailSchema + "DataGridGrid";

    /// <summary>Id of the tab panel component, <c>Tabs</c> being the name every page gets from BasePageV2.</summary>
    public static string TabPanel(string schema, string panel) => schema + panel + "TabPanel";

    /// <summary>Id of the list that holds one <c>li[data-item-index]</c> per visible tab.</summary>
    public static string TabItems(string schema, string panel) => TabPanel(schema, panel) + "-tabpanel-items";

    /// <summary>
    /// Attribute selector for an id. Classic ids contain GUIDs with hyphens and the occasional dot,
    /// which a <c>#id</c> selector would need escaping for; the attribute form never does.
    /// </summary>
    public static string Selector(string id) => "[id=\"" + id + "\"]";

    /// <summary>How an element is addressed, for messages: the id with the control alternatives braced.</summary>
    public static string Describe(string schema, string name, IReadOnlyList<string> controls)
    {
        var control = controls.Count == 1 ? controls[0] : "{" + string.Join("|", controls) + "}";
        return schema + name + control + "-el";
    }
}
