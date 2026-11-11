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

    /// <summary>Combines a validated execution step with its page-bound elements and session services.</summary>
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

    /// <summary>Validated plan step currently being executed.</summary>
    public ExecutionStep Step { get; }

    /// <summary>Parsed DSL command belonging to the validated step.</summary>
    public ParsedCommand Command => Step.Command;

    /// <summary>Live session used by commands that navigate, authenticate, or materialize elements.</summary>
    public IExecutionSession Session { get; }

    /// <summary>Structured command-level diagnostic logger.</summary>
    public ILogger Logger { get; }

    /// <summary>Live elements, in the order the validator resolved them.</summary>
    public IReadOnlyList<IUiElement> Targets => _elements;

    /// <summary>Static side of the same targets: definition, implementation type and value kind.</summary>
    public IReadOnlyList<ResolvedTarget> Resolved => Step.Targets;

    /// <summary>Returns whether validation produced a live target at the requested position.</summary>
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

        var value = Substitute(token.Value);
        return Session.StaticValues.TryGetValue(value, out var staticValue) ? staticValue : value;
    }

    /// <summary>Replaces the case's <c>$name</c> references in an unquoted argument with their values.</summary>
    public string Substitute(string text) =>
        CaseVariables.Substitute(text, Session.Variables);

    /// <summary>
    /// Rejoins a <c>Column=Value;...</c> list after substituting <c>$name</c> in its unquoted tokens, and parses it.
    /// Validation accepted the shape, so a parse error here is a substituted value that broke it.
    /// </summary>
    public IReadOnlyList<RecordAssignment> Assignments(IEnumerable<CommandToken> tokens)
    {
        var text = RecordAssignments.Rejoin(tokens.Select(t => t.IsQuoted ? t : t with { Value = Substitute(t.Value) }));
        if (!RecordAssignments.TryParse(text, out var assignments, out var error))
        {
            throw new CommandFailedException("'" + Command.RawText + "' cannot be read once its values are filled in: " + error + ".");
        }

        return assignments;
    }
}
