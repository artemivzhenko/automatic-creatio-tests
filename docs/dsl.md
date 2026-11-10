# DSL Reference

## Syntax

Write one command per line. Blank lines and lines beginning with `#` are ignored. Double quotes preserve spaces and make a literal value; unquoted values may name static values or elements where the command permits them. The first command must be `use <user>`.

Copy the `.act.exp` examples under `Cases/` and `ProcessCases/` to `.act` files to adapt them.

## Commands

| Command | Syntax | Target | Example |
| --- | --- | --- | --- |
| use | `use <user>` | none | `use testUser` |
| open | `open <page> [for <recordId>]` | none | `open ContactForm for c4ed336c-3e9b-40fe-8b82-5632476472b4` |
| check | `check <code> [not] exists` \| `check <code> is [not] required\|readonly\|editable` \| `check <code> columns` | `IExistable`; `required`/`readonly` need `IStateAware`, `editable` needs `IEditableAware`, `columns` needs `IColumned` | `check Input_Text is required` |
| check-value | `check-value <code> eq|ne|gt|ge|lt|le|contains|startswith|endswith <value>` | `IValueElement` | `check-value Input_Text eq "Sample Name 123"` |
| set | `set <code> "value" | set <code> <sourceCodeOrStaticValue>` | `IValueElement` | `set Input_Text DefInput` |
| click | `click <code>` | `IClickable` | `click SaveButton` |
| goto | `goto <code>` | `INavigableToRecord` | `goto ComboBox_Account` |
| clear | `clear <code>` | `IClearable` | `clear ComboBox_Contact` |
| wait | `wait <number> second|minute` | none | `wait 2 second` |
| create | `create <object>` | none | `create TestEntity` |
| end | `end` | none | `end` |
| analyse | `analyse ["<page path>"] [<pageName>]` | none | `analyse "/0/Shell/#Card/Contacts_FormPage/edit/<id>" ContactForm` |
| analyse | `analyse ["<page path>"] [<pageName>]` | none | `analyse "/0/Shell/#Card/Contacts_FormPage/edit/{id}" ContactForm` |
| add-record | `add-record <code> set Column=Value;Column=Value` | `IRecordAddable` | `add-record AddressList set Address type=Business;Full address=Main st. 1` |
| start | `start <process> [with Param=Value;Param=Value]` | none (a configured process) | `start UsrOrder_Process with Amount=10` |
| check-result | `check-result <parameter> eq|ne|gt|ge|lt|le|contains|startswith|endswith <value>` | none (an output of the last started process) | `check-result Verdict eq "big"` |
| check-record | `check-record <object> Column=Value;Column=Value [not] exists` | none (read over OData) | `check-record UsrOrder UsrName=Order 1 exists` |
| check-row | `check-row <code> Column=Value;Column=Value [not] exists` | `IRowed` | `check-row Products Product=Pump;Quantity=2 exists` |
| select-row | `select-row <code> Column=Value;Column=Value` | `IRowed` | `select-row Products Product=Pump` |
| delete-row | `delete-row <code> Column=Value;Column=Value` | `IRowed` | `delete-row Products Product=Pump` |
| on | `on <page>` | none (a configured page) | `on ProductClassic` |

`check <code> exists` also verifies that the rendered caption matches the `label` in the page file under `Configuration/pages`; required and readonly are never asserted implicitly, they are separate lines.

`check <code> columns` compares the columns the element renders -- names and left-to-right order --
with the `columns` listed for it in its page file. The expectation lives in configuration rather than
in the script for the same reason a caption does: a column list is a property of the page, so a
renamed column is one edit to the page file instead of one edit per case that touches the grid. A
line whose element declares no `columns` is rejected during validation, before a browser is started:
with nothing to compare against it could never fail.

`add-record <code> set Column=Value;Column=Value` adds one record to an element that accepts one in
place. The list is split on `;`, then each pair on its first `=`, so a value may itself contain an
`=`; whitespace around names and values is dropped. Quoting the list is optional -- the tokens are
joined back together -- so both of these are the same line:

```
add-record ContactList set Full name=Jon Doe;Job title=Developer
add-record ContactList set "Full name=Jon Doe;Job title=Developer"
```

