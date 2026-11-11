# AGENTS.md

Orientation for an AI agent working in this repository. Read this before changing anything.

## What this project is

A test framework for Creatio pages, Freedom UI (8.x) and Classic UI (7.x inside the 8.x Shell). Tests
are **not** written in C#. They are written in a small DSL, one command per line, in `.act` files:

```
use Supervisor
open ContactForm for c4ed336c-3e9b-40fe-8b82-5632476472b4
check JobTitle exists
set JobTitle "Head of Sales"
check-value JobTitle eq "Head of Sales"
click SaveButton
```

The C# code is the interpreter for that DSL on top of Playwright. A test author never touches
selectors, Playwright, or C#.

## Build and check

```bash
dotnet build                                                      # 0 warnings is the bar
dotnet test                                                       # 291 tests, no browser needed
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only  # lint the suite, no browser
dotnet run --project src/CreatioAutoTests.Cli --                  # run the cases live
```

A live run needs `Configuration/creatio.env.config.json`, which is gitignored because it holds a
password. Copy `creatio.env.config.example.json` and fill it in.

**CS1591 is an error in `src/`.** Every public member needs an XML summary or the build fails.

## The rule that shapes everything

**A test case is validated completely before a single browser action runs.** Parsing and validation
are the first pass; execution is the second. A typo fails in milliseconds with a line number rather
than three minutes into a browser session.

So element types, value kinds and capabilities are all knowable *statically*, from configuration plus
attributes, with no live page. **Never introduce anything that needs an open browser to decide whether
a command is valid.**

## Layers, bottom to top

| Layer | Namespace | Responsibility |
|---|---|---|
| 1 | `CreatioAutoTests.Frontend` | UI elements, one class per element type and generation: Freedom UI addressed by `element-name`, Classic UI (`Frontend.Classic`) by the ids built from page schema, element name and control |
| 1 | `CreatioAutoTests.Frontend.Runtime` | Per-generation page readiness (`IPageRuntime`): the one page-level thing the generations disagree on |
| 2 | `CreatioAutoTests.Validation` | Command shape checking and static target resolution |
| 3 | `CreatioAutoTests.Commands` | One class per DSL keyword, discovered by reflection |
| 4 | `CreatioAutoTests.Configuration`, `.Session` | Config loading, catalog, authentication, browser contexts |
| 5 | `CreatioAutoTests.Execution` | Parser, execution plan, command executor |
| 6 | `CreatioAutoTests.Runner` | Wires it together; the CLI and an NUnit fixture are thin shells |
| — | `CreatioAutoTests.Analysis` | Reads a live page and writes its configuration file: `PageAnalyzer` for Freedom UI through host tags, `ClassicPageAnalyzer` for Classic UI through Ext components and the view model; the address picks one |

A lower layer never references a higher one.

## Four invariants

Breaking any of these is a design error, not a style preference.

**1. Registration is by attribute, never by editing a list.**
`[UiElement("Lookup", HostTag = "crt-combobox", ValueKind = ValueKind.Lookup)]` and
`[Command("click", ...)]` are discovered by reflection. Adding an element type or a command is adding
*one class*. If a change seems to need an edit to a registry, a factory, an enum or a switch, you have
misread the task — stop and say so.

A type name is unique *per generation*: `[UiElement("Lookup", Generation = UiGeneration.ClassicUI,
Controls = new[] { "LookupEdit", "ComboBoxEdit" }, ...)]` coexists with the Freedom one, and the page's
`type` picks between them. Never make a Classic element reachable from a Freedom page or the other way
round, and never name a Classic type differently from its Freedom twin: the point is that a case reads
the same whatever the page is built with.

**2. Commands declare capabilities, not classes.**

```csharp
[Command("click", Validator = typeof(SingleTargetValidator),
                  SupportedTargets = new[] { typeof(IClickable) })]
```

