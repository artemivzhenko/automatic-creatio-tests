using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Configuration;

/// <summary>
/// One business process a script may start, loaded from its own file in the processes directory.
///
/// A process test never opens a browser: it starts the process over the process engine service
/// and reads the result parameters back. What validation needs to know statically is therefore
/// the parameter list - which ones can be passed in, which ones come back, and of what type.
/// </summary>
public sealed class ProcessDefinition
{
    /// <summary>Schema name of the process: <c>start UsrOrder_Process</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parameters of the process, in the order the schema declares them.</summary>
    public List<ProcessParameter> Parameters { get; set; } = new();

    /// <summary>Parameters a script may pass with <c>start ... with</c>: inputs and variables.</summary>
    public IEnumerable<ProcessParameter> Inputs =>
        Parameters.Where(p => p.Direction is not ProcessParameter.Out);

    /// <summary>Parameters a script may assert with <c>check-result</c>: outputs and variables.</summary>
    public IEnumerable<ProcessParameter> Outputs =>
        Parameters.Where(p => p.Direction is not ProcessParameter.In);

    /// <summary>Finds a parameter by code, case-insensitively.</summary>
    public ProcessParameter? Find(string code) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Formats the process name and its parameter count for diagnostics.</summary>
    public override string ToString() => Name + " (" + Parameters.Count + " parameter(s))";
}

/// <summary>One parameter of a process: its code, Creatio type name and direction.</summary>
public sealed class ProcessParameter
{
    /// <summary>Direction value for a parameter the process reads.</summary>
    public const string In = "in";

    /// <summary>Direction value for a parameter the process produces.</summary>
    public const string Out = "out";

    /// <summary>Direction value for a parameter that is both set and read.</summary>
    public const string Variable = "variable";

    /// <summary>Code of the parameter as the schema names it.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Creatio data value type name, such as <c>Integer</c>, <c>Text</c> or <c>Lookup</c>.</summary>
    public string Type { get; set; } = "Text";

    /// <summary><c>in</c>, <c>out</c> or <c>variable</c>; anything else is treated as a variable.</summary>
    public string Direction { get; set; } = Variable;

    /// <summary>
    /// The value kind the comparison and literal rules of the UI tests use, derived from the
    /// Creatio type name so that <c>check-result Total gt "0"</c> is checked the same way a
    /// number field is.
    /// </summary>
    public ValueKind ValueKind => Type switch
    {
        "Integer" or "Float" or "Float1" or "Float2" or "Float3" or "Float4" or "Float8" or "Money" => ValueKind.Number,
        "Boolean" => ValueKind.Boolean,
        "Date" or "DateTime" or "Time" => ValueKind.DateTime,
        "Lookup" => ValueKind.Lookup,
        _ => ValueKind.Text,
    };

    /// <summary>Formats the parameter the way the schema designer shows it.</summary>
    public override string ToString() => Code + " (" + Type + ", " + Direction + ")";
}
