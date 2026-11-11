using System.Globalization;
using System.Text.RegularExpressions;

namespace CreatioAutoTests.Execution;

/// <summary>
/// The <c>$name</c> values of one case: an <c>insert ... as Order</c> alias and a <c>remember ... as $n</c>
/// share one table. Names are checked statically (a name used before the line that defines it is a
/// validation error); values exist only at run time.
/// </summary>
public static class CaseVariables
{
    /// <summary>A reference: <c>$name</c>, optionally <c>$name+N</c> / <c>$name-N</c> with an integer N.</summary>
    public static readonly Regex Reference = new(@"\$([A-Za-z_]\w*)(?:([+-])(\d+))?", RegexOptions.CultureInvariant);

    /// <summary>The names the text refers to, in order, each with whether it carries an offset.</summary>
    public static IEnumerable<(string Name, bool HasOffset)> References(string text) =>
        Reference.Matches(text).Select(m => (m.Groups[1].Value, m.Groups[2].Success));

    /// <summary>A name as written after <c>as</c>: <c>$n</c> and <c>n</c> are the same variable.</summary>
    public static string NameOf(string text) => text.TrimStart('$');

    /// <summary>
    /// Replaces every reference with its value; <c>$n+1</c> adds to an integer value.
    /// </summary>
    /// <exception cref="CommandFailedException">A name has no value yet, or an offset meets a non-integer value.</exception>
    public static string Substitute(string text, IDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0)
        {
            return text;
        }

        return Reference.Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new CommandFailedException("'$" + name + "' has no value: the line that sets it did not run.");
            }

            if (!m.Groups[2].Success)
            {
                return value;
            }

            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                throw new CommandFailedException("'$" + name + "' is \"" + value + "\", not an integer, so '" + m.Value + "' cannot be computed.");
            }

            var offset = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return (m.Groups[2].Value == "+" ? number + offset : number - offset).ToString(CultureInfo.InvariantCulture);
        });
    }
}
