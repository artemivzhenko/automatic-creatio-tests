# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Process cases.** A backend tier kept apart from the page cases: scripts under
  `ProcessCases/` (`ProcessCasesFolders`) start a business process over the process engine
  service (`start <process> [with Param=Value;...]`), assert its result parameters
  (`check-result <parameter> <op> <value>`, the operators and typing of `check-value`) and the
  records it left behind over OData (`check-record <object> Column=Value;... [not] exists`).
  Processes are described under `Configuration/processes/` (parameters, types, directions), so
  an unknown process, an output passed in, or a value of the wrong type fails in validation. The
  browser is now launched on first use, so a run of process cases alone never opens one: the
  bundled case runs in about a second.

- **Data grids.** New element type `DataGrid` (`crt-data-grid`), with `check <code> [not] exists` and
  `check <code> is [not] editable`. Editability and placement inside an expansion panel are two
  properties Creatio configures independently, so all four combinations occur and each is asserted
  separately. A grid has no caption of its own: its `label` is the caption of the panel holding it,
  which is also how the grid is reached — the framework opens and scrolls to that panel before it
  reads anything.
- **Column assertions.** `check <code> columns` compares the rendered column names and their
  left-to-right order with the `columns` recorded in the page file. The failure message names the
  first position that differs, because a renamed column and two swapped columns are different page
  changes that a bare pass/fail cannot tell apart. A line whose element declares no `columns` is
  rejected during validation rather than passing vacuously.
- **`add-record`.** `add-record <code> set Column=Value;Column=Value` adds a record to a grid that
  allows inline editing. The list is split on `;` and each pair on its first `=`, and quoting it is
  optional. Column names are checked against the page file during validation, so a misspelled column
  fails before a browser starts; values are entered according to what the cell renders -- a checkbox
  takes `true`/`false`, a lookup is resolved through its autocomplete, everything else is typed. The
  row reaches the database when the page is saved, the same way a value written with `set` does.
  Creatio opens most cell editors only on a double click, so a single click is tried first and the
  second spent only where it is needed; a column Creatio renders as a link or a coloured label is
  read-only in the list and is reported as such.
- **Toggle tabs.** New element type `ToggleTab` (`crt-button-toggle-group`) for the second shape
  Creatio renders tabs in. It is the opposite of a tab panel in the DOM: one `element-name` for the
  whole group, with the tabs inside named only by their captions. So the group is the element and the
  open tab is its value -- `set X "Tab 2"` switches, `check-value X eq "Tab 2"` asserts -- and no new
  command was needed. `analyse` records the captions as `tabs`, and the `type` in the page file is
  what separates the two kinds. Adds the `Tab` value kind, which copies into text and nothing else.
- `analyse` opens every collapsed expansion panel and scrolls each one into view before its final
  pass, and records the columns of each grid it finds.
- `analyse` walks the tabs of every toggle group as it already walks the tabs of a tab panel, and
  scrolls each tab from top to bottom before reading it. Creatio renders much of a page only as it
  approaches the viewport, so without this how much of a page was found depended on the size of the
  window and on which tab the page happened to open on.
- `viewportWidth` and `viewportHeight` in `run.config.json` (1600 x 950 by default) fix the size of
  the browser window, for both cases and analysis.

### Fixed

- Grid editability is read from the footer row Creatio adds for appending a record, not from the
  absence of a per-cell editing-disabled marker. That marker means one column is read-only, so an
  editable grid holding a calculated or image column was reported as not editable.
- A Save button is now considered gone when it stops showing, not only when Creatio removes it from
  the page. Some pages collapse the button instead of removing it, and there a save that plainly
  succeeded was reported as a failure.
- A toggle group no longer reports the caption of its first tab as its own name.
- Analysis no longer lets the first pass decide what the rest of the walk may find: a control met
  while hidden was marked as seen and then skipped on every later pass, even once it became visible.
  Which tab the page opened on therefore silently changed the result.
- The browser window is no longer whatever size Playwright defaults to. At 1280 x 720 Creatio's
  side-panel overlay covers the tab headers, so `click <tab>` could not be performed at all on a
  contact page.
- An option list is written to a page file only when the element type declares a member that can take
  it, so analysis can no longer record something the next run would refuse to load.
- A control with no caption of its own no longer inherits the caption of the expansion panel it sits
  in. That fallback exists for grids, and element types now declare whether it applies to them, so a
  grid's toolbar buttons are no longer all recorded under the grid's panel caption.

## [1.0.0] — 2026-09-03

First release. A complete rewrite of the original framework, sharing none of its structure.

### The language

Tests are `.act` files, one command per line, in a DSL with twelve commands: `use`, `open`, `check`,
`check-value`, `set`, `click`, `goto`, `clear`, `wait`, `create`, `end` and `analyse`. See
[docs/dsl.md](docs/dsl.md).

### How it is built

- **Two passes.** A test case is parsed and validated in full before a browser opens, so a typo costs
  milliseconds rather than minutes. Every problem in a file is reported at once, with line numbers.
- **Reflection registries.** An element type or a command is one class carrying one attribute.
  Nothing central is edited to add either — no switch, no enum, no factory.
- **Capabilities rather than types.** A command declares the interface it needs, so a new element type
  works with every command that matches it. `Tab` gained `click` and `check` support without either
  command changing.
- **Values cross layers as text.** Every format and Creatio quirk lives inside its element class, so
  the command layer contains no type switches at all.

### What it can drive

Fifteen element types: `Text`, `RichText`, `Email`, `Phone`, `Link`, `Integer`, `Decimal`, `Date`,
`Time`, `DateTime`, `Boolean`, `Lookup`, `Button`, `Tab` and `CasesBar`.

### Analysing a page

`analyse` opens a Freedom UI page and writes its configuration: every field, button, tab and stage bar
it can address, plus the controls it cannot. It opens each tab in turn, because Freedom UI keeps only
the open one in the DOM. Available as a DSL command and as a CLI subcommand.

A run has two phases: analysis scripts first, the catalog reloaded from what they wrote, then the test
cases. Analysis is off by default — once a page is described the configuration is reused.

### Finding a page

`schemas` lists the instance's Freedom UI page schemas over OData, each with the address that opens
it. Analysing a page needs its schema name, and nothing in the browser shows the list.

### Configuration

One file per page under `Configuration/pages`, each holding the page and its elements. Environment,
site, static values, objects and run settings are separate files.

### Running

A CLI with two modes, and a text, JUnit XML or HTML report. `--validate-only` lints a whole suite
without a browser.

### Known limitations

- `create` is implemented and validated but has never been executed against a live Creatio. Treat it
  as experimental.
- Verified against one instance: Creatio 8.3.2, .NET Framework, English interface.
- An element's tab is not recorded in configuration, so a control on a closed tab reports as missing
  rather than as "on another tab".
- A read-only, empty date or number control cannot have its exact type established by anyone; it is
  recorded and reported as presence-only.
