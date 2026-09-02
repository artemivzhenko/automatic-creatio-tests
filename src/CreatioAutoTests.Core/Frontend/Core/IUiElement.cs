namespace CreatioAutoTests.Frontend;

/// <summary>Anything the DSL can address by code: a field, a button, a stage bar, a tab.</summary>
public interface IUiElement
{
    /// <summary>Value of the element-name attribute on the Freedom UI host tag.</summary>
    string Code { get; }

    /// <summary>Caption declared in elements.config.json.</summary>
    string Label { get; }
}
