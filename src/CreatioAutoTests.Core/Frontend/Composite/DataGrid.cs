using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend.Fields;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Frontend.Composite;

/// <summary>
/// Detail list or data grid (crt-data-grid).
///
/// A grid has two properties that Creatio configures independently, the way a field has required and
/// read-only: whether inline editing is on, and whether the grid sits inside an expansion panel.
/// Neither implies the other, so all four combinations occur.
/// </summary>
[UiElement("DataGrid", HostTag = "crt-data-grid", Options = typeof(DataGridOptions), LabeledByPanel = true)]
public sealed class DataGrid : FreedomElementBase, IEditableAware, IColumned, IRecordAddable
{
    /// <summary>
    /// The columns a person sees, and nothing else.
    ///
    /// Creatio gives every grid two structural cells that carry no data: a leading toolbar cell for
    /// the row controls, and a trailing auto-sized cell that absorbs the free width. Both announce
    /// themselves as column headers to assistive technology, so they have to be excluded by class
    /// rather than by position -- the leading one is missing from grids with no row controls.
    /// </summary>
    internal const string ColumnHeaderSelector =
        "[role=\"columnheader\"]" +
        ":not(.crt-toolbar-cell)" +
        ":not(.crt-data-table-header-autoresized-cell)";

    /// <summary>
    /// Every header cell, structural ones included. Filling a cell means matching it to its header
    /// by position, and positions only line up while both lists are complete.
    /// </summary>
    private const string AllColumnHeadersSelector = "[role=\"columnheader\"]";

    /// <summary>Body cells of one row, in the same order as the header cells.</summary>
    private const string CellSelector = "[role=\"gridcell\"], td";

    /// <summary>
    /// The button that appends a row, in the table's footer. Creatio also renders a hidden insert
    /// button on every row, for adding one in the middle; both announce themselves with the same
    /// localised label, so they are told apart by class and not by what they say.
    /// </summary>
    private const string CreateRowButtonSelector = ".crt-create-item-button button";

    /// <summary>The row Creatio has just opened for editing.</summary>
    private const string ActiveRowSelector = "[role=\"row\"].crt-data-table-row-active";

    private const int RowSettleMs = 600;
    private const int CellSettleMs = 300;

    /// <summary>
    /// How long a lookup cell is given to raise its autocomplete before the value is treated as
    /// plain text. Short on purpose: it is paid once per cell that turns out not to be a lookup.
    /// </summary>
    private const int AutocompleteProbeMs = 1500;

    /// <summary>
    /// How long a cell is given to open on a single click before a double click is tried. Short
    /// on purpose: it is paid once for every cell that needs the second click, which is most of them.
    /// </summary>
    private const int EditorProbeMs = 800;

    /// <summary>
    /// How long a lookup's options are given to arrive. The panel becomes visible before Creatio has
    /// loaded its rows, so reading it once reports an empty list for a value that is there.
    /// </summary>
    private const int OptionResolveTimeoutMs = 5000;
    private const int OptionPollIntervalMs = 100;

    private const string PanelTag = "crt-expansion-panel";
    private const string PanelHeaderSelector = ".expansion-panel-header";

    /// <summary>
    /// Marks a panel's toggle icon as collapsed. This is the class behind the icon colour a person
    /// sees, and reading it survives a theme change in a way that sampling the colour would not.
    /// </summary>
    private const string CollapsedIconSelector = ".toggle-icon.toggle-icon-collapsed";
    private const int PanelSettleMs = 1200;

    private bool _panelRevealed;

    private readonly DataGridOptions _options;

    /// <summary>Creates the grid against an open page.</summary>
    /// <param name="context">Page, definition and run settings for this element.</param>
    public DataGrid(ElementRuntimeContext context) : base(context)
    {
        _options = context.Definition.BindOptions<DataGridOptions>();
    }

    /// <summary>Columns declared in the page file, in the order they are expected on screen.</summary>
    public IReadOnlyList<string> ExpectedColumns => _options.Columns;

    /// <inheritdoc />
    protected override ILocator LabelLocator => Root.Locator(".crt-input-label, .crt-grid-caption");

    /// <summary>
    /// Finds the grid, opening the expansion panel it lives in when it is not on screen yet.
    ///
    /// A collapsed panel renders none of its contents, so the grid is genuinely absent until the
    /// panel is opened. The configured label is the panel's caption, which is how the panel is
    /// found: a grid has no caption of its own, so its label names the panel that holds it.
    /// </summary>
    /// <param name="timeoutMs">How long to wait for the grid once its panel is open.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    public override async Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (await base.ExistsAsync(timeoutMs, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (_panelRevealed || string.IsNullOrWhiteSpace(Label))
        {
            return false;
        }