Every column named is checked against the `columns` in the page file during validation, so a
misspelled column fails before a browser is started. Values are entered the way the cell asks for
them: a checkbox column takes `true` or `false`, a lookup column takes the text of an offered option
and is resolved through its autocomplete, and everything else is typed in.

Not every column of an editable grid can be filled. Creatio renders some of them as links or coloured
labels, and those stay read-only in the list whatever the grid's own setting is; naming one fails with
`did not open for editing`. Of the rest, only the first column and checkbox columns open on a single
click -- the others need a double click, which the framework does for you.

The record is entered in place and reaches the database when the page is saved, exactly as a value
written with `set` does -- `add-record` saves nothing on its own. Note that Creatio replaces a page's
Close button with Save and Cancel as soon as the page has unsaved changes, so those two buttons are
only addressable after something has changed.

`check-row`, `select-row` and `delete-row` address one row of an element that lists records by the
values in its cells, named by the column captions as `check-record` names them for OData. Nothing is
ever addressed by position. `check-row` asserts presence, `select-row` clicks the row, and
`delete-row` deletes it through the element's own delete action and answers the confirmation;
columns are checked against the element's `columns` during validation when the page file declares
them. Today the Classic `Detail` offers rows.

`on <page>` says which configured page the browser is on after Creatio navigated by itself, after a
button that opened another record or a link that was followed. Nothing is navigated: the open page
is rebound, waited for, and the lines below resolve their codes against that page.

`analyse` describes a page and writes `Configuration/pages/<pageName>.json`: every field, button,
tab and stage bar it can address, plus a list of controls that have no element type yet. Given an
address it navigates there itself, so the page does not have to be configured first; given no
argument it describes the page `open` left on screen.

Analysis and testing belong in separate scripts, in separate folders. A script is validated in full
before any of it runs, so elements `analyse` discovers are not addressable on the line below it --
but they are addressable by the test cases, because the run has two phases. See "Analysis phase"
below. The same work is available without a script as `creatio-tests analyse "<path>"`.

`create` follows its configured section, New button, form, fields, and Save button. `end` logs readable values from the current page without changing the outcome. `analyse` describes a live page and writes its page configuration; it can also be run as the CLI subcommand `analyse`.

## Process cases

The backend tier. A process case starts business processes over the process engine service,
reads their result parameters back and checks the records they left behind over OData. No page
is opened, and a run made only of process cases never launches a browser - the case above runs
in about a second.

```
use Supervisor
start UsrOrder_Process with Amount=500;Contact=0c48c818-08f0-4f70-9b58-be512227f3bf
check-result Verdict eq "big"
check-result Total ge "5"
check-record UsrOrder UsrName=Order 1;UsrAmount=500 exists
check-record UsrOrderLog UsrOrder=0c48c818-08f0-4f70-9b58-be512227f3bf not exists
```

Process cases live in their own folders (`ProcessCasesFolders`, default `./ProcessCases`) and
run after the UI cases; `--filter` and `--validate-only` see them the same way. They are a
different kind of test - what the backend does, not what a user sees - which is why they are
kept apart from the page cases rather than mixed into them.

`start` names a process described under `Configuration/processes` (one file per process, see
below). Values after `with` are checked against the parameter's type before the run, an output
parameter cannot be passed in, and a bare value may name a static value. The engine's answer
decides the line: a process that did not start (unknown, unpublished, an exception) fails with
the engine's message.

`check-result` reads an output or variable parameter of the process the script last started,
with the operators and typing of `check-value`: numbers compare as numbers, dates as dates, the
rest as text. A lookup result is its id.

`check-record` builds an OData filter from the list: a GUID, `true`/`false` and a number go
unquoted, everything else is a string; a lookup column given a GUID is addressed by its `Id`
suffix (`UsrContact=<guid>` reads `UsrContactId`). Only existence is asserted, which is enough
to prove that a process created, found or removed what it should have.

```jsonc
// Configuration/processes/UsrOrder_Process.json
{
  "name": "UsrOrder_Process",
  "parameters": [
    { "code": "Amount", "type": "Money", "direction": "in" },
    { "code": "Contact", "type": "Lookup", "direction": "in" },
    { "code": "Verdict", "type": "Text", "direction": "out" },
    { "code": "Total", "type": "Integer", "direction": "variable" }
  ]
}
```

