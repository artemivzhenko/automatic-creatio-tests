using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Fields;

/// <summary>
/// Lookup control (crt-combobox). Writing a value means driving the autocomplete: type, wait for the
/// panel, poll until the option shows up because the list loads asynchronously, then click it.
/// </summary>
[UiElement("Lookup", HostTag = "crt-combobox", ValueKind = ValueKind.Lookup)]
public sealed class LookupField : FieldElementBase, INavigableToRecord
{
    private const string PanelSelector = "div.mat-autocomplete-panel.mat-autocomplete-visible";
    private const string LinkSelector = ".link-wrap a.crt-link";
    private const int OptionResolveTimeoutMs = 5000;
    private const int OptionPollIntervalMs = 100;
    private const int SettleAfterPickMs = 200;

    /// <summary>Initializes the autocomplete-backed lookup field.</summary>
    public LookupField(ElementRuntimeContext context) : base(context)
    {
    }


    /// <summary>Input that accepts lookup search text across supported Creatio versions.</summary>
    protected override ILocator ValueLocator =>
        Root.Locator(MaterialInputSelector + ", input.crt-autocomplete-input, input[role=\"combobox\"]");

    /// <summary>Href of the record link, refreshed on every read. Used by the 'goto' command.</summary>
    public string? RecordPageUrl { get; private set; }

    /// <summary>Checks lookup-specific readonly state because these controls omit the base lock icon.</summary>
    public override async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        var input = ValueLocator;

        var rootReadonly = await SafeAttributeAsync(Root, "readonly").ConfigureAwait(false);
        var inputReadonly = await SafeAttributeAsync(input, "readonly").ConfigureAwait(false);
        var ariaReadonly = await SafeAttributeAsync(input, "aria-readonly").ConfigureAwait(false);

        var disabled = await SafeIsDisabledAsync(input).ConfigureAwait(false);

