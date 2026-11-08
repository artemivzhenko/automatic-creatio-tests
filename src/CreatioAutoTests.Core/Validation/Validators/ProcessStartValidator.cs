using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'start':
///   start UsrOrder_Process
///   start UsrOrder_Process with Amount=10;Contact=0c48c818-08f0-4f70-9b58-be512227f3bf
/// The process and every parameter named are checked against the processes directory, and each
/// value against the parameter's type, so a typo fails before anything is started. Reports the
/// process it starts so that later 'check-result' lines know whose parameters they read.
/// </summary>
public sealed class ProcessStartValidator : ICommandValidator
{
    private readonly ValueCompatibilityPolicy _policy;

    /// <summary>Initializes the literal checks shared with the page commands.</summary>
    public ProcessStartValidator(ValueCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Validates the process name and its parameter assignments.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount == 0)
        {
            return ValidationResult.Fail("'start' takes a process name: 'start <process> [with Param=Value;Param=Value]'.");
        }

        var name = command.ValueAt(0)!;
        if (!context.Processes.TryGetValue(name, out var process))
        {
            var known = context.Processes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
            return ValidationResult.Fail(
                "Process '" + name + "' is not configured under Configuration/processes. Known: " +
                (known.Length == 0 ? "(none)" : string.Join(", ", known)) + ".");
        }

        if (command.ArgumentCount == 1)
        {
            return ValidationResult.OkWithProcess(name);
        }

        if (!string.Equals(command.ValueAt(1), "with", StringComparison.OrdinalIgnoreCase) || command.ArgumentCount < 3)
        {
            return ValidationResult.Fail(
                "'start " + name + "' takes its values after 'with': 'start " + name + " with Param=Value;Param=Value'.");
        }

        var text = RecordAssignments.Rejoin(command.Arguments.Skip(2));
        if (!RecordAssignments.TryParse(text, out var assignments, out var parseError))
        {
            return ValidationResult.Fail("'start " + name + "' cannot be read: " + parseError + ".");
        }

        foreach (var assignment in assignments)
        {
            var error = CheckAssignment(process, assignment, context);
            if (error is not null)
            {
                return ValidationResult.Fail("'start " + name + "': " + error + ".");
            }
        }

        return ValidationResult.OkWithProcess(name);
    }

    private string? CheckAssignment(ProcessDefinition process, RecordAssignment assignment, ValidationContext context)
    {
        var parameter = process.Find(assignment.Column);
        if (parameter is null)
        {
            return "the process has no parameter '" + assignment.Column + "'. It has: " +
                   string.Join(", ", process.Parameters.Select(p => p.Code));
        }

        if (!process.Inputs.Contains(parameter))
        {
            return "'" + parameter.Code + "' is an output parameter and cannot be passed in";
        }

        // A bare name may be a static value, the same way 'set' reads one.
        var literal = context.StaticValues.TryGetValue(assignment.Value, out var staticValue) ? staticValue : assignment.Value;

        return _policy.IsLiteralAssignable(literal, parameter.ValueKind, out var literalError)
            ? null
            : "'" + parameter.Code + "' rejected the value: " + literalError;
    }
}
