# creatio-tests MCP server

Gives an agent the utility without a shell: read the DSL and the cases, validate, run, analyse a
page, list schemas, read the last report. Nothing here writes a case or a page file - an agent
in the tester role gets this server and nothing else, so it can prove but not change.

| Tool | What it does |
| --- | --- |
| `overview()` | where things are, the described pages and processes, the loop |
| `dsl()` | `docs/dsl.md` |
| `cases(folder)`, `case(name)` | the `.act` files and their text |
| `page_config(name)`, `process_config(name)` | `Configuration/pages/<name>.json`, `Configuration/processes/<name>.json` |
| `validate(filter)` | `--validate-only`, no environment needed |
| `run(filter, verbose)` | runs the cases, returns the report |
| `analyse(page_path, name, include_hidden, dry_run)` | describes a live page |
| `schemas(text, list_pages)` | the instance's page schemas |
| `last_report()` | the newest `Reports/run-*.txt` |

## Install

```powershell
dotnet build                                            # the tools run the built creatio-tests.exe
python -m venv mcp-server\.venv
mcp-server\.venv\Scripts\python.exe -m pip install "mcp>=2.0"
claude mcp add --scope user creatio-tests -- <repo>\mcp-server\.venv\Scripts\python.exe <repo>\mcp-server\server.py
```

The repository root is the parent of this folder (`CREATIO_TESTS_ROOT` overrides it); a run may
take minutes, `CREATIO_TESTS_TIMEOUT` (seconds, default 1800) bounds it. The environment and
users come from the repository's `Configuration/creatio.env.config.json`, as for the command line.

Check: `mcp-server\.venv\Scripts\python.exe mcp-server\test_server.py` (calls the tools as
functions; `validate` runs the real command line).
