using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>Opens a configured page, optionally an existing record.</summary>
[Command("open", Validator = typeof(PageValidator), Usage = "open <page> [for <recordId>]")]
public sealed class OpenCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var pageName = context.Command.ValueAt(0)!;
        var recordId = context.Command.ArgumentCount == 3 ? context.Command.ValueAt(2) : null;

        return context.Session.OpenPageAsync(pageName, recordId, cancellationToken);
    }
}
