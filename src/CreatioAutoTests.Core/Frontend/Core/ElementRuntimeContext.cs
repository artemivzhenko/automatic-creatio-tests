using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>Everything a live UI element needs: the open page, its definition, the page it sits on and run settings.</summary>
public sealed class ElementRuntimeContext
{
    /// <summary>Bundles the page-bound dependencies supplied when an element is materialized.</summary>
    public ElementRuntimeContext(
        IPage page,
        ElementDefinition definition,
        RunSettings settings,
        ILogger logger,
        PageDefinition? pageDefinition = null)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        PageDefinition = pageDefinition;
    }

    /// <summary>Playwright page on which the configured control is currently rendered.</summary>
    public IPage Page { get; }

    /// <summary>Static configuration that identifies and labels the control.</summary>
    public ElementDefinition Definition { get; }

    /// <summary>
    /// The configured page the element belongs to, when the caller knows it. A Classic UI element
    /// needs it for the page schema name that prefixes every control id; a Freedom UI element never
    /// looks at it.
    /// </summary>
    public PageDefinition? PageDefinition { get; }

    /// <summary>Timeout and run settings shared by all elements in the case.</summary>
    public RunSettings Settings { get; }

    /// <summary>Logger for element-level diagnostics.</summary>
    public ILogger Logger { get; }
}
