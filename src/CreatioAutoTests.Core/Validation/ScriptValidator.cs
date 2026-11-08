using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Validation;

/// <summary>
/// The first of the two passes. Walks a whole .act file without a browser, resolving every element
/// against the page the script is standing on, and reports all problems at once. Only a file that
/// validates completely is turned into an <see cref="ExecutionPlan"/>.
/// </summary>
public sealed class ScriptValidator
{
    private readonly CommandRegistry _commands;
    private readonly IServiceProvider _services;

    /// <summary>Initializes reflection-discovered command definitions and their validator service provider.</summary>
    public ScriptValidator(CommandRegistry commands, IServiceProvider services)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Validates every line and accumulates all issues so authors can fix a complete file at once.
    /// Virtual page transitions from <c>open</c> and <c>create</c> make later element references
    /// resolve against the page where execution will stand.
    /// </summary>
    public ExecutionPlan Validate(
        string testName,
        string filePath,
        IReadOnlyList<ParsedCommand> commands,
        ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ValidationIssue>();
        var steps = new List<ExecutionStep>();

        context.CurrentPage = null;
        context.CurrentProcess = null;
        context.UserDeclared = false;

        if (commands.Count == 0)
        {
            issues.Add(new ValidationIssue(0, string.Empty, "The test case contains no commands."));
            return ExecutionPlan.Invalid(testName, filePath, issues);
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var parsed = commands[i];

            if (!_commands.TryResolve(parsed.Keyword, out var registration))
            {
                issues.Add(new ValidationIssue(
                    parsed.LineNumber,
                    parsed.RawText,
                    "Unknown command '" + parsed.Keyword + "'. Known commands: " +
                    string.Join(", ", _commands.Keywords) + "."));
                continue;
            }

            // Every script names the user it runs as before it does anything else.
            if (i == 0 && !IsUserDeclaration(registration))
            {
                issues.Add(new ValidationIssue(
                    parsed.LineNumber,
                    parsed.RawText,
                    "A test case must start with 'use <username>'."));
            }

            var validator = ResolveValidator(registration);
            var result = validator.Validate(parsed, context);

            if (!result.IsValid)
            {
                issues.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText, result.Error!));
                ApplyStateTransition(context, result);
                continue;
            }

            var unsupported = FindUnsupportedTarget(registration, result);
            if (unsupported is not null)
            {
                issues.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText, unsupported));
                ApplyStateTransition(context, result);
                continue;
            }

            ApplyStateTransition(context, result);
            steps.Add(new ExecutionStep(parsed, registration, result.Targets));
        }

        return issues.Count == 0
            ? ExecutionPlan.Valid(testName, filePath, steps)
            : ExecutionPlan.Invalid(testName, filePath, issues);
    }

    private static bool IsUserDeclaration(CommandRegistration registration) =>
        string.Equals(registration.Keyword, "use", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Carries 'open' and 'create' page changes forward so the codes on later lines are resolved
    /// against the page they will actually be evaluated on.
    /// </summary>
    private static void ApplyStateTransition(ValidationContext context, ValidationResult result)
    {
        if (result.NextPage is not null)
        {
            context.CurrentPage = result.NextPage;
        }

        if (result.NextProcess is not null)
        {
            context.CurrentProcess = result.NextProcess;
        }

        if (result.DeclaresUser)
        {
            context.UserDeclared = true;
        }
    }

    /// <summary>
    /// Confirms the resolved element is something this command can drive. This is where the
    /// command's declared target list meets the class the validator picked.
    /// </summary>
    private static string? FindUnsupportedTarget(CommandRegistration registration, ValidationResult result)
    {
        if (registration.IsTargetless || result.Targets.Count == 0)
        {
            return null;
        }

        var primary = result.Targets[0];
        if (registration.Accepts(primary.ElementType))
        {
            return null;
        }

        var expected = string.Join(", ", registration.SupportedTargets.Select(t => t.Name));

        return "'" + registration.Keyword + "' cannot work with '" + primary.Code + "' (" +
               primary.Definition.Type + "): it requires " + expected + ".";
    }

    private ICommandValidator ResolveValidator(CommandRegistration registration)
    {
        return (ICommandValidator)ActivatorUtilities.GetServiceOrCreateInstance(
            _services,
            registration.ValidatorType);
    }
}
