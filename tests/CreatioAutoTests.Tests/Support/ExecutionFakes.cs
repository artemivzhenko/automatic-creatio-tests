using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;
using Microsoft.Playwright;

namespace CreatioAutoTests.Tests.Support;

public sealed class FakeExecutionSession : IExecutionSession
{
    public RunSettings Settings { get; init; } = new();
    public ElementCatalog Catalog { get; init; } = CatalogBuilder.Default();
    public IReadOnlyDictionary<string, string> StaticValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ObjectDefinition> Objects { get; init; } = new Dictionary<string, ObjectDefinition>();
    public string? CurrentUser => null;
    public string? CurrentPage => null;
    public int MaterializedCount { get; private set; }

    public Task UseUserAsync(string username, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OpenPageAsync(string pageName, string? recordId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AdoptPageAsync(string pageName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NavigateAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IPage RequirePage() => throw new InvalidOperationException("No browser is available in unit tests.");
    public IUiElement Materialize(ResolvedTarget target)
    {
        MaterializedCount++;
        return new FakeUiElement(target.Code);
    }

    public IReadOnlyDictionary<string, ProcessDefinition> Processes { get; init; } =
        new Dictionary<string, ProcessDefinition>(StringComparer.OrdinalIgnoreCase);
    public CreatioAutoTests.Session.ProcessRunResult? LastProcess { get; set; }
    public Func<string, IReadOnlyDictionary<string, string>, CreatioAutoTests.Session.ProcessRunResult>? OnStart { get; init; }
    public Func<string, string, bool>? OnRecordExists { get; init; }

    public Task<CreatioAutoTests.Session.ProcessRunResult> StartProcessAsync(string process, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        LastProcess = OnStart is null
            ? new CreatioAutoTests.Session.ProcessRunResult(process, Guid.NewGuid().ToString(), true, null, new Dictionary<string, string>())
            : OnStart(process, values);
        return Task.FromResult(LastProcess);
    }

    public Task<bool> RecordExistsAsync(string entity, string filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnRecordExists?.Invoke(entity, filter) ?? false);
}

public sealed class FakeUiElement : IUiElement
{
    public FakeUiElement(string code) => Code = code;
    public string Code { get; }
    public string Label => Code;
}

public sealed class SucceedingTestCommand : ICommand
{
    public static int Calls { get; set; }
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class FailedTestCommand : ICommand
{
    public static int Calls { get; set; }
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        Calls++;
        throw new CommandFailedException("expected command failure");
    }
}

public sealed class BrokenTestCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("expected broken command");
}