Capabilities live in `Frontend/Core/Capabilities.cs`. `Tab` implements `IClickable` and both `click`
and `check` accepted it with no change to either command. Preserve that. **Never write
`if (element is SomeConcreteField)` in a command or a validator.**

**3. Values cross layers as text.** `IValueElement` exposes string accessors; every conversion,
format and Creatio quirk lives inside the element class. `ValueKind` is the only type information the
upper layers see, and `ValueCompatibilityPolicy` is the only place that reasons about it.

**4. Async only.** No synchronous wrappers — the predecessor had 34 and they were removed on purpose.
`CancellationToken` last, `.ConfigureAwait(false)` on anything reaching Playwright.

## Creatio behaviour that looks like a bug

These cost real debugging time. Know them before touching the analyser or an element class.

* **Only the open tab is in the DOM.** A control on another tab is legitimately absent.
* **Tab headers carry no active marker** — no `aria-selected`, no class. The open tab is the one whose
  `crt-toggle-container-item` is still laid out; containers of visited tabs accumulate and the
  inactive ones are hidden with `display:none`.
* **Creatio remembers which tab a page was closed on**, so which tab opens cannot be assumed.
* **The loading overlay clears long before the form is built**, so `open` waits for the control count
  to stop growing.
* **Two Angular Material generations coexist**: 8.3 emits `mat-mdc-input-element`, older builds
  `mat-input-element`. Matching only one is silent — the host resolves and the value never does.
* **An MDC checkbox ignores a click on its host.** The code tries the touch target, the styled box,
  the label, then dispatches the event on the input.
* **An empty date control is told from an empty time one by the picker icon's `aria-label`**
  (`Calendar`, `Time`, `Date/Time`). That label follows the interface language, so an unrecognised one
  falls back to the displayed value rather than guessing.

And on Classic UI, verified on Creatio 8.3 inside the Shell:

* **There is no iframe.** The Shell renders a Classic page in its own document, and the old
  `/0/Nui/ViewModule.aspx` address redirects to `/0/Shell/#CardModuleV2/...`. The loading overlay is
  the same `#loading-animation`.
* **Ids are predictable: `{Schema}{Name}{Control}-el`.** `Name` is the element's name in the schema's
  `diff`, which the section wizard spells `LOOKUP<guid>`; the `-wrap` box carries `base-edit-disabled`,
  the `{Schema}{Name}Label` caption carries `t-label-is-required`. Inside a detail the prefix is the
  detail schema, not the page.
* **`Ext.getCmp(id)` hands over the live control and, through `model`, the page's view model.**
  `bindings.value.modelItem` is the column; `model.get(column)` is the typed value. That is how typed
  fields are read, because the DOM shows the user's culture (`12,50`, `06.11.2026`).
* **Search in a `LookupEdit` starts on `keyup`.** Playwright types non-Latin text through
  `insertText`, which raises no key events, so the element dispatches a `keyup` itself after filling.
  The list is a `div.listview` appended to `body`, tagged with the element's name in `data-item-marker`.
* **Tab headers have no ids.** They are `li[data-item-index]`, and the index is the position in the
  page's `TabsCollection`, hidden tabs included; the name is resolved to the index through the view
  model. Visited tabs stay in the DOM, hidden.
* **The toolbar changes when the page is dirty.** Close gives way to Save and Cancel after the first
  edit, so Close is only addressable before it.
* **A stage change saves the record.** The actions dashboard's `onActionChanged` sets the stage column
  and calls `saveMasterEntity` (`SaveRecord`), then reloads the card. `set` on a `CasesBar` is a write.
  Sub-stages are folded into their parent's menu (`createActionsMenu`); the bar shows the parent, the
  stage column holds the sub-stage, which is why the value is read from the view model. Before the
  move the page is validated (`ValidateCard`): an empty required field stops it with a message box.
