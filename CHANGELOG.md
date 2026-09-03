# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

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
