# CreatioAutoTests

## What It Is

CreatioAutoTests is a DSL for testing Creatio pages, Freedom UI and Classic UI alike. Test cases are `.act` files, not C#; the runner validates every line before it opens a browser.

## A Test Case At A Glance

```act
use Supervisor
open ContactForm for c4ed336c-3e9b-40fe-8b82-5632476472b4
check CloseButton exists
check GeneralInfoTab exists
check JobTitle is not readonly
```

## Requirements

- .NET 8 SDK
- Windows PowerShell or PowerShell 7, to run the Playwright bootstrapper
- Chromium, installed in step 2 below
- A reachable Creatio instance, and a user that can log in to it

Verified against Creatio 8.3.2 (.NET Framework, English interface) for Freedom UI, and against
Creatio 8.3.0 (.NET Framework, Ukrainian interface) for Classic UI pages served inside the Shell.
Other versions are untested; see [docs/dsl.md](docs/dsl.md) for the one place the interface language
matters to Freedom UI analysis.

## Getting Started

```powershell
git clone <repository-url>
Set-Location automatic-creatio-tests

# 1. Point the framework at a Creatio instance.
Copy-Item Configuration/creatio.env.config.example.json Configuration/creatio.env.config.json
#    then edit that file: BaseUrl, and a username and password that can log in.
#    It is gitignored, because it holds a password.

# 2. Build, then install the browser Playwright drives.
dotnet build
powershell -File src/CreatioAutoTests.Cli/bin/Debug/net8.0/playwright.ps1 install chromium

# 3. Check the bundled cases without opening a browser.
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only
```

The `playwright.ps1` bootstrapper is produced by the build, which is why step 2 is in that order.
There is no global `playwright` command to install.

Step 3 needs no Creatio instance: it parses and validates every case against the configuration. To
run cases for real you also need pages described under
[Configuration/pages](Configuration/pages) — one JSON file per page, holding its URL and its
elements. The bundled ones were produced from a Creatio 8.3 instance and are examples; describe your
own. Find the page's schema name first, then describe it:

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- schemas Contact
```

```
  Contacts_FormPage
      /0/Shell/#Card/Contacts_FormPage/edit/<recordId>
```

Then:

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- `
    analyse "/0/Shell/#Card/Contacts_FormPage/edit/<recordId>" --name ContactForm --list
```

Then write an `.act` file under [Cases](Cases) and run it. `.act` files are yours and stay out of
git (they name your environment's schemas); the bundled examples ship as `.act.exp`, which the
runner ignores - copy one to `.act` to run it.

## Running

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- --help
dotnet run --project src/CreatioAutoTests.Cli -- --root C:\path\to\workspace
dotnet run --project src/CreatioAutoTests.Cli -- --filter contact-form-baseline
dotnet run --project src/CreatioAutoTests.Cli -- --verbose
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only
dotnet run --project src/CreatioAutoTests.Cli -- analyse "/0/Shell/#Card/Contacts_FormPage/edit/<id>" --name ContactForm
dotnet run --project src/CreatioAutoTests.Cli -- --analyse
dotnet run --project src/CreatioAutoTests.Cli -- --analyse-only
```

`--validate-only` checks every selected case, configuration reference, command shape, page transition, element capability, and value compatibility without opening a browser.

`analyse` opens a page and writes `Configuration/pages/<name>.json`. `--analyse` runs configured analysis scripts before cases and reloads the catalog; `--analyse-only` runs that first phase without cases.

## How It Is Put Together

| Layer | Responsibility |
| --- | --- |
| Frontend | Element implementations: Freedom UI, and Classic UI under `Frontend/Classic` |
| Runtime | Per-generation page readiness: how the framework knows a page has finished rendering |
| Validation | Static command and target checking |
| Commands | Reflection-discovered DSL keywords |
| Configuration and session | Files, catalog, authentication, browser context |
| Execution | Parsing, plans, and command execution |
| Runner | CLI and NUnit-facing orchestration |
| Analysis | Reads a live page - Freedom UI or Classic UI, the address picks the analyser - and writes its per-page configuration |
| Process cases | The backend tier: `start`, `check-result`, `check-record` over the process engine service and OData, no browser |

