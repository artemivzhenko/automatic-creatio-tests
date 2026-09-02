using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>Selects the user the rest of the script runs as.</summary>
[Command("use", Validator = typeof(UserValidator), Usage = "use <username>")]
public sealed class UseCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Session.UseUserAsync(context.Command.ValueAt(0)!, cancellationToken);
}
