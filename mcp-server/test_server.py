"""Checks for the MCP server's tools, called as plain functions:  python test_server.py"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import server  # noqa: E402


def test_reads():
    assert "## Process cases" in server.dsl()
    listing = server.cases()
    assert "Cases/" in listing and ".act" in listing
    first_case = next(p for p in sorted((server.ROOT / "Cases").glob("*.act")))
    assert "use " in server.case(first_case.stem)
    assert server.case("nope").startswith("no case")
    first_page = next(p for p in sorted((server.ROOT / "Configuration" / "pages").glob("*.json")))
    assert f'"name": "{first_page.stem}"' in server.page_config(first_page.stem)
    assert "does not exist" in server.page_config("Nope")
    assert "does not exist" in server.process_config("Nope")
    assert "Loop for a new case" in server.overview()


def test_validate_runs_the_command_line():
    out = server.validate()
    assert "case(s) checked" in out and out.rstrip().endswith("exit code 0"), out


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
            print("ok", name)