The first pass parses and validates a complete case. Only a valid plan reaches the second pass, which drives Playwright. When analysis is selected, analysis scripts run first and the catalog is reloaded before cases start. See [docs/dsl.md](docs/dsl.md) for the language reference.

## Classic UI

A page file says which generation it is, and the same case syntax drives both:

```jsonc
{ "name": "OrderClassic", "url": "/0/Shell/#CardModuleV2/UsrOrder1Page", "type": "ClassicUI", "schema": "UsrOrder1Page",
  "elements": [ { "code": "UsrType", "label": "Type", "type": "Lookup", "name": "LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4" } ] }
```

```act
use Supervisor
open OrderClassic for 27bcc84f-727d-4314-8b0e-99b286253a27
check UsrType is required
set UsrType "Spare parts"
check-value UsrType eq "Spare parts"
```

Classic controls are addressed by the ids Creatio builds from the page schema and the element's
name in the schema; typed values are read from the page's view model, so dates and numbers compare
as ISO and invariant text whatever the user's culture shows. `analyse` describes a Classic page
when given a `#CardModuleV2/<Schema>` address, walking its tabs and naming each field by the column
it binds; `schemas --classic` lists the instance's Classic pages. See [docs/dsl.md](docs/dsl.md),
"Classic UI".

## Process Tests

Business processes are tested without a browser: a case under [ProcessCases](ProcessCases)
starts a process, reads its result parameters and checks the records it touched.

```act
use Supervisor
start UsrOrder_Process with Amount=500
check-result Verdict eq "big"
check-record UsrOrder UsrName=Order 1 exists
```

The process is described under [Configuration/processes](Configuration/processes) (its
parameters and their directions), so a wrong parameter or value fails in validation. See
[docs/dsl.md](docs/dsl.md), "Process cases".

## Cases That Write

A case may create the data it needs over OData, assert what the backend did - an event listener,
a trigger a SQL script created, a default - and clean up after itself:

```act
# writes
# ac: AC-01
use Supervisor
insert UsrOrder as Order set UsrName=Listener bed;UsrAmount=500
update UsrOrder $Order set UsrAmount=5000
check-record UsrOrderLog UsrOrder=$Order;UsrKind=Big exists
check-count UsrOrderLog UsrOrder=$Order eq 1
delete-record UsrOrderLog UsrOrder=$Order
```

`insert` binds the new record's Id to `$Order`; `remember <code> as $x` and
`remember-record <object> <filter> as $x` bind what a page shows or which record matched; every
record a case inserts is deleted when it ends unless the `insert` says `keep`. `# writes` makes
the case stop at its first failure and refuses an inserted alias that is neither deleted nor kept.
`# ac: AC-01` names the acceptance criterion: run with `--task-folder <dir>` and every case writes
`<dir>/evidence/AC-01-report-N.txt` with its outcome, failed steps and console errors, plus a draft
row of the tester's report table. `check console no errors` fails on browser errors of the pages
the case opened. See [docs/dsl.md](docs/dsl.md), "Cases that write".

## MCP Server

[mcp-server/](mcp-server/README.md) exposes the utility to an agent: the DSL, the cases and
page/process files, `validate`, `run` (with the evidence folder of a task), `analyse`, `schemas`
and the last report. It reads and runs only, so an agent that may test but not change gets this
server alone.

## Extending It

To add a command, copy [src/CreatioAutoTests.Core/Commands/ClickCommand.cs](src/CreatioAutoTests.Core/Commands/ClickCommand.cs), choose or add a validator, declare `SupportedTargets`, and add unit tests. No registry, enum, or switch is edited.

To add an element type, copy [src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs](src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose its base class, add `[UiElement]`, and add an options class when it accepts extra JSON members. No registry, enum, or switch is edited.

## Licence

MIT. See [LICENSE](LICENSE).