using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Runs the record creation workflow an object describes: open its section, click New, follow
/// Creatio onto the form, fill the configured fields and save. Every part of that workflow was
/// checked during validation, so failures here are real UI failures.
/// </summary>
[Command("create", Validator = typeof(ObjectValidator), Usage = "create <object>")]
public sealed class CreateCommand : ICommand
{
    /// <summary>Executes the validated section, New, form, fill, and Save workflow for an object definition.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var objectName = context.Command.ValueAt(0)!;
        var definition = context.Session.Objects[objectName];
        var session = context.Session;

        context.Logger.LogInformation("Creating {Object} through {Section}.", objectName, definition.SectionPage);

        await session.OpenPageAsync(definition.SectionPage, recordId: null, cancellationToken).ConfigureAwait(false);
        await ClickAsync(context, definition.SectionPage, definition.NewButton, cancellationToken).ConfigureAwait(false);

        // Creatio navigates on its own; rebind the open page to the form configuration.
        await session.AdoptPageAsync(definition.FormPage, cancellationToken).ConfigureAwait(false);

        await FillAsync(context, definition, cancellationToken).ConfigureAwait(false);
        await ClickAsync(context, definition.FormPage, definition.SaveButton, cancellationToken).ConfigureAwait(false);

        context.Logger.LogInformation("Created {Object}.", objectName);
    }

    private static async Task FillAsync(
        CommandContext context,
        ObjectDefinition definition,
        CancellationToken cancellationToken)
    {
        foreach (var pair in definition.Fields)
        {
            var element = Materialize(context, definition.FormPage, pair.Key);

            if (element is not IValueElement valueElement)
            {
                throw new CommandFailedException(
                    "'" + pair.Key + "' on page '" + definition.FormPage + "' does not hold a value.");
            }

            if (element is IStateAware stateAware &&
                await stateAware.IsReadOnlyAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CommandFailedException(
                    "'" + pair.Key + "' is read-only on the form and cannot be filled.");
            }

            var value = context.Session.StaticValues[pair.Value];
            await valueElement.SetValueFromStringAsync(value, cancellationToken).ConfigureAwait(false);

            context.Logger.LogDebug("Filled {Code} with {Value}.", pair.Key, value);
        }
    }

    private static async Task ClickAsync(
        CommandContext context,
        string page,
        string code,
        CancellationToken cancellationToken)
    {
        if (Materialize(context, page, code) is not IClickable clickable)
        {
            throw new CommandFailedException("'" + code + "' on page '" + page + "' cannot be clicked.");
        }

        await clickable.ClickAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Elements used by 'create' come from the object definition rather than the command line, so
    /// they are resolved here against the catalog instead of arriving as validated targets.
    /// </summary>
    private static IUiElement Materialize(CommandContext context, string page, string code)
    {
        if (!context.Session.Catalog.TryGetElement(page, code, out var entry))
        {
            throw new CommandFailedException("'" + code + "' is not configured on page '" + page + "'.");
        }

        return context.Session.Materialize(new ResolvedTarget(code, entry));
    }
}
