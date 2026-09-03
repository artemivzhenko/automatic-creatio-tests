namespace CreatioAutoTests.Commands;

/// <summary>
/// Marks a class as a DSL command and binds it to its keyword. Discovered by
/// <see cref="CommandRegistry"/> through reflection, so adding a command is adding one class:
/// no switch, no enum, no factory to touch.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute : Attribute
{
    /// <summary>Associates a command implementation with its DSL keyword.</summary>
    public CommandAttribute(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Command keyword must be provided.", nameof(keyword));
        }

        Keyword = keyword.Trim();
    }

    /// <summary>First word of the command in an .act file, matched case-insensitively.</summary>
    public string Keyword { get; }

    /// <summary>Validator that checks this command's shape and resolves its targets.</summary>
    public Type? Validator { get; set; }

    /// <summary>
    /// Element classes or capability interfaces this command can act on. Naming a capability keeps
    /// the list open: a new element type that implements it becomes usable without touching the
    /// command. An empty list means the command addresses no element.
    /// </summary>
    public Type[] SupportedTargets { get; set; } = Array.Empty<Type>();

    /// <summary>One-line syntax reminder, surfaced in CLI help and error messages.</summary>
    public string? Usage { get; set; }
}
