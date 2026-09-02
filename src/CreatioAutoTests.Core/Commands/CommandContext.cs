using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// What a command sees at execution time: the validated step it belongs to, its targets brought to
/// life against the open page, and the session it can drive.
/// </summary>
public sealed class CommandContext
{
    private readonly IReadOnlyList<IUiElement> _elements;

    public CommandContext(
        ExecutionStep step,
        IReadOnlyList<IUiElement> elements,
        IExecutionSession session,
        ILogger logger)
    {
        Step = step ?? throw new ArgumentNullException(nameof(step));
        _elements = elements ?? throw new ArgumentNullException(nameof(elements));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionStep Step { get; }

    public ParsedCommand Command => Step.Command;

    public IExecutionSession Session { get; }

    public ILogger Logger { get; }

    /// <summary>Live elements, in the order the validator resolved them.</summary>
    public IReadOnlyList<IUiElement> Targets => _elements;

    /// <summary>Static side of the same targets: definition, implementation type and value kind.</summary>
    public IReadOnlyList<ResolvedTarget> Resolved => Step.Targets;

    public bool HasTarget(int index) => index >= 0 && index < _elements.Count;

    /// <summary>Value kind the validator determined for a target, without touching the browser.</summary>
    public ValueKind KindOf(int index = 0) =>
        index >= 0 && index < Step.Targets.Count ? Step.Targets[index].Entry.ValueKind : ValueKind.None;

    /// <summary>
    /// The target at <paramref name="index"/> as the capability the command needs.
    /// Validation guarantees the cast, so a failure here is a framework bug, not a test error.
    /// </summary>
    public T Target<T>(int index = 0) where T : class
    {
        if (!HasTarget(index))
        {
            throw new InvalidOperationException(
                "Command '" + Command.Keyword + "' expected a target at position " + index +
                " but validation produced " + _elements.Count + ".");
        }

        if (_elements[index] is not T typed)
        {
            throw new InvalidOperationException(
                "Target '" + _elements[index].Code + "' is a " + _elements[index].GetType().Name +
                " which does not provide " + typeof(T).Name + ".");
        }

        return typed;
    }

    /// <summary>Reads an argument as a literal, expanding an unquoted static value name.</summary>
    public string ResolveValue(int argumentIndex)
    {
        var token = Command.ArgumentAt(argumentIndex)
                    ?? throw new InvalidOperationException(
                        "Command '" + Command.Keyword + "' has no argument at position " + argumentIndex + ".");

        if (token.IsQuoted)
        {
            return token.Value;
        }

        return Session.StaticValues.TryGetValue(token.Value, out var staticValue) ? staticValue : token.Value;
    }
}
