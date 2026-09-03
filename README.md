# CreatioAutoTests

## What It Is

CreatioAutoTests is a DSL for testing Creatio Freedom UI. Test cases are `.act` files, not C#; the runner validates every line before it opens a browser.

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
- A reachable Creatio instance
- Chromium for Playwright: `playwright install chromium`

## Getting Started

```powershell
git clone <repository-url>
Set-Location automatic-creatio-tests
playwright install chromium
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only
```

Set the Creatio URL and users in [Configuration/creatio.env.config.json](Configuration/creatio.env.config.json). Each page has its own JSON file under [Configuration/pages](Configuration/pages), containing its URL and elements. Use `analyse` to produce or refresh a page file from a live page, then add an `.act` file under [Cases](Cases) and run it:

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only --filter contact-form-baseline
```

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
| Frontend | Freedom UI element implementations |
| Validation | Static command and target checking |
| Commands | Reflection-discovered DSL keywords |
| Configuration and session | Files, catalog, authentication, browser context |
| Execution | Parsing, plans, and command execution |
| Runner | CLI and NUnit-facing orchestration |
| Analysis | Reads live Freedom UI pages and writes their per-page configuration |

The first pass parses and validates a complete case. Only a valid plan reaches the second pass, which drives Playwright. When analysis is selected, analysis scripts run first and the catalog is reloaded before cases start. See [docs/dsl.md](docs/dsl.md) for the language reference.

## Extending It

To add a command, copy [src/CreatioAutoTests.Core/Commands/ClickCommand.cs](src/CreatioAutoTests.Core/Commands/ClickCommand.cs), choose or add a validator, declare `SupportedTargets`, and add unit tests. No registry, enum, or switch is edited.

To add an element type, copy [src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs](src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose its base class, add `[UiElement]`, and add an options class when it accepts extra JSON members. No registry, enum, or switch is edited.

## Licence

MIT. See [LICENSE](LICENSE).