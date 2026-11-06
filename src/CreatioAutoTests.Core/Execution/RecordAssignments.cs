namespace CreatioAutoTests.Execution;

/// <summary>One column of a new record and the value to put in it.</summary>
/// <param name="Column">Column caption as it is rendered in the grid header.</param>
/// <param name="Value">Text to enter, in the same form the matching field type accepts.</param>
public readonly record struct RecordAssignment(string Column, string Value)
{
    /// <summary>Formats the pair the way it is written in a script.</summary>
    public override string ToString() => Column + "=" + Value;
}

/// <summary>
/// Reads the <c>Column=Value;Column=Value</c> list that 'add-record' takes.
///
/// Lexical work, so it lives beside the parser rather than in an element: the validation pass has to
/// reject a malformed list without a browser, and the command has to read the same list back the
/// same way.
/// </summary>
public static class RecordAssignments
{
    /// <summary>Separates one assignment from the next.</summary>
    public const char PairSeparator = ';';

    /// <summary>Separates a column name from its value.</summary>
    public const char ValueSeparator = '=';

    /// <summary>
    /// Parses an assignment list.
    ///
    /// Only the first <c>=</c> splits a pair, so a value may contain one. Whitespace around names and
    /// values is dropped: a script is written for a person to read, and 'Name = Jon' means what it
    /// looks like.
    /// </summary>
    /// <param name="text">The raw list, without the surrounding command.</param>
    /// <param name="assignments">Pairs in the order they were written.</param>
    /// <param name="error">Why the list could not be read, when it could not.</param>
    /// <returns><see langword="true"/> when the whole list parsed.</returns>
    public static bool TryParse(
        string? text,
        out IReadOnlyList<RecordAssignment> assignments,
        out string error)
    {
        assignments = Array.Empty<RecordAssignment>();

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "no column assignments were given";
            return false;
        }

        var result = new List<RecordAssignment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in text.Split(PairSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf(ValueSeparator);
            if (separator < 0)
            {
                error = "'" + pair + "' is not a 'Column=Value' pair";
                return false;
            }

            var column = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();

            if (column.Length == 0)
            {
                error = "'" + pair + "' names no column";
                return false;
            }

            if (!seen.Add(column))
            {
                error = "column '" + column + "' is assigned more than once";
                return false;
            }

            result.Add(new RecordAssignment(column, value));
        }

        if (result.Count == 0)
        {
            error = "no column assignments were given";
            return false;
        }

        assignments = result;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Rebuilds the list from the tokens that followed 'set'.
    ///
    /// The tokenizer splits on whitespace, so a column name or value containing a space arrives as
    /// several tokens. Joining them back means a script can be written either way -- quoted as one
    /// argument, or plainly as it reads.
    /// </summary>
    /// <param name="tokens">Every token after the 'set' keyword.</param>
    public static string Rejoin(IEnumerable<CommandToken> tokens) =>
        string.Join(' ', tokens.Select(t => t.Value));
}
