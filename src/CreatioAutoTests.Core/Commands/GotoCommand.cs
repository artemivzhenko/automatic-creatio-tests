using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>Follows the record link offered by a validated navigable target.</summary>
[Command("goto",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(INavigableToRecord) },
    Usage = "goto <code>")]
public sealed class GotoCommand : ICommand
{
    /// <summary>Delegates record navigation to the resolved target.</summary>
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<INavigableToRecord>().NavigateToRecordAsync(cancellationToken);
}