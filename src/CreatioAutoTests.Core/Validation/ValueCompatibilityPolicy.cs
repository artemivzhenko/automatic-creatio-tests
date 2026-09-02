using System.Globalization;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation;

/// <summary>
/// The single place that decides whether a value may be written into an element. Both rules it
/// enforces are checkable without a browser, so a bad assignment fails during the validation pass
/// instead of minutes into a run.
/// </summary>
public sealed class ValueCompatibilityPolicy
{
    /// <summary>Source kinds accepted by each target kind when copying element to element.</summary>
    private static readonly Dictionary<ValueKind, ValueKind[]> CopyRules = new()
    {
        [ValueKind.Text] = new[]
        {
            ValueKind.Text, ValueKind.Number, ValueKind.Boolean,
            ValueKind.DateTime, ValueKind.Lookup, ValueKind.Stage
        },
        [ValueKind.Number] = new[] { ValueKind.Number },
        [ValueKind.Boolean] = new[] { ValueKind.Boolean },
        [ValueKind.DateTime] = new[] { ValueKind.DateTime },
        [ValueKind.Lookup] = new[] { ValueKind.Text, ValueKind.Lookup },
        [ValueKind.Stage] = new[] { ValueKind.Text, ValueKind.Lookup, ValueKind.Stage }
    };

    /// <summary>Checks 'set target source' where both sides are elements.</summary>
    public bool CanCopy(ValueKind source, ValueKind target, out string error)
    {
        error = string.Empty;

        if (target == ValueKind.None)
        {
            error = "the target does not hold a value";
            return false;
        }

        if (source == ValueKind.None)
        {
            error = "the source does not hold a value";
            return false;
        }

        if (!CopyRules.TryGetValue(target, out var accepted))
        {
            error = "values of kind " + target + " cannot be assigned";
            return false;
        }

        if (Array.IndexOf(accepted, source) >= 0)
        {
            return true;
        }

        error = "a " + source + " value cannot be copied into a " + target +
                " field; accepted sources: " + string.Join(", ", accepted);
        return false;
    }

    /// <summary>Checks 'set target "literal"' against what the target can actually store.</summary>
    public bool IsLiteralAssignable(string literal, ValueKind target, out string error)
    {
        error = string.Empty;
        var text = literal ?? string.Empty;

        switch (target)
        {
            case ValueKind.None:
                error = "the target does not hold a value";
                return false;

            case ValueKind.Boolean:
                if (text is "true" or "false")
                {
                    return true;
                }

                error = "a boolean field accepts only \"true\" or \"false\", got \"" + text + "\"";
                return false;

            case ValueKind.Number:
                if (text.Length == 0 || decimal.TryParse(
                        text,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out _))
                {
                    return true;
                }

                error = "\"" + text + "\" is not a number";
                return false;

            case ValueKind.DateTime:
                if (text.Length == 0 || IsoDateTime.TryParse(text, out _))
                {
                    return true;
                }

                error = "\"" + text + "\" is not an ISO 8601 date/time (" +
                        IsoDateTime.DescribeAcceptedFormats() + ")";
                return false;

            case ValueKind.Stage:
                if (text.Length > 0)
                {
                    return true;
                }

                error = "a stage bar needs the name of the stage to switch to";
                return false;

            default:
                return true;
        }
    }

    /// <summary>Operators the DSL allows against a given kind of value.</summary>
    public IReadOnlyCollection<string> OperatorsFor(ValueKind kind) => kind switch
    {
        ValueKind.Number or ValueKind.DateTime => new[] { "eq", "ne", "gt", "ge", "lt", "le" },
        ValueKind.Boolean => new[] { "eq", "ne" },
        ValueKind.None => Array.Empty<string>(),
        _ => new[] { "eq", "ne", "contains", "startswith", "endswith" }
    };
}
