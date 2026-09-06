"""Execute the link auditor against in-memory generated pages; no site build needed."""
import importlib.util
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("wiki_links", ROOT / "tools/check_wiki_links.py")
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)


def run_case(files, base):
    dist = (ROOT / "Build/wiki-link-checker-memory").resolve()
    pages = {dist / relative: content for relative, content in files.items()}
    with patch.object(Path, "rglob", return_value=list(pages)), \
         patch.object(Path, "read_text", autospec=True, side_effect=lambda path, **kwargs: pages[path]), \
         patch.object(Path, "is_file", autospec=True, side_effect=lambda path: path in pages):
        return CHECKER.check(dist, base)


def main():
    checks = 0
    for base in ("/", "/BossRushMod/"):
        result = run_case({
            "guide/myindex.html": '<a href="#wrong-page-only">bad</a>',
            "guide/my.html": '<h2 id="wrong-page-only">different page</h2>',
        }, base)
        assert len(result["broken_fragments"]) == 1, "A name ending in index must retain its own route"
        assert not result["missing"]
        checks += 2
        result = run_case({
            "guide/index.html": '<h2 id="home">home</h2><a href="child#ok">child</a>',
            "guide/child.html": '<h2 id="ok">child</h2><a href="./#home">home</a>',
        }, base)
        assert result["refs_checked"] == 2
        assert not result["missing"] and not result["broken_fragments"], "Real index pages use directory URLs"
        checks += 2
    print(f"WikiLinkCheckerRegressionTests: PASS ({checks} checks)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
