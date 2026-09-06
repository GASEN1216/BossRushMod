"""Validate generated Wiki href/src destinations using browser URL resolution."""
from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit, unquote
import argparse
import json


class Page(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.refs = []
        self.ids = set()

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if values.get("id"):
            self.ids.add(values["id"])
        for key in ("href", "src"):
            if values.get(key):
                self.refs.append(values[key])


def check(dist, base):
    pages = {}
    for path in sorted(dist.rglob("*.html")):
        page = Page()
        page.feed(path.read_text(encoding="utf-8"))
        pages[path.resolve()] = page
    missing, fragments, checked = [], [], 0
    destinations = {}
    origin = "https://wiki-validation.invalid"
    for path, page in pages.items():
        relative = path.relative_to(dist).as_posix()
        # cleanUrls=true: index.html represents a directory, other pages omit .html.
        route = relative[:-10] if path.name == "index.html" else relative[:-5]
        current = origin + base + route
        for ref in page.refs:
            target_url = urlsplit(urljoin(current, ref))
            if target_url.scheme not in ("http", "https") or target_url.netloc != "wiki-validation.invalid":
                continue
            checked += 1
            decoded = unquote(target_url.path)
            if not decoded.startswith(base):
                missing.append([relative, ref, target_url.path, "outside configured base"])
                continue
            if decoded not in destinations:
                target = dist / decoded[len(base):]
                candidates = (target, Path(str(target) + ".html"), target / "index.html")
                destinations[decoded] = next((p.resolve() for p in candidates if p.is_file()), None)
            resolved = destinations[decoded]
            if resolved is None or not resolved.is_relative_to(dist):
                missing.append([relative, ref, target_url.path, "missing output"])
            elif target_url.fragment and resolved in pages:
                fragment = unquote(target_url.fragment)
                if fragment not in pages[resolved].ids and not fragment.startswith(":~:text="):
                    fragments.append([relative, ref, fragment])
    return {"html_count": len(pages), "refs_checked": checked,
            "missing": missing, "broken_fragments": fragments}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dist", default="wiki-site/docs/.vitepress/dist")
    parser.add_argument("--base", default="/BossRushMod/")
    parser.add_argument("--report", help="Optional JSON evidence output")
    args = parser.parse_args()
    dist = Path(args.dist).resolve()
    if not args.base.startswith("/") or not args.base.endswith("/"):
        parser.error("base must start and end with /")
    if not dist.is_dir():
        parser.error("Build output not found: " + str(dist))
    result = check(dist, args.base)
    if args.report:
        target = Path(args.report)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wiki links: {result['html_count']} pages / {result['refs_checked']} references / "
          f"{len(result['missing'])} missing / {len(result['broken_fragments'])} broken fragments")
    for problem in (result["missing"] + result["broken_fragments"])[:12]:
        print("FAIL " + repr(problem))
    return 1 if not result["html_count"] or result["missing"] or result["broken_fragments"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
