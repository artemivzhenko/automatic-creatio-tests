namespace CreatioAutoTests.Commands;

/// <summary>
/// A DSL command. By the time this runs the command has already been validated and its targets
/// resolved, so an implementation is usually a couple of lines.
/// </summary>
public interface ICommand
{
    /// <summary>Executes an already validated command whose targets were resolved before browser activity began.</summary>
    Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default);
}
