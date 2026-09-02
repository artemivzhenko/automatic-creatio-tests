using System.Reflection;
using CreatioAutoTests.Validation;

namespace CreatioAutoTests.Commands;

/// <summary>One command implementation discovered by reflection.</summary>
public sealed record CommandRegistration(
    string Keyword,
    Type ImplementationType,
    Type ValidatorType,
    IReadOnlyList<Type> SupportedTargets,
    string? Usage)
{
    /// <summary>True when this command addresses no element, such as 'wait' or 'end'.</summary>
    public bool IsTargetless => SupportedTargets.Count == 0;

    /// <summary>Checks a resolved element against what this command declared it can drive.</summary>
    public bool Accepts(Type elementType) =>
        SupportedTargets.Count == 0 || SupportedTargets.Any(t => t.IsAssignableFrom(elementType));

    public override string ToString() => Keyword + " -> " + ImplementationType.Name;
}

/// <summary>
/// Maps a DSL keyword onto the class that implements it, discovered by scanning for
/// <see cref="CommandAttribute"/>.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandRegistration> _byKeyword;

    public CommandRegistry(IEnumerable<CommandRegistration> registrations)
    {
        _byKeyword = new Dictionary<string, CommandRegistration>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (_byKeyword.TryGetValue(registration.Keyword, out var existing))
            {
                throw new InvalidOperationException(
                    "Keyword '" + registration.Keyword + "' is claimed by both " +
                    existing.ImplementationType.FullName + " and " +
                    registration.ImplementationType.FullName + ".");
            }

            _byKeyword.Add(registration.Keyword, registration);
        }
    }

    public IReadOnlyCollection<string> Keywords =>
        _byKeyword.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyCollection<CommandRegistration> Registrations => _byKeyword.Values.ToArray();

    public bool TryResolve(string keyword, out CommandRegistration registration) =>
        _byKeyword.TryGetValue(keyword ?? string.Empty, out registration!);

    /// <summary>Scans the given assemblies, defaulting to the assembly that holds the framework.</summary>
    public static CommandRegistry Discover(params Assembly[] assemblies)
    {
        var scanned = assemblies.Length > 0 ? assemblies : new[] { typeof(CommandRegistry).Assembly };

        var registrations = scanned
            .SelectMany(SafeGetTypes)
            .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<CommandAttribute>() })
            .Where(x => x.Attribute is not null)
            .Select(x => CreateRegistration(x.Type, x.Attribute!))
            .ToArray();

        return new CommandRegistry(registrations);
    }

    private static CommandRegistration CreateRegistration(Type type, CommandAttribute attribute)
    {
        if (type.IsAbstract || !typeof(ICommand).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                type.FullName + " is marked with [Command] but is abstract or does not implement ICommand.");
        }

        if (attribute.Validator is null)
        {
            throw new InvalidOperationException(
                type.FullName + " must name a Validator: every command is validated before it runs.");
        }

        if (!typeof(ICommandValidator).IsAssignableFrom(attribute.Validator))
        {
            throw new InvalidOperationException(
                attribute.Validator.FullName + " declared by " + type.FullName +
                " does not implement ICommandValidator.");
        }

        return new CommandRegistration(
            attribute.Keyword,
            type,
            attribute.Validator,
            attribute.SupportedTargets,
            attribute.Usage);
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
