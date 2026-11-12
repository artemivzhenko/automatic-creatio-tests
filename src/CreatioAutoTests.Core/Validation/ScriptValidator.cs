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
        var warnings = new List<ValidationIssue>();
        var steps = new List<ExecutionStep>();
        string? activeTab = null;
        var shownConfirmed = new HashSet<string>(StringComparer.Ordinal);

        context.CurrentPage = null;
        context.CurrentProcess = null;
        context.UserDeclared = false;
        context.Variables.Clear();
        var inserted = new Dictionary<string, (ParsedCommand Line, bool Kept)>(StringComparer.Ordinal);

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

            var referenceError = CheckReferences(parsed, context);
            if (referenceError is not null)
            {
                issues.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText, referenceError));
                continue;
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

            foreach (var target in result.Targets)
            {
                var tab = target.Definition.Tab;
                if (!string.IsNullOrWhiteSpace(tab) && !string.Equals(tab, activeTab, StringComparison.Ordinal))
                {
                    warnings.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText,
                        target.Code + " lies on tab " + tab + "; add 'click " + tab +
                        "' or 'set Tabs ...' before line " + parsed.LineNumber));
                }
            }

            ConditionalWarning(parsed, result, shownConfirmed, warnings);

            if (result.NextPage is not null)
            {
                activeTab = null;
                shownConfirmed.Clear();
            }
            else if (result.Targets.Count > 0 && string.Equals(parsed.Keyword, "click", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(result.Targets[0].Definition.Type, "Tab", StringComparison.OrdinalIgnoreCase))
            {
                activeTab = result.Targets[0].Code;
            }
            else if (result.Targets.Count > 0 && string.Equals(parsed.Keyword, "set", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(result.Targets[0].Definition.Type, "TabPanel", StringComparison.OrdinalIgnoreCase))
            {
                var value = parsed.ArgumentAt(1);
                var caption = value?.Value;
                if (value is not null && !value.IsQuoted && context.StaticValues.TryGetValue(value.Value, out var staticValue))
                {
                    caption = staticValue;
                }

                var matchingTabs = context.Catalog.Entries
                    .Where(e => string.Equals(e.Page, context.CurrentPage, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(e.Definition.Type, "Tab", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(e.Definition.Label, caption, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Definition.Code).Take(2).ToArray();
                activeTab = matchingTabs.Length == 1 ? matchingTabs[0] : null;
            }

            ApplyStateTransition(context, result);
            if (result.Variable is { } effect)
            {
                if (effect.Defines is not null)
                {
                    context.Variables.Add(effect.Defines);
                    if (effect.Inserts)
                    {
                        inserted[effect.Defines] = (parsed, effect.Kept);
                    }
                }

                if (effect.Deletes is not null)
                {
                    inserted.Remove(effect.Deletes);
                }
            }

            steps.Add(new ExecutionStep(parsed, registration, result.Targets));
        }

        // '# writes' states the author's intent: every record the case inserts is deleted by the case or kept on purpose.
        var writes = context.Directives.ContainsKey(WritesDirective);
        if (writes)
        {
            foreach (var (alias, (line, kept)) in inserted.Where(i => !i.Value.Kept))
            {
                issues.Add(new ValidationIssue(line.LineNumber, line.RawText,
                    "'# writes': the record inserted as '" + alias + "' is neither deleted later ('delete <object> $" + alias +
                    "') nor marked 'keep' ('insert <object> as " + alias + " keep set ...')."));
            }
        }

        var plan = issues.Count == 0
            ? ExecutionPlan.Valid(testName, filePath, steps, warnings)
            : ExecutionPlan.Invalid(testName, filePath, issues, warnings);
        plan.StopOnFailure = writes;
        plan.AcceptanceCriterion = context.Directives.TryGetValue(AcDirective, out var ac) && ac.Length > 0 ? ac : null;
        return plan;
    }

    /// <summary>
    /// A control a page rule toggles may be absent. 'exists' samples that once; 'set' and 'check-value' have no
    /// wait of their own and fail after the whole element timeout. 'check X is shown' is the wait.
    /// </summary>
    private static void ConditionalWarning(
        ParsedCommand parsed, ValidationResult result, ISet<string> shownConfirmed, List<ValidationIssue> warnings)
    {
        if (result.Targets.Count == 0 || !result.Targets[0].Definition.Conditional)
        {
            return;
        }

        var code = result.Targets[0].Code;
        var rest = parsed.Arguments.Skip(1).Select(a => a.Value.ToLowerInvariant()).ToArray();
        var isCheck = string.Equals(parsed.Keyword, "check", StringComparison.OrdinalIgnoreCase);
        if (isCheck && rest is ["is", "shown"])
        {
            shownConfirmed.Add(code);
        }
        else if (isCheck && rest is ["is", "not", "shown"])
        {
            shownConfirmed.Remove(code);
        }
        else if (isCheck && rest is ["exists"] or ["not", "exists"])
        {
            warnings.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText,
                "a rule toggles " + code + "; 'check " + code + " is [not] shown' waits for it."));
        }
        else if ((string.Equals(parsed.Keyword, "set", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(parsed.Keyword, "check-value", StringComparison.OrdinalIgnoreCase)) &&
                 !shownConfirmed.Contains(code))
        {
            warnings.Add(new ValidationIssue(parsed.LineNumber, parsed.RawText,
                "a rule toggles " + code + "; put 'check " + code + " is shown' before this line."));
        }
    }

    /// <summary>Header directive: the case writes data; stop at the first failure and account for every insert.</summary>
    public const string WritesDirective = "writes";

    /// <summary>Header directive: the acceptance criterion the case proves, for its evidence files.</summary>
    public const string AcDirective = "ac";

    /// <summary>
    /// A <c>$name</c> in an unquoted argument must be defined by an earlier line; the token after <c>as</c> is the
    /// definition itself. <c>$name+N</c> computes a number, so it is only the value 'check-value' compares with.
    /// </summary>
    private static string? CheckReferences(ParsedCommand parsed, ValidationContext context)
    {
        for (var i = 0; i < parsed.ArgumentCount; i++)
        {
            var token = parsed.Arguments[i];
            if (token.IsQuoted || (i > 0 && string.Equals(parsed.Arguments[i - 1].Value, "as", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var (name, hasOffset) in CaseVariables.References(token.Value))
            {
                if (!context.Variables.Contains(name))
                {
                    return "'$" + name + "' is used before a line defines it: 'insert <object> as " + name +
                           " set ...', 'remember <code> as $" + name + "' or 'remember-record <object> <filter> as $" + name + "'.";
                }

                if (hasOffset && !(string.Equals(parsed.Keyword, "check-value", StringComparison.OrdinalIgnoreCase) && i == 2))
                {
                    return "'" + token.Value + "' computes a number; it is only allowed as the value 'check-value' compares with.";
                }
            }
        }

        return null;
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