`direction` is `in`, `out` or `variable` (both). The file is what the process schema declares;
the knowledge base's `proc.py test-config <package> <Process> --out <file>` writes it from the
package, so a process built as files describes itself for its tests.

## Element Types

| `type` | Creatio control | Value kind | Extra members |
| --- | --- | --- | --- |
| Button | `crt-button` | None | `isSave`, `doubleClick`, `settleMs`, `disappearTimeoutMs`; `pinned` |
| CasesBar | `crt-entity-stage-progress-bar` | Stage | `values`, `subvalues`; `pinned` |
| DataGrid | `crt-data-grid` | None | `columns`; `pinned` |

A `DataGrid` has two properties Creatio configures independently, the way a field has required and
read-only: whether inline editing is on, and whether it sits inside an expansion panel. All four
combinations occur. Inline editing is read from the row Creatio adds to the table footer for
appending a record, which it renders only for a grid that takes edits in place; the per-cell
editing-disabled marker says only that one column is read-only, and an editable grid with a
calculated or image column carries plenty of those.

A grid carries no caption of its own, so its `label` is the caption of the panel that holds it, and
a grid placed directly on a page has an empty label. That label is also how the grid is reached: when
it is not on screen, the framework brings that panel into view and opens it if it is closed. Creatio
renders a panel's contents only once it scrolls into view, so an expanded panel further down the page
holds nothing until something looks at it.

Creatio renders tabs in two shapes, and they are opposites in the DOM. A tab panel gives every tab
its own `element-name`, so each tab is an element of type `Tab` and `click <code>` switches to it. A
toggle group has one `element-name` for the whole group and names the tabs inside it only by their
captions, so the group is one element of type `ToggleTab` and the open tab is its value:

```
click GeneralInfoTab                              # a tab of a tab panel
set ViewToggle "Tab 2"                            # a tab of a toggle group
check-value ViewToggle eq "Tab 2"
```

The distinction is the `type` in the page file, which is also what tells `analyse` how to read the
control. `tabs` lists the captions the group offers, in display order; a toggle group has no caption
of its own, so its `label` is empty. `click` does not accept a `ToggleTab` and `set` does not accept
a `Tab`: which of the two a control is decides how it is driven.

`columns` lists the grid's column captions in display order, as `analyse` found them. Only the
columns a person sees are recorded: Creatio gives every grid a leading cell for the row controls and
a trailing cell that absorbs the free width, and both announce themselves as column headers without
naming any data.
| Tab | `crt-tab-panel-header-item` | None | `pinned` |
| ToggleTab | `crt-button-toggle-group` | Tab | `tabs`; `pinned` |
| Text | `crt-input` | Text | `pinned` |
| RichText | `crt-rich-text-editor` | Text | `pinned` |
| Email | `crt-email-input` | Text | `pinned` |
| Phone | `crt-phone-input` | Text | `pinned` |
| Link | `crt-web-input` | Text | `pinned` |
| Integer, Decimal | `crt-number-input` | Number | `pinned` |
| Date, Time, DateTime | `crt-datetimepicker` | DateTime | `pinned` |
| Boolean | `crt-checkbox` | Boolean | `pinned` |
| Lookup | `crt-combobox` | Lookup | `pinned` |

`pinned: true` marks an entry as a human decision. The analyser leaves that entry untouched on later runs, including decisions that cannot be inferred from the DOM.

## Classic UI

A page file declares its generation with `type`: `FreedomUI` (the default) or `ClassicUI`. The two
share the DSL, the commands and the type names, and nothing else: a Classic page is Ext JS, addressed
by predictable DOM ids rather than `element-name`. The page's generation decides which `Text` or
`Lookup` an element is, so a Freedom-only type on a Classic page is refused when the configuration
loads, with the message naming the generation it does exist for.

