using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation.Validators;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Clicks an element. Reference implementation of a command: the attribute states the keyword, the
/// validator and the capability required, and the body does exactly one thing.
/// Declaring <see cref="IClickable"/> rather than a concrete class is what will let a future Tab
/// element work with 'click' without this file changing.
/// </summary>
[Command("click",
    Validator = typeof(SingleTargetValidator),
    SupportedTargets = new[] { typeof(IClickable) },
    Usage = "click <code>")]
public sealed class ClickCommand : ICommand
{
    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
        context.Target<IClickable>().ClickAsync(cancellationToken);
}
