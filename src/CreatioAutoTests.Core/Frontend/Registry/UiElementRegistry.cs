using System.Reflection;

namespace CreatioAutoTests.Frontend.Registry;

/// <summary>One element implementation discovered by reflection.</summary>
public sealed record UiElementRegistration(
    string TypeName,
    Type ImplementationType,
    ValueKind ValueKind,
    string HostTag,
    Type? OptionsType,
    bool LabeledByPanel = false)
{
    /// <summary>Capability interfaces the implementation offers, used by command validation.</summary>
    public IReadOnlyList<Type> Capabilities { get; } = ImplementationType
        .GetInterfaces()
        .Where(i => typeof(IUiElement).IsAssignableFrom(i))
        .ToArray();

    /// <summary>Formats the configured type name and its implementation for diagnostics.</summary>
    public override string ToString() => TypeName + " -> " + ImplementationType.Name;
}

/// <summary>
/// Maps the "type" value from elements.config.json onto the class that implements it.
/// Populated by scanning assemblies for <see cref="UiElementAttribute"/>, so a new element type
/// is one new class and nothing else.
/// </summary>
public sealed class UiElementRegistry
{
    private readonly Dictionary<string, UiElementRegistration> _byTypeName;

    /// <summary>Builds a case-insensitive type map and rejects duplicate configuration type names.</summary>
    public UiElementRegistry(IEnumerable<UiElementRegistration> registrations)
    {
        _byTypeName = new Dictionary<string, UiElementRegistration>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (_byTypeName.TryGetValue(registration.TypeName, out var existing))
            {
                throw new InvalidOperationException(
                    "Element type '" + registration.TypeName + "' is claimed by both " +
                    existing.ImplementationType.FullName + " and " +
                    registration.ImplementationType.FullName + ".");
            }

            _byTypeName.Add(registration.TypeName, registration);
        }
    }

    /// <summary>Every registered type name, ordered for stable error messages.</summary>
    public IReadOnlyCollection<string> KnownTypeNames =>
        _byTypeName.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Every discovered registration used by factories and the reverse host-tag index.</summary>
    public IReadOnlyCollection<UiElementRegistration> Registrations => _byTypeName.Values.ToArray();

    /// <summary>Looks up a configuration type name without treating an unknown type as an error.</summary>
    public bool TryResolve(string typeName, out UiElementRegistration registration) =>
        _byTypeName.TryGetValue(typeName ?? string.Empty, out registration!);

    /// <summary>Resolves a configuration type name or reports all known names to aid correction.</summary>
    public UiElementRegistration Resolve(string typeName)
    {
        if (TryResolve(typeName, out var registration))
        {
            return registration;
        }

        throw new InvalidOperationException(
            "Unknown element type '" + typeName + "'. Known types: " + string.Join(", ", KnownTypeNames) + ".");
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
            attribute.LabeledByPanel);
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
