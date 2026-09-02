using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

[Command("goto",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(INavigableToRecord) },
    Usage = "goto <code>")]
public sealed class GotoCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<INavigableToRecord>().NavigateToRecordAsync(cancellationToken);
}