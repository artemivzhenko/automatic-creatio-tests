using CreatioAutoTests.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend;

/// <summary>Everything a live UI element needs: the open page, its definition and run settings.</summary>
public sealed class ElementRuntimeContext
{
    public ElementRuntimeContext(
        IPage page,
        ElementDefinition definition,
        RunSettings settings,
        ILogger logger)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IPage Page { get; }

    public ElementDefinition Definition { get; }

    public RunSettings Settings { get; }

    public ILogger Logger { get; }
}
