"""OfficialCompileListFileExistenceGuard: 官方编译脚本必须覆盖全部生产源码。"""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
EXCLUDED_DIRS = {
    ".git",
    ".codex_tmp",
    "Build",
    "tests",
    "wiki-site",
    "鸭科夫源码",
}


def fail(message: str) -> int:
    print(message)
    return 1


def normalize_source(path: str) -> str:
    return re.sub(r"/+", "/", path.replace("\\", "/")).lstrip("./")


def iter_compile_sources(text: str) -> set[str]:
    sources: set[str] = set()
    for match in re.finditer(r"([A-Za-z0-9_./\\-]+\.cs)(?=\s*(?:\^|\r?\n|$))", text):
        sources.add(normalize_source(match.group(1)))
    return sources


def iter_production_sources() -> set[str]:
    sources: set[str] = set()
    for path in Path(".").rglob("*.cs"):
        if any(part in EXCLUDED_DIRS for part in path.parts):
            continue
        sources.add(normalize_source(path.as_posix()))
    return sources


def main() -> int:
    text = COMPILE.read_text(encoding="utf-8")
    compile_sources = iter_compile_sources(text)

    missing: list[str] = []
    for source in sorted(compile_sources):
        if not Path(source).exists():
            missing.append(source)

    if missing:
        return fail("OfficialCompileListFileExistenceGuard: missing compile source(s): " + ", ".join(missing))

    production_sources = iter_production_sources()
    omitted = sorted(production_sources - compile_sources)
    if omitted:
        return fail("OfficialCompileListFileExistenceGuard: production source(s) omitted from compile_official.bat: " + ", ".join(omitted))

    print("OfficialCompileListFileExistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
