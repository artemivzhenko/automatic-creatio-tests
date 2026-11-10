"""Checks for the MCP server's tools, called as plain functions:  python test_server.py"""

import sys
import shutil
import tempfile
import json
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import server  # noqa: E402


def test_reads():
    assert "## Process cases" in server.dsl()
    listing = server.cases()
    assert "Cases/" in listing and ".act.exp" in listing and "[example]" in listing
    process_examples = server.cases("ProcessCases")
    assert "order-process.act.exp" in process_examples and "[example]" in process_examples
    first_case = next(p for p in sorted((server.ROOT / "Cases").glob("*.act.exp")))
    assert "use " in server.case(first_case.name.removesuffix(".act.exp")) and "use " in server.case(first_case.name)
    assert server.case("nope").startswith("no case")
    first_page = next(p for p in sorted((server.ROOT / "Configuration" / "pages").glob("*.json")))
    assert f'"name": "{first_page.stem}"' in server.page_config(first_page.stem)
    assert "does not exist" in server.page_config("Nope")
    assert "does not exist" in server.process_config("Nope")
    assert "Loop for a new case" in server.overview()


def test_validate_runs_the_command_line():
    root = server.ROOT
    with tempfile.TemporaryDirectory() as tmp:
        isolated = Path(tmp)
        (isolated / "Configuration" / "pages").mkdir(parents=True)
        (isolated / "Cases").mkdir()
        for name in ("creatio.site.config.json", "run.config.json", "objects.config.json", "static.values.json"):
            shutil.copyfile(root / "Configuration" / name, isolated / "Configuration" / name)
        shutil.copyfile(root / "Configuration" / "creatio.env.config.example.json",
                        isolated / "Configuration" / "creatio.env.config.json")
        shutil.copyfile(root / "Configuration" / "pages" / "OrderClassic.json",
                        isolated / "Configuration" / "pages" / "OrderClassic.json")
        shutil.copyfile(root / "Cases" / "classic-order-form.act.exp",
                        isolated / "Cases" / "classic-order-form.act")
        page = isolated / "Configuration" / "pages" / "OrderClassic.json"
        data = json.loads(page.read_text(encoding="utf-8-sig"))
        data["elements"].append({"code": "Код", "label": "Название", "type": "Text", "tab": "GeneralInfoTab"})
        page.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        (isolated / "Cases" / "unicode.act").write_text(
            'use Supervisor\nopen OrderClassic\nset Код "Тест"\n', encoding="utf-8")
        try:
            server.ROOT = isolated
            out = server.validate()
            page_out = server.page_config("OrderClassic")
        finally:
            server.ROOT = root
    assert "case(s) checked" in out and out.rstrip().endswith("exit code 0"), out
    assert "Название" in page_out
    assert "warning: Код lies on tab GeneralInfoTab" in out, out


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
            print("ok", name)
