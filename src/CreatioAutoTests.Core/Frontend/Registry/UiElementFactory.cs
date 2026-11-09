using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Registry;

/// <summary>Builds live element instances for an open page from their configuration entries.</summary>
public sealed class UiElementFactory
{
    private readonly UiElementRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes the factory with the discovered types and their diagnostic logger source.</summary>
    public UiElementFactory(UiElementRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>Creates a Freedom UI element against an already open page, for callers without a page definition.</summary>
    public IUiElement Create(ElementDefinition definition, IPage page, RunSettings settings) =>
        Create(definition, null, page, settings);

    /// <summary>
    /// Creates the element described by <paramref name="definition"/> against an already open page.
    /// The concrete class comes from the registry of the page's generation, which is what the
    /// validator resolved earlier; without a page definition the element is taken to be Freedom UI.
    /// </summary>
    public IUiElement Create(ElementDefinition definition, PageDefinition? pageDefinition, IPage page, RunSettings settings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(settings);

        var generation = pageDefinition?.Generation ?? UiGeneration.FreedomUI;
        var registration = _registry.Resolve(generation, definition.Type);
        var logger = _loggerFactory.CreateLogger(registration.ImplementationType);
        var context = new ElementRuntimeContext(page, definition, settings, logger, pageDefinition);

        try
        {
            return (IUiElement)Activator.CreateInstance(registration.ImplementationType, context)!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException(
                "Creating element '" + definition.Code + "' of type '" + definition.Type + "' failed: " +
                ex.InnerException.Message,
                ex.InnerException);
        }
    }
}
