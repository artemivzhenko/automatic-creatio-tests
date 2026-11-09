using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Controls;

/// <summary>
/// Classic button. The name in the schema is suffixed with <c>Button</c> in the id:
/// <c>{Prefix}{Name}Button-wrapperEl</c> is the whole button when it has an icon or a menu,
/// <c>{Prefix}{Name}Button-textEl</c> its caption, and a text-only button has only the latter.
/// The prefix is the page schema, or the detail schema for a detail's own buttons.
/// </summary>
[UiElement("Button", Generation = UiGeneration.ClassicUI, Controls = new[] { "Button" },
    Options = typeof(ClassicButtonOptions))]
public sealed class Button : ClassicElementBase, IClickable
{
    private readonly ClassicButtonOptions _options;
    private readonly string _prefix;

    /// <summary>Initializes a Classic button from its options.</summary>
    public Button(ElementRuntimeContext context) : this(context, context.Definition.BindOptions<ClassicButtonOptions>())
    {
    }

    private Button(ElementRuntimeContext context, ClassicButtonOptions options) : base(context, options)
    {
        _options = options;
        _prefix = string.IsNullOrWhiteSpace(options.Detail) ? Schema : options.Detail.Trim();
    }

    /// <summary>The wrapper when there is one, else the text span.</summary>
    protected override string BuildSelector() =>
        ClassicIds.Selector(ClassicIds.ButtonWrapper(_prefix, Name)) + ", " +
        ClassicIds.Selector(ClassicIds.ButtonText(_prefix, Name));

    /// <inheritdoc />
    protected override string LocatorDescription => _prefix + Name + "Button";

    /// <summary>The caption span.</summary>
    protected override ILocator LabelLocator => Page.Locator(ClassicIds.Selector(ClassicIds.ButtonText(_prefix, Name)));

    /// <summary>Classic upper-cases button captions in CSS, so the caption is read from the text content.</summary>
    public override async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        var text = await TextContentAsync(LabelLocator).ConfigureAwait(false);
        return text is null ? null : NormalizeLabel(text);
    }

    /// <summary>Clicks the button unless Classic marked it disabled, then lets the page react.</summary>
    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var classes = await SafeAttributeAsync(Root, "class").ConfigureAwait(false) ?? string.Empty;
        if (classes.Contains("t-btn-disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Button '" + Code + "' is disabled and cannot be clicked.");
        }

        await Root.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        if (_options.SettleMs > 0)
        {
            await Page.WaitForTimeoutAsync(_options.SettleMs).ConfigureAwait(false);
        }

        if (_options.IsSave)
        {
            await CompleteSaveAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A Classic save either succeeds, and the Save button leaves the toolbar as the page turns
    /// clean, or fails with a message box: a validation error naming an empty required field, or a
    /// server error. The box is dismissed and its caption is the failure.
    /// </summary>
    private async Task CompleteSaveAsync()
    {
        var box = Page.Locator(".ts-messagebox-box");
        var deadline = DateTime.UtcNow.AddMilliseconds(_options.DisappearTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (await box.CountAsync().ConfigureAwait(false) > 0 && await box.First.IsVisibleAsync().ConfigureAwait(false))
            {
                var caption = (await box.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                var firstLine = caption.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? caption;

                var button = box.First.Locator(".t-btn-wrapper[data-item-marker]");
                if (await button.CountAsync().ConfigureAwait(false) > 0)
                {
                    await button.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
                }

                throw new InvalidOperationException("Save failed. Creatio reported: " + firstLine);
            }

            if (!await Root.IsVisibleAsync().ConfigureAwait(false))
            {
                return;
            }

            await Page.WaitForTimeoutAsync(250).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Save button '" + Code + "' was still showing after " + _options.DisappearTimeoutMs +
            " ms and no error was shown; the record was probably not saved.");
    }
}
