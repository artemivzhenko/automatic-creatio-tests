using System.Globalization;
using System.Text.Json;
using CreatioAutoTests.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Classic.Composite;

/// <summary>
/// Classic detail: the grid of related records on a page, addressed by the detail's schema name.
/// Everything inside a detail is prefixed by that schema, not by the page:
/// <c>{Detail}Container</c> holds it, <c>{Detail}DetailControlGroup-caption</c> names it,
/// <c>grid-{Detail}DataGridGrid-wrap</c> is the grid with <c>.grid-captions</c> and one
/// <c>.grid-listed-row</c> per record, and the grid component <c>{Detail}DataGridGrid</c> holds the
/// loaded collection.
///
/// The detail's value is its row count, so <c>check-value Services eq "3"</c> and <c>gt "0"</c>
/// read naturally; it cannot be set. Rows are addressed by their cell values
/// (<c>check-row</c>, <c>select-row</c>, <c>delete-row</c>). A detail whose grid is a
/// <c>ConfigurationGrid</c> takes edits in place: <c>check X is editable</c> reports it and
/// <c>add-record</c> fills the row it opens. Its buttons are ordinary <c>Button</c> elements with
/// the <c>detail</c> option.
///
/// A detail saves its own records: a row added or deleted here is in the database at once,
/// whatever the page's Save button does afterwards.
/// </summary>
[UiElement("Detail", Generation = UiGeneration.ClassicUI, Controls = new[] { "Detail" },
    ValueKind = ValueKind.Number, Options = typeof(ClassicDetailOptions))]
public sealed class Detail : ClassicElementBase, IValueElement, IColumned, IEditableAware, IRowed, IRecordAddable
{
    private const string RowSelector = ".grid-listed-row, .grid-tiled-row";
    private const string CaptionSelector = ".grid-captions [data-item-marker]";
    private const string InlineEditingClass = "configuration-grid";
    private const string ActiveRowSelector = ".configuration-grid-row-active";
    private const string RowActionsSelector = ".configuration-grid-row-actions";
    private const string MessageBoxSelector = ".ts-messagebox-box";
    private const int RowWaitMs = 8000;
    private const int ListWaitMs = 8000;
    private const int PollMs = 150;
    private const int SettleMs = 800;

    private readonly ClassicDetailOptions _options;

    /// <summary>Initializes a Classic detail from its options.</summary>
    public Detail(ElementRuntimeContext context) : this(context, context.Definition.BindOptions<ClassicDetailOptions>())
    {
    }

    private Detail(ElementRuntimeContext context, ClassicDetailOptions options) : base(context, options)
    {
        _options = options;
    }

    /// <summary>Column captions declared in configuration, in the expected left-to-right order.</summary>
    public IReadOnlyList<string> ExpectedColumns => _options.Columns;

    /// <summary>The detail's container, or the grid itself for a list that has no detail container (a section).</summary>
    protected override string BuildSelector() =>
        ClassicIds.Selector(ClassicIds.DetailContainer(Name)) + ", " + ClassicIds.Selector(ClassicIds.DetailGrid(Name));

    /// <inheritdoc />
    protected override string LocatorDescription => ClassicIds.DetailContainer(Name);

    /// <summary>The caption node of the detail's control group.</summary>
    protected override ILocator LabelLocator => Page.Locator(ClassicIds.Selector(ClassicIds.DetailCaption(Name)));

    private ILocator Grid => Page.Locator(ClassicIds.Selector(ClassicIds.DetailGrid(Name)));

