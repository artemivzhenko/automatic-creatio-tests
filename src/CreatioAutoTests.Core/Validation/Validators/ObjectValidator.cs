using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Validation.Validators;

/// <summary>
/// Shape of 'create': one configured object. The whole workflow the object describes is checked
/// here as well, so a broken objects.config.json is reported before a browser opens: both pages
/// exist, both buttons exist and are clickable, and every field it fills is real and writable.
/// </summary>
public sealed class ObjectValidator : ICommandValidator
{
    private readonly ValueCompatibilityPolicy _policy;

    /// <summary>Initializes static compatibility checks for fields populated by object creation.</summary>
    public ObjectValidator(ValueCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Validates every page, clickable button, writable field, and static value in a create workflow.</summary>
    public ValidationResult Validate(ParsedCommand command, ValidationContext context)
    {
        if (command.ArgumentCount != 1)
        {
            return ValidationResult.Fail("'create' takes exactly one object name: 'create <object>'.");
        }

        var name = command.ValueAt(0)!;
        if (!context.Objects.TryGetValue(name, out var definition))
        {
            return ValidationResult.Fail(
                "Object '" + name + "' is not configured. Known objects: " +
                (context.Objects.Count == 0
                    ? "(none)"
                    : string.Join(", ", context.Objects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))) +
                ".");
        }

        foreach (var page in new[] { definition.SectionPage, definition.FormPage })
        {
            if (!context.Catalog.PageExists(page))
            {
                return ValidationResult.Fail(
                    "Object '" + name + "' refers to unknown page '" + page + "'.");
            }
        }

        var buttonError =
            CheckButton(context, definition.SectionPage, definition.NewButton, name, "new") ??
            CheckButton(context, definition.FormPage, definition.SaveButton, name, "save");

        if (buttonError is not null)
        {
            return ValidationResult.Fail(buttonError);
        }

        var fieldError = CheckFields(context, definition, name);
        if (fieldError is not null)
        {
            return ValidationResult.Fail(fieldError);
        }

        // A create leaves the script standing on the form page.
        return ValidationResult.OkOnPage(definition.FormPage);
    }

    private static string? CheckButton(
        ValidationContext context,
        string page,
        string code,
        string objectName,
        string role)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Object '" + objectName + "' does not name its " + role + " button.";
        }

        if (!context.Catalog.TryGetElement(page, code, out var entry))
        {
            return "Object '" + objectName + "' refers to " + role + " button '" + code +
                   "' which is not configured on page '" + page + "'.";
        }

        if (!entry.Supports(typeof(IClickable)))
        {
            return "The " + role + " button '" + code + "' of object '" + objectName + "' is a " +
                   entry.Definition.Type + ", which cannot be clicked.";
        }

        return null;
    }

    private string? CheckFields(ValidationContext context, Configuration.ObjectDefinition definition, string name)
    {
        foreach (var pair in definition.Fields)
        {
            if (!context.Catalog.TryGetElement(definition.FormPage, pair.Key, out var entry))
            {
                return "Object '" + name + "' fills '" + pair.Key +
                       "' which is not configured on form page '" + definition.FormPage + "'.";
            }

            if (!context.StaticValues.TryGetValue(pair.Value, out var value))
            {
                return "Object '" + name + "' fills '" + pair.Key + "' from static value '" + pair.Value +
                       "' which does not exist.";
            }

            if (!_policy.IsLiteralAssignable(value, entry.ValueKind, out var error))
            {
                return "Object '" + name + "' cannot fill '" + pair.Key + "': " + error + ".";
            }
        }

        return null;
    }
}
