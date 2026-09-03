namespace CreatioAutoTests.Execution;

/// <summary>
/// One argument of a DSL command. Whether it was quoted is recorded at tokenizing time so no
/// validator has to inspect the raw text again to tell a literal from an element reference.
/// </summary>
public sealed record CommandToken(string Value, bool IsQuoted)
{
    /// <summary>Formats the token while preserving its literal quotation when present.</summary>
    public override string ToString() => IsQuoted ? "\"" + Value + "\"" : Value;
}

/// <summary>A single non-empty, non-comment line of an .act file after tokenizing.</summary>
public sealed record ParsedCommand(
    int LineNumber,
    string RawText,
    string Keyword,
    IReadOnlyList<CommandToken> Arguments)
{
    /// <summary>Number of command arguments after its keyword.</summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>Returns an argument token by position, or null when the position is absent.</summary>
    public CommandToken? ArgumentAt(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;

    /// <summary>Returns an argument value by position, discarding its quotation metadata.</summary>
    public string? ValueAt(int index) => ArgumentAt(index)?.Value;

    /// <summary>Formats the original DSL source line with its line number.</summary>
    public override string ToString() => "line " + LineNumber + ": " + RawText;
}
