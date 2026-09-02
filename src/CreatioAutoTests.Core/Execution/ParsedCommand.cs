namespace CreatioAutoTests.Execution;

/// <summary>
/// One argument of a DSL command. Whether it was quoted is recorded at tokenizing time so no
/// validator has to inspect the raw text again to tell a literal from an element reference.
/// </summary>
public sealed record CommandToken(string Value, bool IsQuoted)
{
    public override string ToString() => IsQuoted ? "\"" + Value + "\"" : Value;
}

/// <summary>A single non-empty, non-comment line of an .act file after tokenizing.</summary>
public sealed record ParsedCommand(
    int LineNumber,
    string RawText,
    string Keyword,
    IReadOnlyList<CommandToken> Arguments)
{
    public int ArgumentCount => Arguments.Count;

    public CommandToken? ArgumentAt(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;

    public string? ValueAt(int index) => ArgumentAt(index)?.Value;

    public override string ToString() => "line " + LineNumber + ": " + RawText;
}
