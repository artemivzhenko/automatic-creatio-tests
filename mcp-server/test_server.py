"""Checks for the MCP server's tools, called as plain functions:  python test_server.py"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import server  # noqa: E402


def test_reads():
    assert "## Process cases" in server.dsl()
    listing = server.cases()
    assert "Cases/" in listing and "ProcessCases/" in listing and "ia-processes.act" in listing
    assert "start iaScriptBuilt_Process" in server.case("ia-processes")
    assert server.case("nope").startswith("no case")
    assert '"name": "iaOrderForm"' in server.page_config("iaOrderForm")
    assert '"Verdict"' in server.process_config("iaScriptBuilt_Process")
    assert "does not exist" in server.page_config("Nope")
    assert "Loop for a new case" in server.overview()


def test_validate_runs_the_command_line():
    out = server.validate("ia-processes")
    assert "ok    ia-processes" in out and out.rstrip().endswith("exit code 0"), out


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
            print("ok", name)
