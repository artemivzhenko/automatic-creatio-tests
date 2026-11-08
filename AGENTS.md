# AGENTS.md

Orientation for an AI agent working in this repository. Read this before changing anything.

## What this project is

A test framework for Creatio Freedom UI. Tests are **not** written in C#. They are written in a small
DSL, one command per line, in `.act` files:

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
dotnet test                                                       # 141 tests, no browser needed
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
| 1 | `CreatioAutoTests.Frontend` | UI elements, one class per element type, addressed by `element-name` |
| 2 | `CreatioAutoTests.Validation` | Command shape checking and static target resolution |
| 3 | `CreatioAutoTests.Commands` | One class per DSL keyword, discovered by reflection |
| 4 | `CreatioAutoTests.Configuration`, `.Session` | Config loading, catalog, authentication, browser contexts |
| 5 | `CreatioAutoTests.Execution` | Parser, execution plan, command executor |
| 6 | `CreatioAutoTests.Runner` | Wires it together; the CLI and an NUnit fixture are thin shells |
| — | `CreatioAutoTests.Analysis` | Reads a live page and writes its configuration file |

A lower layer never references a higher one.

## Four invariants

Breaking any of these is a design error, not a style preference.

**1. Registration is by attribute, never by editing a list.**
`[UiElement("Lookup", HostTag = "crt-combobox", ValueKind = ValueKind.Lookup)]` and
`[Command("click", ...)]` are discovered by reflection. Adding an element type or a command is adding
*one class*. If a change seems to need an edit to a registry, a factory, an enum or a switch, you have
misread the task — stop and say so.

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
| `src/CreatioAutoTests.Cli` | the runner: run cases, `analyse` a page, or list `schemas` |
| `tests/` | unit tests, no browser |
| `Cases/`, `Analysis/` | `.act` scripts: test cases, and scripts that describe pages |
| `ProcessCases/` | `.act` scripts of the backend tier: `start` a process, `check-result`, `check-record`; no browser |
| `Configuration/pages/` | one file per page, holding the page and its elements |
| `Configuration/processes/` | one file per process a case may start: parameters, types, directions |
| `copilot/tasks/` | briefs for delegated work — gitignored, present only in the working tree |
| `backlog/` | the framework's own backlog — gitignored, same |
| `source/` | the pre-rewrite framework, kept for reference, gitignored |

## Before you finish

`dotnet build` and `dotnet test` must both be clean. If you cannot finish, say what you did and what
remains — a half-applied change is worse than none, and reverting cleanly is a valid outcome.
