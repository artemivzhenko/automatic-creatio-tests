# DSL Reference

## Syntax

Write one command per line. Blank lines and lines beginning with `#` are ignored. Double quotes preserve spaces and make a literal value; unquoted values may name static values or elements where the command permits them. The first command must be `use <user>`.

## Commands

| Command | Syntax | Target | Example |
| --- | --- | --- | --- |
| use | `use <user>` | none | `use testUser` |
| open | `open <page> [for <recordId>]` | none | `open ContactForm for c4ed336c-3e9b-40fe-8b82-5632476472b4` |
| check | `check <code> [not] exists` \| `check <code> is [not] required\|readonly` | `IExistable`; the state forms also need `IStateAware` | `check Input_Text is required` |
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

`check <code> exists` also verifies that the rendered caption matches the `label` in the page file under `Configuration/pages`; required and readonly are never asserted implicitly, they are separate lines.

`analyse` describes a page and writes `Configuration/pages/<pageName>.json`: every field, button,
tab and stage bar it can address, plus a list of controls that have no element type yet. Given an
address it navigates there itself, so the page does not have to be configured first; given no
argument it describes the page `open` left on screen.

Analysis and testing belong in separate scripts, in separate folders. A script is validated in full
before any of it runs, so elements `analyse` discovers are not addressable on the line below it --
but they are addressable by the test cases, because the run has two phases. See "Analysis phase"
below. The same work is available without a script as `creatio-tests analyse "<path>"`.

`create` follows its configured section, New button, form, fields, and Save button. `end` logs readable values from the current page without changing the outcome. `analyse` describes a live page and writes its page configuration; it can also be run as the CLI subcommand `analyse`.

## Element Types

| `type` | Creatio control | Value kind | Extra members |
| --- | --- | --- | --- |
| Button | `crt-button` | None | `isSave`, `doubleClick`, `settleMs`, `disappearTimeoutMs`; `pinned` |
| CasesBar | `crt-entity-stage-progress-bar` | Stage | `values`, `subvalues`; `pinned` |
| Tab | `crt-tab-panel-header-item` | None | `pinned` |
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
| `creatio.env.config.json` | `BaseUrl`, optional `AuthUrl`, `Users` (`Username`, `Password`), optional `CasesFolders` and `AnalysisFolders` |
| `creatio.site.config.json` | `AuthPath`, optional `ODataBasePath`, optional `ProcessStartPath` |
| `pages/*.json` | one file per page: `name`, `url`, optional `type`, and `elements` (`code`, `label`, `type`, optional `pinned`, plus type-specific options). The page is implied by the file, so elements never repeat it. |
| `objects.config.json` | `objects`: `name`, `sectionPage`, `formPage`, `newButton`, `saveButton`, `fields` mapping codes to static-value names |
| `static.values.json` | object mapping static names to scalar values |
| `run.config.json` | `headless`, `continueOnFailure`, `runAnalysis`, `elementTimeoutMs`, `absenceTimeoutMs`, `pageLoadTimeoutMs`, `reportsDirectory`, `reports`, `skipFilePrefix` |

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
`ai_helpers/analyse.py` writes these files from a live page.

## Validation

The first pass checks all lines before a browser action runs: unknown commands, malformed arguments, absent pages/elements, unsupported capabilities, incompatible copies, invalid literals, and object workflow references.

```text
Unknown command 'opne'. Known commands: analyse, check, check-value, click, clear, create, end, goto, open, set, use, wait.
Element 'X' has unknown type 'Slider'. Known types: Boolean, Button, CasesBar, Date, DateTime, Decimal, Email, Integer, Link, Lookup, Phone, RichText, Tab, Text, Time.
'wait' expects the unit 'second' or 'minute'.
```

## Recipes

To add a command, copy [ClickCommand.cs](../src/CreatioAutoTests.Core/Commands/ClickCommand.cs), select or implement an `ICommandValidator`, declare its capability interfaces in `SupportedTargets`, and add tests. Reflection discovers the command; no registry, enum, or switch changes.

To add an element, copy [TextField.cs](../src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose a suitable base, apply `[UiElement]`, and provide an options class where needed. Reflection discovers the element; no registry, enum, or switch changes.