        // A lookup never renders the padlock icon the base class also looks for.
        return IsFlagSet(rootReadonly)
               || IsFlagSet(inputReadonly)
               || string.Equals(ariaReadonly, "true", StringComparison.OrdinalIgnoreCase)
               || disabled;
    }

    /// <summary>Reads the committed link when present and refreshes its record URL for <c>goto</c>.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        // A rendered link wins over the input: it is what Creatio shows for a committed selection.
        var link = Root.Locator(LinkSelector);
        if (await link.CountAsync().ConfigureAwait(false) > 0)
        {
            RecordPageUrl = await link.First.GetAttributeAsync("href").ConfigureAwait(false);

            var linkText = await link.First.InnerTextAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                return linkText.Trim();
            }
        }
        else
        {
            RecordPageUrl = null;
        }

        try
        {
            var typed = await ValueLocator.InputValueAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(typed) ? string.Empty : typed.Trim();
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Drives the asynchronous autocomplete and commits the requested selectable option. Polling is
    /// necessary because the panel becomes visible before Creatio has loaded its option rows.
    /// </summary>
    public override async Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var target = value.Trim();

        await ClearAsync(cancellationToken).ConfigureAwait(false);

        var input = ValueLocator;
        await input.ClickAsync().ConfigureAwait(false);
        await input.FillAsync(target).ConfigureAwait(false);

        var panel = Page.Locator(PanelSelector);
        try
        {
            await panel.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TimeoutMs
            }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "Autocomplete panel never appeared for lookup '" + Code + "' while searching '" + target + "'.", ex);
        }

        var match = await ResolveOptionAsync(panel.First, target).ConfigureAwait(false);
        if (match is null)
        {
            var offered = await CollectOptionsAsync(panel.First).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Option '" + target + "' was not offered by lookup '" + Code + "'. Offered: " +
                (offered.Count == 0 ? "(none)" : string.Join(", ", offered)) + ".");
        }

        await match.ClickAsync().ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(SettleAfterPickMs).ConfigureAwait(false);

        var committed = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(committed))
        {
            throw new InvalidOperationException(
                "Lookup '" + Code + "' stayed empty after picking '" + target + "'.");
        }
    }

    /// <summary>Uses the lookup clear icon when available, then falls back to clearing the input.</summary>
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var clearButton = Root.Locator(".combobox-expander.clear, mat-icon[svgicon=\"small-close\"]");

        try
        {
            if (await clearButton.CountAsync().ConfigureAwait(false) > 0)
            {
                await clearButton.First.ClickAsync().ConfigureAwait(false);
                return;
            }
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Clear icon of lookup {Code} could not be clicked; falling back to Fill.", Code);
        }

        await ValueLocator.FillAsync(string.Empty).ConfigureAwait(false);
    }

    /// <summary>Follows the committed record link required by the <c>goto</c> command.</summary>
    public async Task NavigateToRecordAsync(CancellationToken cancellationToken = default)
    {
        await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(RecordPageUrl))
        {
            throw new InvalidOperationException(
                "Lookup '" + Code + "' has no record link: it is either empty or not rendered as a link.");
        }

        var link = Root.Locator(LinkSelector);
        if (await link.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException("Record link of lookup '" + Code + "' disappeared from the DOM.");
        }

        await link.First.ClickAsync().ConfigureAwait(false);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
        {
            Timeout = Context.Settings.PageLoadTimeoutMs
        }).ConfigureAwait(false);
    }

    /// <summary>Polls the panel until the wanted option is rendered or the budget runs out.</summary>
    private async Task<ILocator?> ResolveOptionAsync(ILocator panel, string target)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(OptionResolveTimeoutMs);

        while (true)
        {
            var options = panel.Locator("mat-option");
            var count = await options.CountAsync().ConfigureAwait(false);

            for (var i = 0; i < count; i++)
            {
                var option = options.Nth(i);
                if (!await IsSelectableAsync(option).ConfigureAwait(false))
                {
                    continue;
                }

                var text = await ReadOptionTextAsync(option).ConfigureAwait(false);
                if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Page.WaitForTimeoutAsync(OptionPollIntervalMs).ConfigureAwait(false);
        }
    }

    private static async Task<List<string>> CollectOptionsAsync(ILocator panel)
    {
        var result = new List<string>();
        var options = panel.Locator("mat-option");
        var count = await options.CountAsync().ConfigureAwait(false);

        for (var i = 0; i < count; i++)
        {
            var option = options.Nth(i);
            if (!await IsSelectableAsync(option).ConfigureAwait(false))
            {
                continue;
            }

            var text = await ReadOptionTextAsync(option).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>Filters out disabled entries and the service rows Creatio appends, such as "Add new".</summary>
    private static async Task<bool> IsSelectableAsync(ILocator option)
    {
        var ariaDisabled = await SafeAttributeAsync(option, "aria-disabled").ConfigureAwait(false);
        if (string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var id = await SafeAttributeAsync(option, "id").ConfigureAwait(false);
        if (!string.IsNullOrEmpty(id)
            && (id.Equals("stickyBottomOption", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("addRecord_", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var text = await ReadOptionTextAsync(option).ConfigureAwait(false);
        return !text.TrimStart().StartsWith("Add new", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadOptionTextAsync(ILocator option)
    {
        try
        {
            var label = option.Locator(".mat-option-text span[crttextoverflowtitle]");
            if (await label.CountAsync().ConfigureAwait(false) > 0)
            {
                return (await label.First.InnerTextAsync().ConfigureAwait(false)).Trim();
            }

            var text = option.Locator(".mat-option-text");
            if (await text.CountAsync().ConfigureAwait(false) > 0)
            {
                return (await text.First.InnerTextAsync().ConfigureAwait(false)).Trim();
            }

            return (await option.InnerTextAsync().ConfigureAwait(false)).Trim();
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }
}
