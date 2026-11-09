using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Fields;

/// <summary>
/// Classic lookup, in both of its shapes: the drop-down list (<c>ComboBoxEdit</c>) and the
/// searchable lookup with a selection window (<c>LookupEdit</c>). Typing into either filters a list
/// that Classic appends to the end of <c>body</c> as <c>div.listview</c>, one <c>li[data-value]</c>
/// per match, and tags the list with the element's name in <c>data-item-marker</c>. Writing a value
/// means typing it and clicking that item; the committed value is then read back from the model.
/// </summary>
[UiElement("Lookup", Generation = UiGeneration.ClassicUI, Controls = new[] { "LookupEdit", "ComboBoxEdit" },
    ValueKind = ValueKind.Lookup, Options = typeof(ClassicElementOptions))]
public sealed class LookupField : ClassicFieldBase
{
    private const int ListWaitMs = 8000;
    private const int ListPollMs = 150;
    private const int SettleAfterPickMs = 300;

    /// <summary>Initializes a Classic lookup.</summary>
    public LookupField(ElementRuntimeContext context) : base(context)
    {
    }

    /// <summary>The list Classic opened for this element: the marker starts with the schema name of the element.</summary>
    private ILocator List => Page.Locator("div.listview[data-item-marker^=\"" + Name + " \"], div.listview[data-item-marker=\"" + Name + "\"]");

    /// <summary>Reads the committed display value from the model; the typed text is never the answer.</summary>
    public override async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var model = await ReadModelValueAsync().ConfigureAwait(false);
        return model.Kind == "lookup" ? model.Text : string.Empty;
    }

    /// <summary>Types the value, waits for the list, picks the exact match and confirms the model took it.</summary>
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
        var input = ValueLocator;

        await input.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await input.FillAsync(target).ConfigureAwait(false);

        // Classic starts the search on keyup. Playwright types non-Latin text through insertText,
        // which raises input events only, so the keyup has to be raised by hand; the key itself is
        // irrelevant, the handler only reads the input's text.
        await input.DispatchEventAsync("keyup", new Dictionary<string, object>
        {
            ["key"] = "a",
            ["code"] = "KeyA",
            ["keyCode"] = 65,
            ["which"] = 65,
            ["bubbles"] = true,
            ["cancelable"] = true,
        }).ConfigureAwait(false);

        var item = await ResolveItemAsync(target).ConfigureAwait(false);
        if (item is null)
        {
            var offered = await CollectOfferedAsync().ConfigureAwait(false);
            await input.PressAsync("Escape").ConfigureAwait(false);
            throw new InvalidOperationException(
                "Option '" + target + "' was not offered by lookup '" + Code + "'. Offered: " +
                (offered.Count == 0 ? "(none)" : string.Join(", ", offered)) + ".");
        }

        await item.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(SettleAfterPickMs).ConfigureAwait(false);

        var committed = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(committed, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Lookup '" + Code + "' holds '" + committed + "' after picking '" + target + "'.");
        }
    }

    /// <summary>Empties the text and tabs out; Classic clears the bound column when the text is gone.</summary>
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var input = ValueLocator;
        await input.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await input.FillAsync(string.Empty).ConfigureAwait(false);
        await input.PressAsync("Escape").ConfigureAwait(false);
        await input.PressAsync("Tab").ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(SettleAfterPickMs).ConfigureAwait(false);

        var model = await ReadModelValueAsync().ConfigureAwait(false);
        if (!model.IsEmpty)
        {
            throw new InvalidOperationException(
                "Lookup '" + Code + "' still holds '" + model.Text + "' after clearing.");
        }
    }

    /// <summary>Polls the list until an item with the wanted caption is rendered or the budget runs out.</summary>
    private async Task<ILocator?> ResolveItemAsync(string target)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ListWaitMs);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var items = List.Locator("li[data-value]");
                var count = await items.CountAsync().ConfigureAwait(false);
                for (var i = 0; i < count; i++)
                {
                    var item = items.Nth(i);
                    var marker = await item.GetAttributeAsync("data-item-marker").ConfigureAwait(false);
                    var text = string.IsNullOrEmpty(marker) ? await item.InnerTextAsync().ConfigureAwait(false) : marker;
                    if (string.Equals(text.Trim(), target, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }
            catch (PlaywrightException ex)
            {
                Logger.LogDebug(ex, "Reading the lookup list of {Code} failed; retrying.", Code);
            }

            await Page.WaitForTimeoutAsync(ListPollMs).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<List<string>> CollectOfferedAsync()
    {
        var result = new List<string>();
        try
        {
            var items = List.Locator("li[data-value]");
            var count = await items.CountAsync().ConfigureAwait(false);
            for (var i = 0; i < count; i++)
            {
                var text = await items.Nth(i).InnerTextAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text.Trim());
                }
            }
        }
        catch (PlaywrightException)
        {
        }

        return result;
    }
}
