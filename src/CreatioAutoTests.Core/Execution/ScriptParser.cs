using System.Text;

namespace CreatioAutoTests.Execution;

/// <summary>
/// Turns an .act file into tokens. Only lexical concerns live here; whether a command makes sense
/// is decided by the validation layer.
/// </summary>
public sealed class ScriptParser
{
    /// <summary>First character of a whole-line DSL comment.</summary>
    public const char CommentPrefix = '#';

    /// <summary>Parses every meaningful line of a script, preserving original line numbers.</summary>
    public IReadOnlyList<ParsedCommand> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new List<ParsedCommand>();
        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == CommentPrefix)
            {
                continue;
            }

            var tokens = Tokenize(trimmed);
            if (tokens.Count == 0)
            {
                continue;
            }

            var keyword = tokens[0].Value;
            var arguments = tokens.Skip(1).ToArray();

            result.Add(new ParsedCommand(lineNumber, trimmed, keyword, arguments));
        }

        return result;
    }

    /// <summary>Reads and tokenizes every meaningful line in a script file.</summary>
    public IReadOnlyList<ParsedCommand> ParseFile(string path) => Parse(File.ReadAllLines(path));

    /// <summary>
    /// Splits on whitespace outside double quotes. A quoted run becomes one token and remembers
    /// that it was quoted, which is how 'set X "Y"' is told apart from 'set X Y'.
    /// </summary>
    public static IReadOnlyList<CommandToken> Tokenize(string text)
    {
        var tokens = new List<CommandToken>();
        var buffer = new StringBuilder();
        var inQuotes = false;
        var quotedToken = false;

        void Flush()
        {
            if (buffer.Length > 0 || quotedToken)
            {
                tokens.Add(new CommandToken(buffer.ToString(), quotedToken));
                buffer.Clear();
            }

            quotedToken = false;
        }

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                quotedToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                Flush();
                continue;
            }

            buffer.Append(c);
        }

        Flush();
        return tokens;
    }
}