        _panelRevealed = true;

        if (!await RevealPanelAsync(Label).ConfigureAwait(false))
        {
            return false;
        }

        return await base.ExistsAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether inline editing is switched on for this grid.
    ///
    /// Decided by the row Creatio adds to the table footer for appending a record, which it renders
    /// only for a grid that takes edits in place. Counting inputs would not do: an editable grid
    /// renders none until a row is actually being edited. Neither would the per-cell
    /// editing-disabled marker, which says only that one column is read-only -- an editable grid
    /// with a calculated or image column carries plenty of those.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when rows can be added and edited in place.</returns>
    /// <exception cref="InvalidOperationException">The grid is not on the open page.</exception>
    public async Task<bool> IsEditableAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        try
        {
            return await Root.Locator(CreateRowButtonSelector).CountAsync().ConfigureAwait(false) > 0;
        }
        catch (PlaywrightException)
        {
            // A grid that cannot be probed is reported as not editable; a write would fail loudly.
            return false;
        }
    }

    /// <summary>
    /// Reads the grid's column captions in display order.
    ///
    /// Read in one call rather than through a locator per header: a grid inside a panel is reached
    /// only after the panel has been opened and scrolled to, and a per-header round trip would give
    /// the table time to re-render underneath the enumeration.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The captions the grid renders, left to right.</returns>
    /// <exception cref="InvalidOperationException">The grid is not on the open page.</exception>
    public async Task<IReadOnlyList<string>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        try
        {
            var captions = await Root.EvaluateAsync<string[]>(
                @"(el, selector) => [...el.querySelectorAll(selector)]
                    .map(h => (h.innerText || h.textContent || '').trim())
                    .filter(t => t.length > 0)",
                ColumnHeaderSelector).ConfigureAwait(false);

            return captions ?? Array.Empty<string>();
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the columns of grid {Code} failed.", Code);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Adds a row and fills the named columns.
    ///
    /// The row is entered in place, which is the only way a grid takes a record without leaving the
    /// page. It reaches the database when the page is saved, exactly as a value written with 'set'
    /// does -- nothing here saves anything on its own.
    /// </summary>
    /// <param name="assignments">Columns to fill, in the order they are entered.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <exception cref="InvalidOperationException">
    /// The grid is absent, does not allow inline editing, or has no column of a given name.
    /// </exception>
    public async Task AddRecordAsync(
        IReadOnlyList<RecordAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        if (!await IsEditableAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Grid '" + Code + "' does not allow inline editing, so a record cannot be added to it in place.");
        }

        // Every column is checked before the row is opened: a half-filled row left behind on the
        // page would have to be discarded by hand before the case could be run again.
        var headers = await ReadHeaderPositionsAsync().ConfigureAwait(false);
        foreach (var assignment in assignments)
        {
            if (headers.ContainsKey(NormalizeLabel(assignment.Column)))
            {
                continue;
            }

            var rendered = await GetColumnsAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Grid '" + Code + "' has no column '" + assignment.Column + "'. On screen it shows: " +
                string.Join(", ", rendered.Select(c => "'" + c + "'")) + ".");
        }

        var create = Root.Locator(CreateRowButtonSelector);
        await create.First.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
        await create.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

        var row = Root.Locator(ActiveRowSelector);
        try
        {
            await row.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TimeoutMs
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            throw new InvalidOperationException(
                "Grid '" + Code + "' opened no row for editing after the add button was clicked.", ex);
        }

        await Page.WaitForTimeoutAsync(RowSettleMs).ConfigureAwait(false);

        foreach (var assignment in assignments)
        {
            await FillCellAsync(row.First, headers[NormalizeLabel(assignment.Column)], assignment)
                .ConfigureAwait(false);
        }

        Logger.LogDebug("Added a row to grid {Code} with {Count} value(s).", Code, assignments.Count);
    }

    /// <summary>
    /// Maps each header caption to its position among all header cells, structural ones included.
    ///
    /// Creatio puts no column identity on a body cell, so position is the only link between a
    /// caption and the cell to type into.
    /// </summary>
    private async Task<Dictionary<string, int>> ReadHeaderPositionsAsync()
    {
        var captions = await Root.EvaluateAsync<string[]>(
            @"(el, selector) => [...el.querySelectorAll(selector)]
                .map(h => (h.innerText || h.textContent || '').trim())",
            AllColumnHeadersSelector).ConfigureAwait(false) ?? Array.Empty<string>();

        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < captions.Length; i++)
        {
            var caption = NormalizeLabel(captions[i]);
            if (caption.Length > 0)
            {
                positions.TryAdd(caption, i);
            }
        }

        return positions;
    }

    /// <summary>
    /// Enters one value into the editing row.
    ///
    /// The kind of cell is read from what it renders rather than declared anywhere: a checkbox is
    /// toggled, a cell that raises an autocomplete is treated as a lookup, and everything else takes
    /// the text as typed. That keeps a grid's column types out of configuration, where they would be
    /// a second thing to keep in step with the page.
    /// </summary>
    private async Task FillCellAsync(ILocator row, int position, RecordAssignment assignment)
    {
        var cell = row.Locator(CellSelector).Nth(position);

        await cell.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
        await cell.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(CellSettleMs).ConfigureAwait(false);

        var checkbox = cell.Locator("input[type=\"checkbox\"]");
        if (await checkbox.CountAsync().ConfigureAwait(false) > 0)
        {
            await SetCheckboxAsync(checkbox.First, assignment).ConfigureAwait(false);
            return;
        }

        // Creatio opens most cell editors only on a double click. The first column and a checkbox
        // column are the exceptions that open on one, so a single click is tried first and the
        // second is spent only where it is needed.
        var input = cell.Locator("input, textarea").First;
        if (!await WaitForEditorAsync(input, EditorProbeMs).ConfigureAwait(false))
        {
            await cell.DblClickAsync(new LocatorDblClickOptions { Timeout = TimeoutMs }).ConfigureAwait(false);

            if (!await WaitForEditorAsync(input, TimeoutMs).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Column '" + assignment.Column + "' of grid '" + Code +
                    "' did not open for editing; Creatio renders it read-only in the list.");
            }
        }

        await input.FillAsync(assignment.Value).ConfigureAwait(false);

        if (await TryPickSuggestionAsync(assignment).ConfigureAwait(false))
        {
            return;
        }

        // Enter is what commits a text cell; without it the value stays in the open editor and is
        // dropped as soon as the next cell takes focus.
        await input.PressAsync("Enter").ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(CellSettleMs).ConfigureAwait(false);
    }

    /// <summary>Waits for a cell's editor to appear, reporting whether it did rather than throwing.</summary>
    private static async Task<bool> WaitForEditorAsync(ILocator input, int timeoutMs)
    {
        try
        {
            await input.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>Toggles a checkbox cell, accepting the same 'true'/'false' the Boolean field does.</summary>
    private async Task SetCheckboxAsync(ILocator checkbox, RecordAssignment assignment)
    {
        var value = assignment.Value.Trim().ToLowerInvariant();
        if (value != "true" && value != "false")
        {
            throw new InvalidOperationException(
                "Column '" + assignment.Column + "' of grid '" + Code +
                "' is a checkbox and expects 'true' or 'false' but received '" + assignment.Value + "'.");
        }

        await checkbox.SetCheckedAsync(value == "true").ConfigureAwait(false);
        await Page.WaitForTimeoutAsync(CellSettleMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Picks the typed value out of an autocomplete, when the cell raised one.
    ///
    /// A lookup cell cannot be committed by typing: the text has to be resolved to a record, and
    /// leaving it unresolved loses the value silently.
    /// </summary>
    /// <returns><see langword="true"/> when the cell was a lookup and the option was chosen.</returns>
    private async Task<bool> TryPickSuggestionAsync(RecordAssignment assignment)
    {
        var panel = Page.Locator(LookupField.PanelSelector);

        try
        {
            await panel.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = AutocompleteProbeMs
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            // No panel means the cell is not a lookup, which is the common case.
            return false;
        }

        var options = panel.First.Locator("mat-option");
        var wanted = NormalizeLabel(assignment.Value);
        var deadline = DateTime.UtcNow.AddMilliseconds(OptionResolveTimeoutMs);
        var offered = new List<string>();

        // Polled, not read once: the panel is shown before its rows arrive, so a single read reports
        // an empty list for a value that is about to appear.
        while (DateTime.UtcNow < deadline)
        {
            offered.Clear();
            var count = await options.CountAsync().ConfigureAwait(false);

            for (var i = 0; i < count; i++)
            {
                var text = NormalizeLabel(await options.Nth(i).InnerTextAsync().ConfigureAwait(false));
                offered.Add(text);

                if (string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    await options.Nth(i).ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs })
                        .ConfigureAwait(false);
                    await Page.WaitForTimeoutAsync(CellSettleMs).ConfigureAwait(false);
                    return true;
                }
            }

            await Page.WaitForTimeoutAsync(OptionPollIntervalMs).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Column '" + assignment.Column + "' of grid '" + Code + "' is a lookup and was not offered '" +
            assignment.Value + "'. Offered: " + (offered.Count == 0 ? "(none)" : string.Join(", ", offered)) + ".");
    }

    /// <summary>
    /// Name of the expansion panel this grid sits in, or null when it is placed directly on the page.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="InvalidOperationException">The grid is not on the open page.</exception>
    public async Task<string?> GetContainingPanelAsync(CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound();
        }

        try
        {
            return await Root.EvaluateAsync<string?>(
                "el => { const p = el.closest('" + PanelTag + "[element-name]');" +
                " return p ? p.getAttribute('element-name') : null; }").ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>
    /// The caption that names this grid on screen: the header of the panel it sits in. A grid placed
    /// directly on a page has none, and an empty caption tells 'check exists' to skip the comparison.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public override async Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var caption = await Root.EvaluateAsync<string?>(
                "el => { const p = el.closest('" + PanelTag + "[element-name]');" +
                " if (!p) return null;" +
                " const h = p.querySelector('" + PanelHeaderSelector + "');" +
                " return h ? h.textContent : null; }").ConfigureAwait(false);

            return caption is null ? null : NormalizeLabel(caption);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Reading the panel caption of grid {Code} failed.", Code);
            return null;
        }
    }

    /// <summary>
    /// Brings the panel captioned <paramref name="caption"/> into view, and opens it if it is closed.
    ///
    /// Two things keep a grid off the page. A collapsed panel renders nothing, and Creatio fills an
    /// expanded panel only once it scrolls into view, so a panel further down reports itself as open
    /// while holding nothing. Scrolling is tried first because, unlike a click, it cannot close a
    /// panel that is already open.
    /// </summary>
    /// <param name="caption">Panel caption, compared after the usual caption normalisation.</param>
    /// <returns><see langword="true"/> when a panel with that caption was found and revealed.</returns>
    private async Task<bool> RevealPanelAsync(string caption)
    {
        try
        {
            var panel = await FindPanelByCaptionAsync(caption).ConfigureAwait(false);
            if (panel is null)
            {
                Logger.LogDebug("No expansion panel is captioned '{Caption}' for grid {Code}.", caption, Code);
                return false;
            }

            var header = Page.Locator(PanelTag + "[element-name=\"" + panel + "\"] " + PanelHeaderSelector);
            await header.First.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
            await Page.WaitForTimeoutAsync(PanelSettleMs).ConfigureAwait(false);

            var collapsed = await Page.Locator(
                    PanelTag + "[element-name=\"" + panel + "\"] " + CollapsedIconSelector)
                .CountAsync().ConfigureAwait(false) > 0;

            if (collapsed)
            {
                await header.First.ClickAsync(new LocatorClickOptions { Timeout = TimeoutMs })
                    .ConfigureAwait(false);
                await Page.WaitForTimeoutAsync(PanelSettleMs).ConfigureAwait(false);
                Logger.LogDebug("Opened panel {Panel} to reach grid {Code}.", panel, Code);
            }

            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            Logger.LogDebug(ex, "Revealing the panel captioned '{Caption}' failed.", caption);
            return false;
        }
    }

    /// <summary>Finds an expansion panel by its header caption, whatever its state.</summary>
    /// <param name="caption">Caption to match.</param>
    private Task<string?> FindPanelByCaptionAsync(string caption) =>
        Page.EvaluateAsync<string?>(
            @"(wanted) => {
                const norm = t => (t || '').trim().replace(/[\s*:]+$/, '').trim();
                const match = [...document.querySelectorAll('" + PanelTag + @"[element-name]')]
                    .find(p => {
                        const h = p.querySelector('" + PanelHeaderSelector + @"');
                        return h && norm(h.textContent) === norm(wanted);
                    });
                return match ? match.getAttribute('element-name') : null;
            }", caption);
}