A Classic page needs its `schema`, the page schema name. Every control id on the page starts with it:
`ContactPageV2NameTextEdit-el` is the input of the element named `Name` on `ContactPageV2`, rendered
by a `TextEdit`; `-wrap` is its box and `ContactPageV2NameLabel` its caption. The DSL addresses the
element by `code`; when the element's name in the schema's `diff` differs from the code, the page
file carries it as `name`. Pages built with the section wizard name their fields
`LOOKUP<guid>` or `STRING<guid>`, so the page file maps a readable code onto that:

```jsonc
// Configuration/pages/OrderClassic.json
{
  "name": "OrderClassic",
  "url": "/0/Shell/#CardModuleV2/UsrOrder1Page",
  "type": "ClassicUI",
  "schema": "UsrOrder1Page",
  "elements": [
    { "code": "UsrName",   "label": "Number",   "type": "Text" },
    { "code": "UsrType",   "label": "Type",     "type": "Lookup",   "name": "LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4" },
    { "code": "UsrDate",   "label": "Date",     "type": "DateTime", "name": "DATETIME7fa68919-649f-46d7-8f33-3149f642c4a3" },
    { "code": "CloseButton", "label": "Close",  "type": "Button" },
    { "code": "GeneralInfoTab", "label": "General", "type": "Tab" },
    { "code": "Tabs", "label": "", "type": "TabPanel", "tabs": ["General", "Products"] },
    { "code": "Stage", "label": "", "type": "CasesBar", "values": ["New", "In progress", "Done"], "subvalues": { "Done": ["Done", "Cancelled"] } },
    { "code": "Products", "label": "Products", "type": "Detail", "name": "UsrOrderProductDetail", "columns": ["Product", "Quantity"] },
    { "code": "AddProduct", "label": "", "type": "Button", "name": "AddRecordButton", "detail": "UsrOrderProductDetail" }
  ]
}
```

| `type` | Classic control(s) | Value kind | Extra members |
| --- | --- | --- | --- |
| Text | `TextEdit` | Text | `name`; `pinned` |
| RichText | `MemoEdit` | Text | `name`; `pinned` |
| Integer | `IntegerEdit` | Number | `name`; `pinned` |
| Decimal | `FloatEdit`, `MoneyEdit` | Number | `name`; `pinned` |
| Boolean | `CheckBoxEdit` | Boolean | `name`; `pinned` |
| Date | `DateEdit` | DateTime | `name`; `pinned` |
| Time | `TimeEdit` | DateTime | `name`; `pinned` |
| DateTime | `DateEdit` + `TimeEdit` | DateTime | `name`; `pinned` |
| Lookup | `LookupEdit`, `ComboBoxEdit` | Lookup | `name`; `pinned` |
| Button | `{Prefix}{Name}Button-wrapperEl` / `-textEl` | None | `name`, `detail`, `settleMs`; `pinned` |
| Tab | `li[data-item-index]` of `{Schema}TabsTabPanel` | None | `name`, `panel`, `settleMs`; `pinned` |
| TabPanel | `{Schema}{Name}TabPanel` | Tab | `tabs`, `settleMs`; `pinned` |
| Detail | `{Detail}Container`, grid `grid-{Detail}DataGridGrid-wrap` | Number (row count) | `name`, `columns`; `pinned` |
| CasesBar | `SectionActionsDashboardActionsControlContainer` (`BaseStageControl`) | Stage | `values`, `subvalues`, `container`, `settleMs`; `pinned` |

A `DateTime` column renders two controls under one name, a date and a time; the element writes both
and reads one value. A `Decimal` covers both the float and the money control because a money field
is a float with a currency behind it. A `Lookup` covers the drop-down list and the searchable
lookup: both open a list under the input when text is typed, and `set` types the value and picks
the item whose caption matches exactly.

The DSL speaks ISO dates and invariant numbers whatever the user's culture. Classic shows
`06.11.2026` and `12,50` to a Ukrainian user, so typed fields are read from the page's view model
rather than from the rendered text, and a date is written in the culture the page itself reports
(`Terrasoft.Resources.CultureSettings`), never from a format hard-coded in the framework.

Tabs have no ids: the headers are `li` items whose `data-item-index` is the tab's position in the
page's `TabsCollection`, hidden tabs counted. A tab is therefore addressed by its name in the schema
(`GeneralInfoTab`, `Tabdebe3cb6TabLabel`), and the framework turns the name into the index through
the view model. The panel is `Tabs` unless `panel` says otherwise. Content of a visited tab stays in
the DOM, hidden, when another tab is opened; a control on a tab that was never opened is absent.

