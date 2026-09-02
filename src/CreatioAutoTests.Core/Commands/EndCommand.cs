using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

[Command("end", Validator = typeof(NoArgumentsValidator), Usage = "end")]
public sealed class EndCommand : ICommand
{
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var page = context.Session.CurrentPage;
        if (string.IsNullOrWhiteSpace(page))
        {
            context.Logger.LogInformation("End reached with no page open; no values to report.");
            return;
        }

        context.Logger.LogInformation("Values on {Page} at end of script:", page);

        foreach (var entry in context.Session.Catalog.Entries.Where(entry =>
                     string.Equals(entry.Page, page, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var element = context.Session.Materialize(new ResolvedTarget(entry.Code, entry));
                if (element is not IValueElement valueElement)
                {
                    continue;
                }

                // Freedom UI renders only the open tab, so most of a page's controls are legitimately
                // absent at any moment. Skip them quickly instead of waiting out the element timeout.
                if (element is IExistable existable &&
                    !await existable
                        .ExistsAsync(context.Session.Settings.AbsenceTimeoutMs, cancellationToken)
                        .ConfigureAwait(false))
                {
                    context.Logger.LogInformation("{Code}: not on the open tab", entry.Code);
                    continue;
                }

                var value = await valueElement.GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
                context.Logger.LogInformation("{Code}: {Value}", entry.Code, value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 'end' is a diagnostic and must never turn a passing run into a failing one,
                // but a cancelled run still has to stop promptly.
                context.Logger.LogWarning(ex, "{Code}: unreadable.", entry.Code);
            }
        }
    }
}