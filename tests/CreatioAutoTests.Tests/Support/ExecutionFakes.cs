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

    public IDictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<string> Console { get; } = new();
    public IReadOnlyList<string> ConsoleErrors => Console;

    /// <summary>Every write, in order: "insert UsrOrder {id} UsrName=x", "update ...", "delete UsrOrder {id}".</summary>
    public List<string> Writes { get; } = new();
    public List<(string Entity, string Id)> Pending { get; } = new();
    public int Cleanups { get; private set; }
    public Func<string, IReadOnlyList<string>, string, int, IReadOnlyList<IReadOnlyDictionary<string, string>>>? OnQuery { get; init; }
    public Func<string, string, long>? OnCount { get; init; }
    public Func<string, string>? OnDelete { get; init; }
    private int _next;

    public Task<string> InsertRecordAsync(string entity, IReadOnlyList<RecordAssignment> values, bool keep, CancellationToken cancellationToken = default)
    {
        var id = new Guid(++_next, 0, 0, new byte[8]).ToString();
        Writes.Add("insert " + entity + " " + id + " " + string.Join(";", values));
        if (!keep)
        {
            Pending.Add((entity, id));
        }

        return Task.FromResult(id);
    }

    public Task UpdateRecordAsync(string entity, string id, IReadOnlyList<RecordAssignment> values, CancellationToken cancellationToken = default)
    {
        Writes.Add("update " + entity + " " + id + " " + string.Join(";", values));
        return Task.CompletedTask;
    }

    public Task DeleteRecordAsync(string entity, string id, CancellationToken cancellationToken = default)
    {
        if (OnDelete?.Invoke(id) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        Writes.Add("delete " + entity + " " + id);
        Pending.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> QueryRecordsAsync(string entity, IReadOnlyList<string> select, string filter, int top, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnQuery?.Invoke(entity, select, filter, top) ?? Array.Empty<IReadOnlyDictionary<string, string>>());

    public Task<long> CountRecordsAsync(string entity, string filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnCount?.Invoke(entity, filter) ?? 0);

    public async Task<IReadOnlyList<string>> CleanupAsync(CancellationToken cancellationToken = default)
    {
        Cleanups++;
        var failures = new List<string>();
        foreach (var (entity, id) in Enumerable.Reverse(Pending.ToArray()))
        {
            try
            {
                await DeleteRecordAsync(entity, id, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(entity + " " + id + ": " + ex.Message);
            }
        }

        Pending.Clear();
        return failures;
    }
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