* **Classic's message box is `.ts-messagebox-box`**: caption in `.ts-messagebox-caption`, buttons as
  `.t-btn-wrapper[data-item-marker]` whose marker is the localized caption (`ОК`, `Так`). A one-button
  box after a stage change may be a refusal *or* a success note ("the order was cancelled"); only the
  stage column afterwards tells which. A page's own logic may instead open a Freedom UI modal
  (`mat-dialog-container`) from the Shell, and an open dialog swallows every later click.
* **The actions dashboard and the details load after the page.** They are separate modules whose ids
  are not prefixed by the page schema (`SectionActionsDashboard...`, `{DetailSchema}...`), so the
  page runtime counts every `-el` node once the page's own have appeared, and the stage bar and
  details still wait for their own content before answering.
* **A configuration grid edits in place and saves at once.** The add button opens a row with class
  `configuration-grid-row-active`; its editors are controls whose id is the column name plus the
  control class (`QSTrademarkLookupEdit-el`, no page prefix) and the cell carries `column-name`; the
  row's actions sit in `.configuration-grid-row-actions` as `[data-item-marker]` `save`, `cancel`,
  `card`, `copy`, `remove`. Enter in a lookup editor opens the selection window, so the row is saved
  with the `save` action, never with Enter. An empty grid renders no header row: columns come from
  the grid component's `captionsConfig` instead.
* **Button names include `Button`.** The schema calls the element `AddRecordButton` and Classic
  appends the control class: `...AddRecordButtonButton-wrapperEl`. A page file's `name` is the
  schema name, never the id.
* **A business rule may lock a field the moment it gets a value**, and a page may lay a field out with
  zero width, present but never visible. Neither is a framework fault; both show up as a read-only
  refusal or a not-found.

## Conventions

C# 12, `net8.0`, nullable enabled, file-scoped namespaces, `sealed` by default, one public type per
file. Comment *why*, not what — the notes above are the kind that earn their place. NUnit 4 in tests;
`Assert.*` never appears in `src/`. A failed assertion throws `CommandFailedException`, which is the
only failure the runner reports as a failed test rather than a broken run.

`copilot/CONVENTIONS.md` has the full list.

## Where things live

| Path | What |
|---|---|
| `src/CreatioAutoTests.Core` | the framework |
| `src/CreatioAutoTests.Core/Frontend/Classic` | the Classic UI elements and their base classes; `Frontend/Runtime` holds the per-generation page readiness |
| `src/CreatioAutoTests.Cli` | the runner: run cases, `analyse` a page, or list `schemas` |
| `tests/` | unit tests, no browser |
| `Cases/`, `Analysis/` | `.act` scripts: test cases, and scripts that describe pages. `.act` is gitignored (written against someone's environment); bundled examples are `.act.exp`, which the runner skips |
| `ProcessCases/` | `.act` scripts of the backend tier: `start` a process, `check-result`, `check-record`; no browser |
| `src/CreatioAutoTests.Core/Session/CreatioODataClient.cs` | the one OData client: insert, update, delete, count, get, run a process; `BPMCSRF` and `ForceUseSession` on every call |
| `src/CreatioAutoTests.Core/Execution/CaseVariables.cs`, `ODataValues.cs` | `$name` substitution and `$name+N`; the typing of filter literals and JSON values |
| `src/CreatioAutoTests.Core/Runner/EvidenceWriter.cs` | `--task-folder <dir>`: `evidence/<AC>-report-N.txt` and a draft report row per case, from the `# ac:` directive |
| `Configuration/pages/` | one file per page, holding the page and its elements |
| `Configuration/processes/` | one file per process a case may start: parameters, types, directions |
| `copilot/tasks/` | briefs for delegated work — gitignored, present only in the working tree |
| `backlog/` | the framework's own backlog — gitignored, same |
| `source/` | the pre-rewrite framework, kept for reference, gitignored |

## Before you finish

`dotnet build` and `dotnet test` must both be clean. If you cannot finish, say what you did and what
remains — a half-applied change is worse than none, and reverting cleanly is a valid outcome.
