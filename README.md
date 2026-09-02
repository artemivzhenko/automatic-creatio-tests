# CreatioAutoTests

## What It Is

CreatioAutoTests is a DSL for testing Creatio Freedom UI. Test cases are `.act` files, not C#; the runner validates every line before it opens a browser.

## A Test Case At A Glance

```act
use testUser
open TestPage for 0c48c818-08f0-4f70-9b58-be512227f3bf
set Input_Text "Sample Name 123"
check-value Input_Text eq "Sample Name 123"
click SaveButton
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

Set the Creatio URL and users in [Configuration/creatio.env.config.json](Configuration/creatio.env.config.json). Describe page URLs in [Configuration/pages.config.json](Configuration/pages.config.json), then configure their elements in [Configuration/elements.config.json](Configuration/elements.config.json). Add an `.act` file under [Cases](Cases), then run it:

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- --filter test-fields
```

## Running

```powershell
dotnet run --project src/CreatioAutoTests.Cli -- --help
dotnet run --project src/CreatioAutoTests.Cli -- --root C:\path\to\workspace
dotnet run --project src/CreatioAutoTests.Cli -- --filter test-fields
dotnet run --project src/CreatioAutoTests.Cli -- --verbose
dotnet run --project src/CreatioAutoTests.Cli -- --validate-only
```

`--validate-only` checks every selected case, configuration reference, command shape, page transition, element capability, and value compatibility without opening a browser.

## How It Is Put Together

| Layer | Responsibility |
| --- | --- |
| Frontend | Freedom UI element implementations |
| Validation | Static command and target checking |
| Commands | Reflection-discovered DSL keywords |
| Configuration and session | Files, catalog, authentication, browser context |
| Execution | Parsing, plans, and command execution |
| Runner | CLI and NUnit-facing orchestration |

The first pass parses and validates a complete case. Only a valid plan reaches the second pass, which drives Playwright. See [docs/dsl.md](docs/dsl.md) for the language reference.

## Extending It

To add a command, copy [src/CreatioAutoTests.Core/Commands/ClickCommand.cs](src/CreatioAutoTests.Core/Commands/ClickCommand.cs), choose or add a validator, declare `SupportedTargets`, and add unit tests. No registry, enum, or switch is edited.

To add an element type, copy [src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs](src/CreatioAutoTests.Core/Frontend/Fields/TextField.cs), choose its base class, add `[UiElement]`, and add an options class when it accepts extra JSON members. No registry, enum, or switch is edited.

## Licence

See [source/LICENSE](source/LICENSE).