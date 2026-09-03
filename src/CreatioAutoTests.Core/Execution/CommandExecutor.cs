using System.Diagnostics;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Frontend;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Execution;

/// <summary>
/// The second of the two passes. Runs an already validated plan: nothing is parsed, resolved or
/// type checked here, so this layer only deals with what the browser actually does.
/// </summary>
public sealed class CommandExecutor
{
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CommandExecutor> _logger;

    /// <summary>Initializes command activation and logger creation for executing validated plan steps.</summary>
    public CommandExecutor(IServiceProvider services, ILoggerFactory loggerFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<CommandExecutor>();
    }

    /// <summary>
    /// Executes every step of the plan. Stops at the first failure unless the run settings ask for
    /// the whole case to be attempted.
    /// </summary>
    public async Task<TestCaseResult> ExecuteAsync(
        ExecutionPlan plan,
        IExecutionSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(session);

        var result = new TestCaseResult
        {
            Name = plan.TestName,
            FilePath = plan.FilePath,
            ValidationIssues = plan.Issues
        };

        if (!plan.IsValid)
        {
            return result;
        }

        var stopwatch = Stopwatch.StartNew();

        foreach (var step in plan.Steps)
        {
            var outcome = await ExecuteStepAsync(step, session, cancellationToken).ConfigureAwait(false);
            result.Outcomes.Add(outcome);

            if (outcome.Success)
            {
                continue;
            }

            if (!session.Settings.ContinueOnFailure)
            {
                break;
            }
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        return result;
    }

    private async Task<CommandOutcome> ExecuteStepAsync(
        ExecutionStep step,
        IExecutionSession session,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var elements = MaterializeTargets(step, session);
            var logger = _loggerFactory.CreateLogger(step.Registration.ImplementationType);
            var context = new CommandContext(step, elements, session, logger);

            var command = (ICommand)ActivatorUtilities.GetServiceOrCreateInstance(
                _services,
                step.Registration.ImplementationType);

            _logger.LogDebug("line {Line}: {Text}", step.Command.LineNumber, step.Command.RawText);

            await command.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            return new CommandOutcome(step, true, null, stopwatch.Elapsed);
        }
        catch (CommandFailedException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("line {Line} failed: {Message}", step.Command.LineNumber, ex.Message);
            return new CommandOutcome(step, false, ex.Message, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "line {Line} threw.", step.Command.LineNumber);
            return new CommandOutcome(step, false, ex.GetType().Name + ": " + ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>Turns the targets the validator resolved into live elements on the open page.</summary>
    private static IReadOnlyList<IUiElement> MaterializeTargets(ExecutionStep step, IExecutionSession session)
    {
        if (step.Targets.Count == 0)
        {
            return Array.Empty<IUiElement>();
        }

        var elements = new IUiElement[step.Targets.Count];
        for (var i = 0; i < step.Targets.Count; i++)
        {
            elements[i] = session.Materialize(step.Targets[i]);
        }

        return elements;
    }
}
