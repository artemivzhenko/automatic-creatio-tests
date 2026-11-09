"""
MCP server exposing the Creatio UI test utility to an agent: the DSL reference, the cases and
configuration on disk, and the runner itself (validate, run, analyse, schemas). Every tool
either reads the repository or runs the command line; none writes a case or a page file - that
is the tester's side of the author/tester loop.

Run directly for stdio:  python server.py
The repository root is the parent of this folder, or CREATIO_TESTS_ROOT.
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

try:
    from mcp.server.mcpserver import MCPServer as _Server  # mcp >= 2.0
except ImportError:  # pragma: no cover - older SDK
    from mcp.server.fastmcp import FastMCP as _Server  # mcp 1.x

ROOT = Path(os.environ.get("CREATIO_TESTS_ROOT") or Path(__file__).resolve().parent.parent)
CLI_PROJECT = ROOT / "src" / "CreatioAutoTests.Cli"
RUN_TIMEOUT = int(os.environ.get("CREATIO_TESTS_TIMEOUT", "1800"))

mcp = _Server(
    "creatio-tests",
    instructions=(
        "The Creatio UI test utility: .act cases in a small DSL, validated in full before a "
        "browser opens, run against a Creatio instance (Freedom UI pages) - and process cases, "
        "the backend tier that starts business processes and checks their results and records "
        "without a browser. Read dsl() before writing a case; cases() and page_config() show what "
        "exists; validate(filter) lints without the environment; run(filter) executes and returns "
        "the report; analyse(path, name) describes a live page into Configuration/pages. The tools "
        "read and run only: a case or a page file is written by the author, not through here."
    ),
)


def _exe() -> list[str]:
    for configuration in ("Debug", "Release"):
        exe = CLI_PROJECT / "bin" / configuration / "net8.0" / ("creatio-tests.exe" if os.name == "nt" else "creatio-tests")
        if exe.is_file():
            return [str(exe)]
    return ["dotnet", "run", "--project", str(CLI_PROJECT), "--"]


def _cli(args: list[str], timeout: int = RUN_TIMEOUT) -> str:
    """Runs the command line from the repository root and returns its output, exit code included."""
    try:
        run = subprocess.run(_exe() + args, cwd=ROOT, capture_output=True, text=True, timeout=timeout, encoding="utf-8", errors="replace")
    except subprocess.TimeoutExpired:
        return f"timed out after {timeout} s: creatio-tests {' '.join(args)}"
    out = (run.stdout or "") + (("\n" + run.stderr) if run.stderr.strip() else "")
    return out.strip() + f"\n\nexit code {run.returncode}"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig") if path.is_file() else f"{path.relative_to(ROOT)} does not exist"


def _first_comment(path: Path) -> str:
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if line.startswith("#"):
            return line.lstrip("# ").strip()
        if line:
            break
    return ""


@mcp.tool()
def overview() -> str:
    """What this utility is, where its cases and configuration live, and the commands the other tools run."""
    pages = sorted(p.stem for p in (ROOT / "Configuration" / "pages").glob("*.json"))
    processes = sorted(p.stem for p in (ROOT / "Configuration" / "processes").glob("*.json"))
    return "\n".join([
        f"Creatio UI test utility at {ROOT}",
        "",
        "Cases: UI cases in Cases/ (open a page, set, check, check-value, click ...), process cases in",
        "ProcessCases/ (start, check-result, check-record; no browser), analysis scripts in Analysis/.",
        f"Pages described: {', '.join(pages) or '(none)'}",
        f"Processes described: {', '.join(processes) or '(none)'}",
        "",
        "Loop for a new case: dsl() for the syntax -> page_config(name) / process_config(name) for the",
        "codes -> write the .act file (author) -> validate(filter) -> run(filter) -> last_report().",
        "A page that is not described yet: schemas(text) to find its schema name, then",
        "analyse('/0/Shell/#Card/<Schema>/edit/<recordId>', name).",
        "",
        "The command line behind these tools: " + " ".join(_exe()),
    ])


@mcp.tool()
def dsl() -> str:
    """The DSL reference (docs/dsl.md): every command, the element types, values, process cases, configuration files."""
    return _read(ROOT / "docs" / "dsl.md")


@mcp.tool()
def cases(folder: str = "") -> str:
    """Lists the .act files with their first comment line, and the bundled .act.exp examples (not run, copy one to .act).
    folder: Cases, ProcessCases, Analysis or empty for all."""
    folders = [folder] if folder else ["Cases", "ProcessCases", "Analysis"]
    lines = []
    for name in folders:
        directory = ROOT / name
        if not directory.is_dir():
            continue
        lines.append(f"{name}/")
        for path in sorted(list(directory.glob("*.act")) + list(directory.glob("*.act.exp"))):
            lines.append(f"  {path.name:<34} {_first_comment(path)}" + ("  [example]" if path.name.endswith(".exp") else ""))
    return "\n".join(lines) or "no cases found"


@mcp.tool()
def case(name: str) -> str:
    """The text of one case or example: a file name with or without .act / .act.exp, searched in Cases, ProcessCases and Analysis."""
    stem = name.removesuffix(".exp").removesuffix(".act")
    for folder in ("Cases", "ProcessCases", "Analysis"):
        for suffix in (".act", ".act.exp"):
            path = ROOT / folder / f"{stem}{suffix}"
            if path.is_file():
                return _read(path)
    return f"no case named {name}"


@mcp.tool()
def page_config(name: str) -> str:
    """A page file from Configuration/pages: the codes, labels and element types a case may address on that page."""
    return _read(ROOT / "Configuration" / "pages" / f"{name}.json")


@mcp.tool()
def process_config(name: str) -> str:
    """A process file from Configuration/processes: the parameters, their types and directions a process case may use."""
    return _read(ROOT / "Configuration" / "processes" / f"{name}.json")


@mcp.tool()
def validate(filter: str = "") -> str:
    """Validates the cases without a browser or an environment: unknown codes, wrong operators, bad values. filter: part of a case name."""
    return _cli(["--validate-only"] + (["--filter", filter] if filter else []), timeout=300)


@mcp.tool()
def run(filter: str = "", verbose: bool = False) -> str:
    """Runs the cases against the configured environment and returns the report. filter: part of a case name; runs everything when empty."""
    args = (["--filter", filter] if filter else []) + (["--verbose"] if verbose else [])
    return _cli(args)


@mcp.tool()
def analyse(page_path: str, name: str, include_hidden: bool = False, dry_run: bool = False) -> str:
    """Describes a live page into Configuration/pages/<name>.json. page_path: '/0/Shell/#Card/<Schema>/edit/<recordId>'."""
    args = ["analyse", page_path, "--name", name, "--list"]
    if include_hidden:
        args.append("--include-hidden")
    if dry_run:
        args.append("--dry-run")
    return _cli(args, timeout=600)


@mcp.tool()
def schemas(text: str = "", list_pages: bool = False) -> str:
    """Finds the Freedom UI page schemas of the instance whose name contains text, with the address that opens each."""
    return _cli(["schemas"] + ([text] if text else []) + (["--list-pages"] if list_pages else []), timeout=120)


@mcp.tool()
def last_report() -> str:
    """The newest run report under Reports/."""
    reports = sorted((ROOT / "Reports").glob("run-*.txt")) if (ROOT / "Reports").is_dir() else []
    return _read(reports[-1]) if reports else "no report yet"


def main() -> None:
    if not CLI_PROJECT.is_dir():
        print(f"not a creatio-tests repository: {ROOT}", file=sys.stderr)
        sys.exit(2)
    mcp.run()


if __name__ == "__main__":
    main()
