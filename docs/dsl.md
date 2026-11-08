# DSL Reference

## Syntax

Write one command per line. Blank lines and lines beginning with `#` are ignored. Double quotes preserve spaces and make a literal value; unquoted values may name static values or elements where the command permits them. The first command must be `use <user>`.

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
| `creatio.env.config.json` (copy `creatio.env.config.example.json`) | `BaseUrl`, optional `AuthUrl`, `Users` (`Username`, `Password`), optional `CasesFolders`, `AnalysisFolders` and `ProcessCasesFolders` |
| `creatio.site.config.json` | `AuthPath`, optional `ODataBasePath`, optional `ProcessStartPath` (default `/0/ServiceModel/ProcessEngineService.svc/RunProcess`) |
| `processes/*.json` | one file per process a case may start: `name` and `parameters` (`code`, `type`, `direction`) |
| `pages/*.json` | one file per page: `name`, `url`, optional `type`, and `elements` (`code`, `label`, `type`, optional `pinned`, plus type-specific options). The page is implied by the file, so elements never repeat it. |
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
Element 'X' has unknown type 'Slider'. Known types: Boolean, Button, CasesBar, Date, DateTime, Decimal, Email, Integer, Link, Lookup, Phone, RichText, Tab, Text, Time.
'wait' expects the unit 'second' or 'minute'.
```

## Recipes

To add a command, copy [ClickCommand.cs](../src/CreatioAutoTests.Core/Commands/ClickCommand.cs), select or implement an `ICommandValidator`, declare its capability interfaces in `SupportedTargets`, and add tests. Reflection discovers the command; no registry, enum, or switch changes.

To add an element, copy [TextField.cs](../src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose a suitable base, apply `[UiElement]`, and provide an options class where needed. Reflection discovers the element; no registry, enum, or switch changes.