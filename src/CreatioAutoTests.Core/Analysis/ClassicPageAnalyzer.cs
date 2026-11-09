using System.Text.Json;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Classic;
using CreatioAutoTests.Frontend.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace CreatioAutoTests.Analysis;

/// <summary>
/// Reads an open Classic UI page and reports the controls on it.
///
/// Where the Freedom analyser walks host tags, this one asks the page itself: every control is an
/// Ext component reachable by its id, and through it the page's view model says which column the
/// control binds, what the column is called and whether it is required. That is what makes the
/// codes readable (<c>QSTypeCargo</c> rather than <c>LOOKUPceebb14a-...</c>) and the types certain:
/// a <c>DateEdit</c> on a DateTime column is half of a <c>DateTime</c>, not a <c>Date</c>.
///
/// The set of controls it recognises comes from the registry: a Classic element type declares the
/// control classes it covers, and that declaration is what maps a control back to a type here.
/// </summary>
public sealed class ClassicPageAnalyzer
{
    private const int TabClickTimeoutMs = 10_000;
    private const int TabSettleMs = 1500;
    private const int SettlePollMs = 500;
    private const int SettleMaxMs = 15_000;

    /// <summary>Identical counts in a row that mean a tab has finished rendering. Details load in their own requests after the fields.</summary>
    private const int StablePolls = 2;

    /// <summary>
    /// Everything a tab renders that analysis cares about: controls (<c>-el</c>), buttons
    /// (<c>-wrapperEl</c>) and detail grids (<c>grid-...-wrap</c>). Counting only controls let a
    /// detail that arrived a second later be missed on one run and found on the next.
    /// </summary>
    private const string RenderedNodesSelector = "[id$=\"-el\"], [id$=\"-wrapperEl\"], [id^=\"grid-\"][id$=\"-wrap\"]";

    /// <summary>Terrasoft.DataValueType values the date family is told apart by.</summary>
    private const int DateTimeValueType = 7;
    private const int DateValueType = 8;
    private const int TimeValueType = 9;

    /// <summary>Codes of the elements that exist once per page and have no schema name of their own.</summary>
    internal const string TabPanelCode = "Tabs";
    internal const string StageBarCode = "Stage";

    private readonly IReadOnlyDictionary<string, string> _typeByControl;
    private readonly ILogger<ClassicPageAnalyzer> _logger;

    /// <summary>Initializes control-class discovery from the Classic registrations and analysis diagnostics.</summary>
    public ClassicPageAnalyzer(UiElementRegistry registry, ILogger<ClassicPageAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // The more specific type claims a control: DateEdit is Date's before it is DateTime's, which
        // declares it together with TimeEdit. Ordering by how many controls a type covers does that
        // without naming any type here.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var registration in registry.RegistrationsOf(UiGeneration.ClassicUI)
                     .OrderBy(r => r.Controls.Count).ThenBy(r => r.TypeName, StringComparer.Ordinal))
        {
            // Only data fields are found through their control class; tabs, buttons, details and
            // the stage bar are found through their own structures below.
            if (registration.ValueKind is ValueKind.None or ValueKind.Tab or ValueKind.Stage ||
                string.Equals(registration.TypeName, "Detail", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var control in registration.Controls)
            {
                map.TryAdd(control, registration.TypeName);
            }
        }

        _typeByControl = map;
    }