Classic renders tabs in one shape, but the DSL offers both views of it, as it does for Freedom UI:
a `Tab` is one tab by its schema name, for `click`; a `TabPanel` (named `Tabs` on nearly every
page) is the panel as one element whose value is the open tab's caption, for `set` and `check-value`:

```
click GeneralInfoTab                 # by schema name
set Tabs "Products"                  # by caption
check-value Tabs eq "Products"
```

A `Detail` is the grid of related records, addressed by the **detail schema name**
(`UsrOrderProductDetail`), because everything inside a detail is prefixed by that schema rather
than by the page. Its value is its row count, read from the grid's loaded collection, so
`check-value Products eq "3"` and `check-value Products gt "0"` read naturally and `set` is
refused. `check Products columns` compares the rendered captions with `columns`, and
`check Products is editable` tells a detail that takes edits in place (Classic renders it as a
configuration grid) from one that opens a page per record. A detail's own buttons are ordinary
`Button` elements with the `detail` option (`UsrOrderProductDetailAddRecordButtonButton-wrapperEl`),
and their `name` is the full schema name of the button, `AddRecordButton`, as it is for page
buttons such as `CloseButton`. A detail on a tab that was never opened is absent, as any control is.

A detail's rows are addressed by their cells: `check-row Products Product=Pump exists`,
`select-row`, `delete-row`. A detail that edits in place also takes `add-record Products set
Product=Pump;Quantity=2`: the row the add button opens is filled through the editors it renders (a
lookup through its list, a checkbox by a click, a date in the page's culture) and saved with the
row's own save action. **A detail saves its own records**: a row added or deleted is in the database
at once, whatever the page's Save button does afterwards. A detail that opens a page per record
takes `click` on its add button followed by `on <page>`.

The page's own `SaveButton` takes `"isSave": true`, as in Freedom UI: after the click, a message
box is read as the reason the save failed, and the button has to leave the toolbar, which is how
Classic shows a clean page. `DiscardChangesButton` is the Cancel that appears beside it.

The `CasesBar` is the stage bar of the actions dashboard (the DCM bar). It is one shared module,
so it needs no schema-derived id and the page file says only `"type": "CasesBar"`. Its value is the
committed stage as the page's stage column holds it, which is the sub-stage when one is selected;
`set` clicks a stage, or opens the parent's menu for a sub-stage, answers the confirmation the page
may ask, and waits. **In Classic a stage change is not an edit: the dashboard validates the page and
saves the record at once**, so `set` on the stage bar writes to the database even though nothing
else on the page did. `values` and `subvalues` are documentation the analyser fills in; the element
asks the control itself which stage hides which sub-stages. A stage the case does not allow from the
current one is refused as not enabled. A message the page shows on the way -- a validation error
naming an empty required field, a note that the move went through, a modal asking for something
first -- is dismissed and quoted: in the log when the stage still changed, in the failure when it did
not, so a case never fails with a bare "holds X" when the page said why.

`analyse` describes a Classic page when the address is a `#CardModuleV2/<Schema>` one, and
`schemas --classic` lists the instance's Classic pages (`...Page`, `...PageV2`, `...1Page`) with the
address to paste. Rather than reading the DOM alone, the analyser asks each control's Ext component
for the column it binds and the column's type, so the page file it writes names a field by its
column (`QSTypeCargo`) and records the schema name (`LOOKUPceebb14a-...`) as `name`. A second element
bound to the same column, typically a copy of a header field on another tab, keeps its schema name as
its code. A DateTime column's date and time controls become one `DateTime` element; details are named
by their schema and carry their `columns`; a detail's buttons are `Button` elements coded detail
schema plus button name, with `detail` set; the tab panel becomes `Tabs` with its captions and the
stage bar `Stage` with its `values` and `subvalues`. Every tab is opened in turn, because a tab
renders its content on first opening, and the tab the page arrived on is restored. `SaveButton` and
`DiscardChangesButton` are always written although a clean page does not render them: BasePageV2
gives them to every page, and they appear the moment the page is dirty.

Three things a Classic page does that look like test failures and are not. Once a page has unsaved
changes, Classic replaces its Close button with Save and Cancel, so `check CloseButton exists`
holds before the first `set` and `check CloseButton not exists` after it. A business rule may lock
a field the moment it gets a value, so a `set` that succeeds can be followed by a `clear` that is
refused as read-only; the refusal names the field. And the actions dashboard and the details are
modules of their own that finish loading after the page's fields, so the stage bar and the details
wait for their own content before answering rather than relying on the page being ready.

## Values

Values cross command layers as text. Dates accept `yyyy-MM-ddTHH:mm:ss`, `yyyy-MM-ddTHH:mm`, or `yyyy-MM-dd`; date values are returned as `yyyy-MM-ddTHH:mm:ss`.

| Target kind | Accepted copied source kinds |
| --- | --- |
| Text | Text, Number, Boolean, DateTime, Lookup, Stage |
| Number | Number |
| Boolean | Boolean |
| DateTime | DateTime |
| Lookup | Text, Lookup |
| Stage | Text, Lookup, Stage |

Boolean literals are exactly `true` or `false`; numbers use invariant decimal syntax.

## Finding A Page

Analysing a page needs its schema name, and the name is rarely what you would guess: contacts live on
`Contacts_FormPage`, plural, while `ContactPageV2` is the classic UI page for the same entity. Nothing
in the browser lists them, so the runner reads them over OData:

```bash
creatio-tests schemas Contact          # form pages whose name contains "Contact"
creatio-tests schemas                  # every form page on the instance
creatio-tests schemas --list-pages     # section pages instead
creatio-tests schemas Order --classic  # Classic UI pages (ContactPageV2, UsrOrder1Page ...)
```

Each result comes with the address that opens it, ready to paste into `analyse`:

```
  Contacts_FormPage
      /0/Shell/#Card/Contacts_FormPage/edit/<recordId>
```

## Analysis Phase

A run has two phases. Analysis scripts live in the folders named by `AnalysisFolders` and run first;
test cases live in `CasesFolders` and run second. Between the phases the element catalog is reloaded
from the pages directory, so the cases are validated against the configuration analysis has just
written rather than whatever was on disk when the process started.

```jsonc
// creatio.env.config.json
"CasesFolders":    ["./Cases"],
"AnalysisFolders": ["./Analysis"]
```

**Analysis is off by default.** Once a page is described its configuration is committed and reused,
so re-analysing on every run costs a browser session per page and tells you nothing new. A pipeline
runs the cases alone; analysis is for the moment a page has actually changed.

```bash
creatio-tests                  # cases only, analysis skipped
creatio-tests --analyse        # analysis first, then the cases against fresh configuration
creatio-tests --analyse-only   # describe the pages and stop
```

`RunAnalysis: true` in `run.config.json` turns it on without a flag. A failed analysis script stops
the run before the cases: configuration they were meant to write is missing, so any result the cases
produced would be misleading.

The analyser opens every tab by default. Freedom UI renders only the open tab, and Creatio remembers
which tab was open when the page was closed, so neither the initial tab nor controls on closed tabs
can be assumed. The analyser restores the tab that was open on arrival after walking the others.

## Element Types The Analyser Cannot Settle

Date, time, and date-time controls share a host tag. The analyser uses the picker icon first
(`Calendar`, `Time`, or `Date/Time`), then the displayed value. For an editable empty control with
no picker icon it reports: `the control is empty and shows no picker icon; open the picker to see
which it is, then set the type and mark the entry "pinned": true`.

A read-only empty date/time control has neither icon nor value, so its exact type cannot be
established. It is reported as `read-only and empty, so the exact type cannot be established;
presence and caption are still assertable`. Keep it as a presence-only element: a test can use
`check <code> exists`, `check <code> is readonly`, and caption matching, but cannot make a
value-type assertion that depends on whether it is Date, Time, or DateTime.

## Configuration Files

All paths are relative to the working directory.

| File | Fields |
| --- | --- |
| `creatio.env.config.json` (copy `creatio.env.config.example.json`) | `BaseUrl`, optional `AuthUrl`, `Users` (`Username`, `Password`, optional `Name`: what a case writes after `use` when the login should stay out of the cases), optional `CasesFolders`, `AnalysisFolders` and `ProcessCasesFolders` |
| `creatio.site.config.json` | `AuthPath`, optional `ODataBasePath`, optional `ProcessStartPath` (default `/0/ServiceModel/ProcessEngineService.svc/RunProcess`) |
| `processes/*.json` | one file per process a case may start: `name` and `parameters` (`code`, `type`, `direction`) |
| `pages/*.json` | one file per page: `name`, `url`, `type` (`FreedomUI`, the default, or `ClassicUI`), `schema` (Classic only: the page schema name), and `elements` (`code`, `label`, `type`, optional `pinned`, plus type-specific options such as a Classic element's `name`). The page is implied by the file, so elements never repeat it. |
| `objects.config.json` | `objects`: `name`, `sectionPage`, `formPage`, `newButton`, `saveButton`, `fields` mapping codes to static-value names |
| `static.values.json` | object mapping static names to scalar values |
| `run.config.json` | `headless`, `continueOnFailure`, `runAnalysis`, `viewportWidth`, `viewportHeight`, `elementTimeoutMs`, `absenceTimeoutMs`, `pageLoadTimeoutMs`, `reportsDirectory`, `reports`, `skipFilePrefix` |

`viewportWidth` and `viewportHeight` (1600 x 950 by default) fix the size of the browser window every
case runs in. This is part of a run's definition rather than a property of whoever's machine it runs
on: Creatio lays a page out differently in a narrow window, to the point where a side panel's hover
actions overlap the tab headers and a tab cannot be clicked at all, and controls further down a page
are never rendered. Analysis uses the same size, so what it records and what a case can reach agree.

```jsonc
// Configuration/pages/ContactForm.json -- one file per page
{
  "name": "ContactForm",
  "url": "/0/Shell/#Card/Contacts_FormPage",
  "type": "FreedomUI",
  "elements": [
    { "code": "JobTitle", "label": "Full job title", "type": "Text" },
    { "code": "Account", "label": "Account", "type": "Lookup" },
    { "code": "GeneralInfoTab", "label": "Contact info", "type": "Tab" },
    { "code": "CloseButton", "label": "Close", "type": "Button" }
  ]
}
```

Every `.json` file under `Configuration/pages` is loaded, including subdirectories, so pages can be
grouped however suits the project. The file name is only a label; a page's identity is its `name`.

## Validation

The first pass checks all lines before a browser action runs: unknown commands, malformed arguments, absent pages/elements, unsupported capabilities, incompatible copies, invalid literals, and object workflow references.

```text
Unknown command 'opne'. Known commands: add-record, analyse, check, check-value, click, clear, create, end, goto, open, set, use, wait.
Element 'X' on page 'ContactForm': Unknown element type 'Slider' for a FreedomUI page. Known types for FreedomUI pages: Boolean, Button, CasesBar, DataGrid, Date, DateTime, Decimal, Email, Integer, Link, Lookup, Phone, RichText, Tab, Text, Time, ToggleTab.
Element 'Email' on page 'OrderClassic': Unknown element type 'Email' for a ClassicUI page. It exists for FreedomUI pages only. Known types for ClassicUI pages: Boolean, Button, Date, DateTime, Decimal, Integer, Lookup, RichText, Tab, Text, Time.
'wait' expects the unit 'second' or 'minute'.
```

## Recipes

To add a command, copy [ClickCommand.cs](../src/CreatioAutoTests.Core/Commands/ClickCommand.cs), select or implement an `ICommandValidator`, declare its capability interfaces in `SupportedTargets`, and add tests. Reflection discovers the command; no registry, enum, or switch changes.

To add an element, copy [TextField.cs](../src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose a suitable base, apply `[UiElement]`, and provide an options class where needed. Reflection discovers the element; no registry, enum, or switch changes.

To add a Classic element, copy [Classic/Fields/TextField.cs](../src/CreatioAutoTests.Core/Frontend/Classic/Fields/TextField.cs) instead: `Generation = UiGeneration.ClassicUI` and the `Controls` it renders as take the place of a host tag, and the same type name may already exist for Freedom UI.