using System.Reflection;

namespace CreatioAutoTests.Frontend.Registry;

/// <summary>One element implementation discovered by reflection.</summary>
public sealed record UiElementRegistration(
    string TypeName,
    Type ImplementationType,
    ValueKind ValueKind,
    string HostTag,
    Type? OptionsType,
    bool LabeledByPanel = false,
    UiGeneration Generation = UiGeneration.FreedomUI,
    IReadOnlyList<string>? Controls = null)
{
    /// <summary>Classic UI control class names the type covers; empty for a Freedom UI element.</summary>
    public IReadOnlyList<string> Controls { get; } = Controls ?? Array.Empty<string>();

    /// <summary>Capability interfaces the implementation offers, used by command validation.</summary>
    public IReadOnlyList<Type> Capabilities { get; } = ImplementationType
        .GetInterfaces()
        .Where(i => typeof(IUiElement).IsAssignableFrom(i))
        .ToArray();

    /// <summary>Formats the configured type name and its implementation for diagnostics.</summary>
    public override string ToString() => Generation.Name() + " " + TypeName + " -> " + ImplementationType.Name;
}

/// <summary>
/// Maps the "type" value from a page file onto the class that implements it, within the page's UI
/// generation. Populated by scanning assemblies for <see cref="UiElementAttribute"/>, so a new
/// element type is one new class and nothing else.
///
/// A type name is unique inside a generation and may be reused across generations: that is what
/// lets a Classic UI page file say <c>"type": "Lookup"</c> exactly like a Freedom UI one does.
/// </summary>
public sealed class UiElementRegistry
{
    private readonly Dictionary<UiGeneration, Dictionary<string, UiElementRegistration>> _byGeneration;

    /// <summary>Builds a case-insensitive type map per generation and rejects duplicate names within one.</summary>
    public UiElementRegistry(IEnumerable<UiElementRegistration> registrations)
    {
        _byGeneration = new Dictionary<UiGeneration, Dictionary<string, UiElementRegistration>>();
        foreach (var generation in Enum.GetValues<UiGeneration>())
        {
            _byGeneration[generation] = new Dictionary<string, UiElementRegistration>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var registration in registrations)
        {
            var map = _byGeneration[registration.Generation];
            if (map.TryGetValue(registration.TypeName, out var existing))
            {
                throw new InvalidOperationException(
                    registration.Generation.Name() + " element type '" + registration.TypeName + "' is claimed by both " +
                    existing.ImplementationType.FullName + " and " +
                    registration.ImplementationType.FullName + ".");
            }

            map.Add(registration.TypeName, registration);
        }
    }

    /// <summary>Every Freedom UI type name, ordered for stable error messages.</summary>
    public IReadOnlyCollection<string> KnownTypeNames => KnownTypeNamesOf(UiGeneration.FreedomUI);

    /// <summary>Every type name of a generation, ordered for stable error messages.</summary>
    public IReadOnlyCollection<string> KnownTypeNamesOf(UiGeneration generation) =>
        _byGeneration[generation].Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Every discovered registration of every generation.</summary>
    public IReadOnlyCollection<UiElementRegistration> Registrations =>
        _byGeneration.Values.SelectMany(m => m.Values).ToArray();

    /// <summary>The registrations of one generation, which is what analysis and the reverse indexes walk.</summary>
    public IReadOnlyCollection<UiElementRegistration> RegistrationsOf(UiGeneration generation) =>
        _byGeneration[generation].Values.ToArray();

    /// <summary>
    /// Looks up a Freedom UI type name without treating an unknown type as an error. Callers that
    /// stand on a page use the overload taking the page's generation.
    /// </summary>
    public bool TryResolve(string typeName, out UiElementRegistration registration) =>
        TryResolve(UiGeneration.FreedomUI, typeName, out registration);

    /// <summary>Looks up a type name within a generation without treating an unknown type as an error.</summary>
    public bool TryResolve(UiGeneration generation, string typeName, out UiElementRegistration registration) =>
        _byGeneration[generation].TryGetValue(typeName ?? string.Empty, out registration!);

    /// <summary>Resolves a Freedom UI type name or reports all known names to aid correction.</summary>
    public UiElementRegistration Resolve(string typeName) => Resolve(UiGeneration.FreedomUI, typeName);

    /// <summary>Resolves a type name within a generation or reports the names that generation does have.</summary>
    public UiElementRegistration Resolve(UiGeneration generation, string typeName)
    {
        if (TryResolve(generation, typeName, out var registration))
        {
            return registration;
        }

        throw new InvalidOperationException(DescribeUnknown(generation, typeName));
    }

    /// <summary>
    /// The message for a type name a generation does not have. Says so explicitly when the name does
    /// exist in the other generation, because that is the mistake a page file is most likely to make.
    /// </summary>
    public string DescribeUnknown(UiGeneration generation, string typeName)
    {
        var other = generation == UiGeneration.FreedomUI ? UiGeneration.ClassicUI : UiGeneration.FreedomUI;
        var hint = TryResolve(other, typeName, out _)
            ? " It exists for " + other.Name() + " pages only."
            : string.Empty;

        return "Unknown element type '" + typeName + "' for a " + generation.Name() + " page." + hint +
               " Known types for " + generation.Name() + " pages: " + string.Join(", ", KnownTypeNamesOf(generation)) + ".";
    }

    /// <summary>Scans the given assemblies, defaulting to the assembly that holds the framework.</summary>
    public static UiElementRegistry Discover(params Assembly[] assemblies)
    {
        var scanned = assemblies.Length > 0
            ? assemblies
            : new[] { typeof(UiElementRegistry).Assembly };

        var registrations = scanned
            .SelectMany(SafeGetTypes)
            .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<UiElementAttribute>() })
            .Where(x => x.Attribute is not null)
            .Select(x => CreateRegistration(x.Type, x.Attribute!))
            .ToArray();

        return new UiElementRegistry(registrations);
    }

    private static UiElementRegistration CreateRegistration(Type type, UiElementAttribute attribute)
    {
        if (type.IsAbstract || !typeof(IUiElement).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                type.FullName + " is marked with [UiElement] but is abstract or does not implement IUiElement.");
        }

        if (type.GetConstructor(new[] { typeof(ElementRuntimeContext) }) is null)
        {
            throw new InvalidOperationException(
                type.FullName + " must expose a public constructor taking a single ElementRuntimeContext.");
        }

        return new UiElementRegistration(
            attribute.TypeName,
            type,
            attribute.ValueKind,
            attribute.HostTag,
            attribute.Options,
            attribute.LabeledByPanel,
            attribute.Generation,
            attribute.Controls);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