    /// <summary>
    /// Extracts controls from an open Classic page and, when requested, visits every tab, because
    /// a tab renders its content only when first opened.
    /// </summary>
    /// <param name="page">The open browser page.</param>
    /// <param name="pageName">Name the page gets in the DSL.</param>
    /// <param name="url">Url template for the page file.</param>
    /// <param name="recordId">Record the page was opened for, if any.</param>
    /// <param name="schema">Page schema name; detected from the page when null.</param>
    /// <param name="includeHidden">Whether to record controls that are not currently visible.</param>
    /// <param name="walkTabs">Whether to open every tab and analyse each.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    public async Task<PageAnalysis> AnalyzeAsync(
        IPage page,
        string pageName,
        string url,
        string? recordId,
        string? schema,
        bool includeHidden = false,
        bool walkTabs = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        schema = string.IsNullOrWhiteSpace(schema) ? await DetectSchemaAsync(page).ConfigureAwait(false) : schema.Trim();
        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new InvalidOperationException(
                "The Classic page schema could not be determined: give an address of the form " +
                PagePath.ClassicCardRoute + "<Schema>/edit/<recordId>.");
        }

        var collector = new ClassicSnapshotCollector(_typeByControl, includeHidden);

        var snapshot = await ReadAsync(page, schema).ConfigureAwait(false);
        collector.Add(snapshot);

        if (walkTabs)
        {
            await WalkTabsAsync(page, schema, snapshot, collector, cancellationToken).ConfigureAwait(false);
        }

        collector.AddStandardPageButtons();

        _logger.LogInformation(
            "Analysed {Page} ({Schema}): {Known} addressable control(s), {Unknown} without an element type.",
            pageName, schema, collector.Elements.Count, collector.Unknown.Count);

        return new PageAnalysis
        {
            PageName = pageName,
            Url = url,
            RecordId = recordId,
            Generation = UiGeneration.ClassicUI,
            Schema = schema,
            Elements = collector.Elements
                .OrderBy(e => e.Type, StringComparer.Ordinal)
                .ThenBy(e => e.Code, StringComparer.Ordinal)
                .ToArray(),
            UnknownControls = collector.Unknown
                .DistinctBy(u => (u.HostTag, u.Code))
                .OrderBy(u => u.HostTag, StringComparer.Ordinal)
                .ThenBy(u => u.Code, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    /// <summary>
    /// Opens every tab the panel lists and analyses each. The tab the page arrived on is restored
    /// at the end. A visited tab's content stays in the DOM hidden, so controls are recorded only
    /// while their tab is the open one unless hidden ones were asked for.
    /// </summary>
    private async Task WalkTabsAsync(
        IPage page,
        string schema,
        JsonElement first,
        ClassicSnapshotCollector collector,
        CancellationToken cancellationToken)
    {
        if (!first.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var items = tabs.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        var headers = page.Locator(ClassicIds.Selector(ClassicIds.TabItems(schema, Text(tabs, "panel"))));
        var originalIndex = items.FirstOrDefault(i => Flag(i, "active")).TryGetProperty("index", out var o) && o.ValueKind == JsonValueKind.Number
            ? o.GetInt32()
            : -1;

        foreach (var item in items)
        {
            if (Flag(item, "active") || !Flag(item, "visible") ||
                !item.TryGetProperty("index", out var indexElement) || indexElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var index = indexElement.GetInt32();
            var name = Text(item, "name");

            if (!await OpenTabAsync(page, headers, index).ConfigureAwait(false))
            {
                _logger.LogWarning("Tab {Tab} could not be opened; its controls were not analysed.", name);
                continue;
            }

            var snapshot = await ReadAsync(page, schema).ConfigureAwait(false);
            var before = collector.Elements.Count;
            collector.Add(snapshot);
            _logger.LogInformation("Tab {Tab}: {Count} new control(s).", name, collector.Elements.Count - before);

            cancellationToken.ThrowIfCancellationRequested();
        }

        if (originalIndex >= 0)
        {
            await OpenTabAsync(page, headers, originalIndex).ConfigureAwait(false);
        }
    }

    /// <summary>Clicks a tab header and waits for the page to stop adding controls.</summary>
    private async Task<bool> OpenTabAsync(IPage page, ILocator headers, int index)
    {
        try
        {
            await headers.Locator("li[data-item-index=\"" + index + "\"]")
                .ClickAsync(new LocatorClickOptions { Timeout = TabClickTimeoutMs }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _logger.LogDebug(ex, "Clicking tab header {Index} failed.", index);
            return false;
        }

        await page.WaitForTimeoutAsync(TabSettleMs).ConfigureAwait(false);
        await WaitForSettledAsync(page).ConfigureAwait(false);
        return true;
    }

    /// <summary>Waits until the number of rendered controls, buttons and grids stops changing.</summary>
    private static async Task WaitForSettledAsync(IPage page)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SettleMaxMs);
        var previous = -1;
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            int current;
            try
            {
                current = await page.EvaluateAsync<int>("s => document.querySelectorAll(s).length", RenderedNodesSelector).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                return;
            }

            if (current == previous)
            {
                if (++stable >= StablePolls)
                {
                    return;
                }
            }
            else
            {
                stable = 0;
            }

            previous = current;
            await page.WaitForTimeoutAsync(SettlePollMs).ConfigureAwait(false);
        }
    }

    /// <summary>The schema the page's own tab panel is named after, when the address did not say.</summary>
    private static async Task<string?> DetectSchemaAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string?>(@"() => {
                if (!(window.Ext && Ext.ComponentManager)) { return null; }
                const all = Object.values(Ext.ComponentManager.all.map || {});
                const panel = all.find(c => c.$className === 'Terrasoft.controls.TabPanel'
                    && /TabsTabPanel$/.test(c.id || '') && !(c.id || '').startsWith('SectionActionsDashboard'));
                return panel ? panel.id.replace(/TabsTabPanel$/, '') : null;
            }").ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>Runs the extraction script against the page as it is now.</summary>
    private static Task<JsonElement> ReadAsync(IPage page, string schema) =>
        page.EvaluateAsync<JsonElement>(Script, schema);

    /// <summary>
    /// The extraction, run in the page. Everything the mapping needs is gathered here in one call,
    /// because a hundred round trips would give the page time to change underneath them.
    /// </summary>
    private const string Script = @"(schema) => {
        const vis = e => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; };
        const text = e => (e && e.textContent || '').trim();
        const cmp = id => { try { return Ext.getCmp(id) || null; } catch (e) { return null; } };
        const toArray = col => {
            if (!col) { return []; }
            if (Array.isArray(col)) { return col; }
            if (typeof col.getItems === 'function') { return col.getItems() || []; }
            if (Array.isArray(col.items)) { return col.items; }
            if (col.map && typeof col.map === 'object') { return Object.values(col.map); }
            return [];
        };
        const buttonsUnder = (root, prefix) => {
            const result = [];
            const seen = new Set();
            for (const el of root.querySelectorAll('[id^=""' + prefix + '""][id$=""Button-wrapperEl""], [id^=""' + prefix + '""][id$=""Button-textEl""]')) {
                const rest = el.id.slice(prefix.length);
                const name = rest.replace(/Button-(wrapperEl|textEl)$/, '');
                if (!name || seen.has(name)) { continue; }
                seen.add(name);
                const textEl = document.getElementById(prefix + name + 'Button-textEl');
                result.push({ name, label: text(textEl), visible: vis(el) });
            }
            return result;
        };

        const controls = [];
        const unknown = [];
        for (const el of document.querySelectorAll('[id^=""' + schema + '""][id$=""-el""]')) {
            const id = el.id.slice(0, -3);
            const c = cmp(id);
            if (!c || !c.$className) { continue; }
            const control = c.$className.split('.').pop();
            const rest = id.slice(schema.length);
            if (!rest.endsWith(control)) { continue; }
            const name = rest.slice(0, rest.length - control.length);
            if (!name) { continue; }
            const b = c.bindings || {};
            const column = (b.value && b.value.modelItem) || (b.checked && b.checked.modelItem) || null;
            const def = (c.model && c.model.columns && column) ? c.model.columns[column] : null;
            const labelEl = document.getElementById(schema + name + 'Label');
            const wrap = document.getElementById(id + '-wrap');
            controls.push({
                name, control, column,
                dataValueType: def ? def.dataValueType : null,
                label: text(labelEl) || (def && def.caption) || '',
                required: !!(labelEl && labelEl.className.indexOf('t-label-is-required') >= 0) || c.isRequired === true || !!(def && def.isRequired),
                readonly: !!(wrap && wrap.className.indexOf('base-edit-disabled') >= 0) || el.readOnly === true || c.enabled === false,
                visible: vis(el) || vis(wrap)
            });
        }

        const buttons = buttonsUnder(document, schema);

        const details = [];
        for (const grid of document.querySelectorAll('[id^=""grid-""][id$=""DataGridGrid-wrap""]')) {
            const detail = grid.id.slice('grid-'.length, grid.id.length - 'DataGridGrid-wrap'.length);
            const container = document.getElementById(detail + 'Container');
            const tools = document.getElementById(detail + 'DetailControlGroup-tools');
            details.push({
                schema: detail,
                caption: text(document.getElementById(detail + 'DetailControlGroup-caption')),
                columns: [...grid.querySelectorAll('.grid-captions [data-item-marker]')].map(c => c.getAttribute('data-item-marker') || text(c)).filter(Boolean),
                editable: grid.classList.contains('configuration-grid'),
                visible: vis(container) || vis(grid),
                buttons: tools ? buttonsUnder(tools, detail) : []
            });
        }

        let tabs = null;
        const panel = cmp(schema + 'TabsTabPanel');
        if (panel) {
            const collection = panel.model && typeof panel.model.get === 'function' ? panel.model.get('TabsCollection') : null;
            const names = collection && typeof collection.getKeys === 'function' ? collection.getKeys() : [];
            const list = document.getElementById(schema + 'TabsTabPanel-tabpanel-items');
            tabs = { panel: 'Tabs', items: names.map((name, index) => {
                const li = list ? list.querySelector('li[data-item-index=""' + index + '""]') : null;
                return { name, index, caption: text(li), visible: vis(li), active: !!(li && li.classList.contains('ts-tabpanel-active-item')) };
            }) };
        }

        let stage = null;
        const bar = cmp('SectionActionsDashboardActionsControlContainer');
        if (bar) {
            const values = [];
            const subvalues = {};
            for (const i of toArray(bar.items)) {
                if (!i.caption) { continue; }
                values.push(i.caption);
                const sub = toArray(i.menu && i.menu.items).map(s => s.caption).filter(Boolean);
                if (sub.length > 0) { subvalues[i.caption] = sub; }
            }
            stage = { values, subvalues, visible: vis(document.getElementById('SectionActionsDashboardActionsControlContainer-wrap')) };
        }

        return { controls, buttons, details, tabs, stage, unknown };
    }";

    /// <summary>
    /// Turns the snapshots of a page, one per visited tab, into elements. Every element is
    /// recorded once, by its identity in the schema: a header field stays visible on every tab and
    /// must not come back under its fallback code on the second pass.
    /// </summary>
    internal sealed class ClassicSnapshotCollector
    {
        /// <summary>Buttons BasePageV2 gives every Classic page and renders only once it is dirty.</summary>
        internal static readonly IReadOnlyList<string> StandardPageButtons = new[] { "SaveButton", "DiscardChangesButton" };

        private readonly IReadOnlyDictionary<string, string> _typeByControl;
        private readonly bool _includeHidden;
        private readonly List<DiscoveredElement> _elements = new();
        private readonly List<UnknownControl> _unknown = new();
        private readonly HashSet<string> _codes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _identities = new(StringComparer.Ordinal);

        /// <summary>Creates a collector over the control-class map of the Classic registrations.</summary>
        public ClassicSnapshotCollector(IReadOnlyDictionary<string, string> typeByControl, bool includeHidden)
        {
            _typeByControl = typeByControl;
            _includeHidden = includeHidden;
        }

        /// <summary>Elements recorded so far.</summary>
        public IReadOnlyList<DiscoveredElement> Elements => _elements;

        /// <summary>Controls no element type claims, as met.</summary>
        public IReadOnlyList<UnknownControl> Unknown => _unknown;

        /// <summary>Records what one snapshot shows, skipping identities already recorded.</summary>
        public void Add(JsonElement snapshot)
        {
            // Fields: one element per schema name, so that a DateTime column's two controls fold into one.
            var byName = snapshot.GetProperty("controls").EnumerateArray()
                .GroupBy(c => Text(c, "name"), StringComparer.Ordinal)
                .Where(g => g.Key.Length > 0);

            foreach (var group in byName)
            {
                var identity = "field:" + group.Key;
                if (_identities.Contains(identity))
                {
                    continue;
                }

                var parts = group.ToArray();
                var controls = parts.Select(p => Text(p, "control")).Distinct(StringComparer.Ordinal).ToArray();
                var dataValueType = parts.Select(p => p.TryGetProperty("dataValueType", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : (int?)null)
                    .FirstOrDefault(d => d is not null);

                var type = ResolveFieldType(controls, dataValueType, _typeByControl);
                if (type is null)
                {
                    _identities.Add(identity);
                    foreach (var control in controls)
                    {
                        _unknown.Add(new UnknownControl(control, group.Key));
                    }

                    continue;
                }

                var column = parts.Select(p => Text(p, "column")).FirstOrDefault(c => c.Length > 0) ?? string.Empty;
                var code = ChooseCode(column, group.Key, _codes);
                var first = parts[0];

                Record(identity, new DiscoveredElement(
                    Code: code,
                    Label: Text(first, "label"),
                    Type: type,
                    HostTag: string.Join("+", controls),
                    Required: parts.Any(p => Flag(p, "required")),
                    ReadOnly: parts.All(p => Flag(p, "readonly")),
                    Visible: parts.Any(p => Flag(p, "visible")),
                    Stages: null,
                    Columns: null,
                    Tabs: null,
                    Uncertainty: null,
                    TypeUndecidable: false,
                    Name: string.Equals(code, group.Key, StringComparison.Ordinal) ? null : group.Key));
            }

            foreach (var button in snapshot.GetProperty("buttons").EnumerateArray())
            {
                var name = Text(button, "name");
                if (name.Length == 0)
                {
                    continue;
                }

                Record("button:" + name, new DiscoveredElement(name, Text(button, "label"), "Button", "Button",
                    false, false, Flag(button, "visible"), null, null, null, null, false));
            }

            foreach (var detail in snapshot.GetProperty("details").EnumerateArray())
            {
                var detailSchema = Text(detail, "schema");
                if (detailSchema.Length == 0)
                {
                    continue;
                }

                var columns = Strings(detail, "columns");
                Record("detail:" + detailSchema, new DiscoveredElement(detailSchema, Text(detail, "caption"), "Detail", "Detail",
                    false, false, Flag(detail, "visible"), null, columns.Length > 0 ? columns : null, null, null, false));

                if (detail.TryGetProperty("buttons", out var detailButtons) && detailButtons.ValueKind == JsonValueKind.Array)
                {
                    foreach (var button in detailButtons.EnumerateArray())
                    {
                        var name = Text(button, "name");
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        Record("button:" + detailSchema + "/" + name, new DiscoveredElement(detailSchema + name, Text(button, "label"), "Button", "Button",
                            false, false, Flag(button, "visible"), null, null, null, null, false,
                            Name: name, Detail: detailSchema));
                    }
                }
            }

            if (snapshot.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Object &&
                tabs.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                var captions = new List<string>();
                foreach (var item in items.EnumerateArray())
                {
                    var name = Text(item, "name");
                    var caption = Text(item, "caption");
                    var visible = Flag(item, "visible");
                    if (visible && caption.Length > 0)
                    {
                        captions.Add(caption);
                    }

                    if (name.Length > 0)
                    {
                        Record("tab:" + name, new DiscoveredElement(name, caption, "Tab", "Tab", false, false, visible, null, null, null, null, false));
                    }
                }

                if (captions.Count > 0)
                {
                    Record("tabpanel", new DiscoveredElement(TabPanelCode, string.Empty, "TabPanel", "TabPanel", false, false, true,
                        null, null, captions, null, false));
                }
            }

            if (snapshot.TryGetProperty("stage", out var stage) && stage.ValueKind == JsonValueKind.Object)
            {
                var values = Strings(stage, "values");
                IReadOnlyDictionary<string, IReadOnlyList<string>>? subvalues = null;
                if (stage.TryGetProperty("subvalues", out var sub) && sub.ValueKind == JsonValueKind.Object)
                {
                    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    foreach (var property in sub.EnumerateObject())
                    {
                        // Playwright serialises objects with reference metadata ("$id"); only real entries count.
                        if (property.Name.StartsWith('$') || property.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        var list = property.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).Where(v => v.Length > 0).ToArray();
                        if (list.Length > 0)
                        {
                            map[property.Name] = list;
                        }
                    }

                    subvalues = map.Count > 0 ? map : null;
                }

                if (values.Length > 0)
                {
                    Record("stage", new DiscoveredElement(StageBarCode, string.Empty, "CasesBar", "BaseStageControl", false, false,
                        Flag(stage, "visible"), values, null, null, null, false, Subvalues: subvalues));
                }
            }
        }

        /// <summary>
        /// Adds the page buttons every Classic edit page has but renders only once it is dirty, so
        /// no analysis of a clean page can see them. They are recorded without a caption.
        /// </summary>
        public void AddStandardPageButtons()
        {
            foreach (var name in StandardPageButtons)
            {
                var identity = "button:" + name;
                if (_identities.Contains(identity))
                {
                    continue;
                }

                _identities.Add(identity);
                _codes.Add(name);
                _elements.Add(new DiscoveredElement(name, string.Empty, "Button", "Button", false, false, false, null, null, null, null, false));
            }
        }

        /// <summary>
        /// Records an element once per identity. Recorded, not merely met: a hidden control on a tab
        /// that is not open stays collectable for the pass in which its tab is open.
        /// </summary>
        private void Record(string identity, DiscoveredElement element)
        {
            if (_identities.Contains(identity) || _codes.Contains(element.Code) || !(_includeHidden || element.Visible))
            {
                return;
            }

            _identities.Add(identity);
            _codes.Add(element.Code);
            _elements.Add(element);
        }
    }

    /// <summary>
    /// The element type for the controls rendered under one schema name.
    ///
    /// The date family is decided by the column when it says: a DateTime column renders a date and
    /// a time control that the <c>DateTime</c> element drives together, a Date column one date
    /// control, a Time column one time control. Without the column the rendered controls decide.
    /// Returns null for a control no Classic element type claims.
    /// </summary>
    internal static string? ResolveFieldType(
        IReadOnlyCollection<string> controls,
        int? dataValueType,
        IReadOnlyDictionary<string, string> typeByControl)
    {
        var types = controls
            .Select(c => typeByControl.TryGetValue(c, out var t) ? t : null)
            .Where(t => t is not null)
            .Select(t => t!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (types.Length == 0)
        {
            return null;
        }

        var dateFamily = types.All(t => t is "Date" or "Time" or "DateTime");
        if (dateFamily)
        {
            return dataValueType switch
            {
                DateTimeValueType => "DateTime",
                DateValueType => "Date",
                TimeValueType => "Time",
                _ => types.Length > 1 ? "DateTime" : types[0],
            };
        }

        return types[0];
    }

    /// <summary>
    /// The code a field gets: the bound column, which a test author recognises, unless the page has
    /// that code already; then the schema name, which is unique by construction.
    /// </summary>
    internal static string ChooseCode(string? column, string name, IReadOnlySet<string> taken)
    {
        var preferred = string.IsNullOrWhiteSpace(column) ? name : column.Trim();
        return taken.Contains(preferred) && !string.Equals(preferred, name, StringComparison.Ordinal) ? name : preferred;
    }

    private static string Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Flag(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string[] Strings(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();
}