    /// <summary>Caption from text content: Classic upper-cases detail captions in CSS.</summary>
    public override async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        var text = await TextContentAsync(LabelLocator).ConfigureAwait(false);
        return text is null ? null : NormalizeLabel(text);
    }

    /// <summary>
    /// The number of records the detail holds, as text. Read from the grid component's collection,
    /// which counts every loaded row whether or not it is scrolled into view; the DOM rows are the
    /// fallback for a grid that exposes no collection.
    /// </summary>
    public async Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var count = await EvaluateOnComponentAsync(
            ClassicIds.DetailGridComponent(Name),
            "c => (c.collection && typeof c.collection.getCount === 'function') ? c.collection.getCount() : null",
            null).ConfigureAwait(false);

        if (count.ValueKind == JsonValueKind.Number)
        {
            return count.GetInt32().ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            var rows = await Grid.Locator(RowSelector).CountAsync().ConfigureAwait(false);
            return rows.ToString(CultureInfo.InvariantCulture);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Counting the rows of detail {Code} failed.", Code);
            return "0";
        }
    }

    /// <summary>A detail's row count is what the page shows; records are added with 'add-record' or its buttons.</summary>
    public Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "'" + Code + "' is a detail: its value is the number of rows and cannot be set. Use 'add-record' or its buttons.");

    /// <summary>Reads the column captions the grid renders, left to right.</summary>
    public async Task<IReadOnlyList<string>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        try
        {
            var captions = await Grid.EvaluateAsync<string[]>(
                @"(el, selector) => [...el.querySelectorAll(selector)]
                    .map(c => (c.getAttribute('data-item-marker') || c.textContent || '').trim())
                    .filter(t => t.length > 0)",
                CaptionSelector).ConfigureAwait(false);

            if (captions is { Length: > 0 })
            {
                return captions;
            }
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the columns of detail {Code} failed.", Code);
        }

        // An empty grid renders no header row at all, only "no data"; the grid component still
        // knows its columns, and in the same order the header would show them.
        var configured = await EvaluateOnComponentAsync(
            ClassicIds.DetailGridComponent(Name),
            "c => (c.captionsConfig || []).map(x => x && (x.name || x.caption) || '').filter(Boolean)",
            null).ConfigureAwait(false);

        return configured.ValueKind == JsonValueKind.Array
            ? configured.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();
    }

    /// <summary>Whether the grid takes edits in place: Classic renders such a detail as a configuration grid.</summary>
    public async Task<bool> IsEditableAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var classes = await SafeAttributeAsync(Grid, "class").ConfigureAwait(false) ?? string.Empty;
        return classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(InlineEditingClass, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Text of every cell of every rendered row, for diagnostics and row assertions.</summary>
    public async Task<IReadOnlyList<IReadOnlyList<string>>> GetRowsAsync()
    {
        try
        {
            var rows = await Grid.EvaluateAsync<string[][]>(
                @"(el, selector) => [...el.querySelectorAll(selector)]
                    .map(r => [...r.querySelectorAll(':scope > [class^=""grid-cols-""]')].map(c => (c.textContent || '').trim()))",
                RowSelector).ConfigureAwait(false);

            return rows ?? Array.Empty<string[]>();
        }
        catch (PlaywrightException)
        {
            return Array.Empty<string[]>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasRowAsync(IReadOnlyList<RecordAssignment> cells, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        return await FindRowIdAsync(cells).ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    public async Task SelectRowAsync(IReadOnlyList<RecordAssignment> cells, CancellationToken cancellationToken = default)
    {
        var row = await RequireRowAsync(cells, cancellationToken).ConfigureAwait(false);
        await row.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(SettleMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the row through the grid's own action. A configuration grid shows its actions beside
    /// the selected row, <c>remove</c> among them; the confirmation Classic asks is answered yes.
    /// The record is gone from the database when this returns.
    /// </summary>
    public async Task DeleteRowAsync(IReadOnlyList<RecordAssignment> cells, CancellationToken cancellationToken = default)
    {
        var row = await RequireRowAsync(cells, cancellationToken).ConfigureAwait(false);
        var before = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);

        await row.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(SettleMs).ConfigureAwait(false);

        var remove = Grid.Locator(RowActionsSelector + " [data-item-marker=\"remove\"]");
        if (await remove.CountAsync().ConfigureAwait(false) == 0 || !await remove.First.IsVisibleAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Detail '" + Code + "' offers no delete action beside the selected row; only a detail that edits in place can delete here.");
        }

        await remove.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await AnswerMessageBoxAsync(yes: true).ConfigureAwait(false);
        await WaitForRowCountChangeAsync(before, cancellationToken).ConfigureAwait(false);

        if (await FindRowIdAsync(cells).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("Row " + Describe(cells) + " of detail '" + Code + "' is still there after deleting it.");
        }
    }

    /// <summary>
    /// Adds a record in place: opens the new row with the detail's add button, fills the named
    /// cells through the editors the row renders and saves the row with its own save action.
    /// </summary>
    public async Task AddRecordAsync(IReadOnlyList<RecordAssignment> assignments, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (!await IsEditableAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Detail '" + Code + "' does not edit in place; its add button opens a page, so use 'click' and 'on' instead.");
        }

        var columns = await GetColumnsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var assignment in assignments)
        {
            if (!columns.Contains(assignment.Column, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Detail '" + Code + "' has no column '" + assignment.Column + "'. Its columns are: " + string.Join(", ", columns) + ".");
            }
        }

        var before = await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false);

        var add = Page.Locator(ClassicIds.Selector(ClassicIds.ButtonWrapper(Name, "AddRecordButton")) + ", " +
                               ClassicIds.Selector(ClassicIds.ButtonText(Name, "AddRecordButton")));
        await add.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        var activeRow = Grid.Locator(ActiveRowSelector);
        try
        {
            await activeRow.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = RowWaitMs }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Detail '" + Code + "' opened no row to edit after its add button was clicked.");
        }

        foreach (var assignment in assignments)
        {
            await FillCellAsync(activeRow.First, columns, assignment).ConfigureAwait(false);
        }

        var save = Grid.Locator(RowActionsSelector + " [data-item-marker=\"save\"]");
        await save.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        var message = await AnswerMessageBoxAsync(yes: false).ConfigureAwait(false);
        if (message is not null)
        {
            throw new InvalidOperationException("Detail '" + Code + "' did not save the new row. Creatio reported: " + message);
        }

        await WaitForRowCountChangeAsync(before, cancellationToken).ConfigureAwait(false);

        if (await FindRowIdAsync(assignments).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException(
                "Detail '" + Code + "' saved a row, but none shows " + Describe(assignments) + ". Rows: " + await DescribeRowsAsync().ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Fills one cell of the row being edited. The editor is a Classic control whose id is the
    /// column name plus its control class (<c>QSTrademarkLookupEdit-el</c>), and the cell carries
    /// the column name in <c>column-name</c>; a lookup is driven through its list, a checkbox by a
    /// click, everything else by typing.
    /// </summary>
    private async Task FillCellAsync(ILocator row, IReadOnlyList<string> columns, RecordAssignment assignment)
    {
        var index = columns.ToList().FindIndex(c => string.Equals(c, assignment.Column, StringComparison.OrdinalIgnoreCase));
        var cell = row.Locator(":scope > [class^=\"grid-cols-\"]").Nth(index);
        var columnName = await SafeAttributeAsync(cell, "column-name").ConfigureAwait(false);

        var editor = string.IsNullOrWhiteSpace(columnName)
            ? cell.Locator("input, textarea")
            : Page.Locator("[id^=\"" + columnName + "\"][id$=\"-el\"]").Filter(new LocatorFilterOptions { HasNot = Page.Locator("[id$=\"-link-el\"]") });

        if (await editor.CountAsync().ConfigureAwait(false) == 0)
        {
            // Some cells open their editor only once clicked.
            await cell.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(400).ConfigureAwait(false);
        }

        if (await editor.CountAsync().ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "Column '" + assignment.Column + "' of detail '" + Code + "' did not open for editing.");
        }

        var input = editor.First;
        var id = await SafeAttributeAsync(input, "id").ConfigureAwait(false) ?? string.Empty;

        if (id.Contains("CheckBoxEdit", StringComparison.Ordinal))
        {
            var want = assignment.Value is "true";
            if (await input.IsCheckedAsync().ConfigureAwait(false) != want)
            {
                await input.ClickAsync(new LocatorClickOptions { Force = true, Timeout = TimeoutMs }).ConfigureAwait(false);
            }

            return;
        }

        await input.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await input.FillAsync(assignment.Value).ConfigureAwait(false);

        if (id.Contains("LookupEdit", StringComparison.Ordinal) || id.Contains("ComboBoxEdit", StringComparison.Ordinal))
        {
            await input.DispatchEventAsync("keyup", new Dictionary<string, object>
            {
                ["key"] = "a", ["code"] = "KeyA", ["keyCode"] = 65, ["which"] = 65, ["bubbles"] = true, ["cancelable"] = true,
            }).ConfigureAwait(false);

            var item = await WaitForListItemAsync(assignment.Value).ConfigureAwait(false)
                       ?? throw new InvalidOperationException(
                           "Option '" + assignment.Value + "' was not offered for column '" + assignment.Column + "' of detail '" + Code + "'.");

            await item.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(300).ConfigureAwait(false);
            return;
        }

        if (id.Contains("DateEdit", StringComparison.Ordinal) || id.Contains("TimeEdit", StringComparison.Ordinal))
        {
            // The DSL speaks ISO; the control wants the user's culture, which the page can render.
            var formatted = await Page.EvaluateAsync<string>(
                @"([iso, time]) => { const d = new Date(iso); const cs = Terrasoft.Resources.CultureSettings; return Ext.Date.format(d, time ? cs.timeFormat : cs.dateFormat); }",
                new object[] { assignment.Value, id.Contains("TimeEdit", StringComparison.Ordinal) }).ConfigureAwait(false);
            await input.FillAsync(formatted).ConfigureAwait(false);
        }

        await input.PressAsync("Tab").ConfigureAwait(false);
    }

    private async Task<ILocator?> WaitForListItemAsync(string value)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ListWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            var items = Page.Locator("div.listview li[data-value]");
            var count = await items.CountAsync().ConfigureAwait(false);
            for (var i = 0; i < count; i++)
            {
                var item = items.Nth(i);
                var text = await item.GetAttributeAsync("data-item-marker").ConfigureAwait(false) ?? await item.InnerTextAsync().ConfigureAwait(false);
                if (string.Equals(text.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase) && await item.IsVisibleAsync().ConfigureAwait(false))
                {
                    return item;
                }
            }

            await Page.WaitForTimeoutAsync(PollMs).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Id of the first rendered row whose named cells all match, or null.</summary>
    private async Task<string?> FindRowIdAsync(IReadOnlyList<RecordAssignment> cells)
    {
        try
        {
            var pairs = cells.Select(c => new[] { c.Column, c.Value }).ToArray();
            var columns = (await GetColumnsAsync().ConfigureAwait(false)).Select(c => c.ToLowerInvariant()).ToArray();
            var result = await Grid.EvaluateAsync<string?>(
                @"(el, [rowSelector, captions, pairs]) => {
                    const indexes = pairs.map(([col]) => captions.indexOf(col.trim().toLowerCase()));
                    if (indexes.some(i => i < 0)) { return null; }
                    for (const row of el.querySelectorAll(rowSelector)) {
                        const texts = [...row.querySelectorAll(':scope > [class^=""grid-cols-""]')].map(c => (c.textContent || '').trim().toLowerCase());
                        if (pairs.every(([, value], k) => texts[indexes[k]] === value.trim().toLowerCase())) { return row.id || ''; }
                    }
                    return null;
                }",
                new object[] { RowSelector, columns, pairs }).ConfigureAwait(false);

            return result;
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Searching the rows of detail {Code} failed.", Code);
            return null;
        }
    }

    private async Task<ILocator> RequireRowAsync(IReadOnlyList<RecordAssignment> cells, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        var id = await FindRowIdAsync(cells).ConfigureAwait(false);
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException(
                "Detail '" + Code + "' has no row " + Describe(cells) + ". Rows: " + await DescribeRowsAsync().ConfigureAwait(false));
        }

        return Page.Locator(ClassicIds.Selector(id));
    }

    /// <summary>Clicks a message box away: its first button for yes, else whatever single button it has; returns its caption.</summary>
    private async Task<string?> AnswerMessageBoxAsync(bool yes)
    {
        var box = Page.Locator(MessageBoxSelector);
        try
        {
            await box.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 1500 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (PlaywrightException)
        {
            return null;
        }

        var caption = (await box.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
        var buttons = box.First.Locator(".t-btn-wrapper[data-item-marker]");
        var count = await buttons.CountAsync().ConfigureAwait(false);
        if (count > 0)
        {
            await buttons.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        }

        // Two buttons was a question, answered yes; one button was a statement worth returning.
        return count >= 2 && yes ? null : caption.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private async Task WaitForRowCountChangeAsync(string? before, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(RowWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await Grid.Locator(ActiveRowSelector).CountAsync().ConfigureAwait(false) == 0 &&
                !string.Equals(await GetValueAsStringAsync(cancellationToken).ConfigureAwait(false), before, StringComparison.Ordinal))
            {
                await Page.WaitForTimeoutAsync(300).ConfigureAwait(false);
                return;
            }

            await Page.WaitForTimeoutAsync(PollMs).ConfigureAwait(false);
        }
    }

    private async Task<string> DescribeRowsAsync()
    {
        var rows = await GetRowsAsync().ConfigureAwait(false);
        return rows.Count == 0 ? "(none)" : string.Join(" | ", rows.Take(10).Select(r => string.Join(" / ", r)));
    }

    private static string Describe(IReadOnlyList<RecordAssignment> cells) => string.Join("; ", cells);
}
