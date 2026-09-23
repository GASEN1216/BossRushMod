"""OfficialCompileListFileExistenceGuard: 官方编译脚本必须覆盖全部生产源码。"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

# 清单解析只有一份实现（tools/compile_list.py），本 guard 与离线语法探针共用，
# 避免两边正则漂移出「探针少检几个文件却不报错」这种静默失明。
from compile_list import normalize_source, parse_compile_sources, read_compile_text  # noqa: E402


COMPILE = Path("compile_official.bat")
EXCLUDED_DIRS = {
    ".git",
    ".codex_tmp",
    "Build",
    "tmp",
    "output",
    "outputs",
    "tests",
    "wiki-site",
    "鸭科夫源码",
}


def fail(message: str) -> int:
    print(message)
    return 1


def iter_compile_sources(text: str) -> set[str]:
    return set(parse_compile_sources(text))


def iter_production_sources() -> set[str]:
    sources: set[str] = set()
    for path in Path(".").rglob("*.cs"):
        if any(part in EXCLUDED_DIRS for part in path.parts):
            continue
        sources.add(normalize_source(path.as_posix()))
    return sources


def main() -> int:
    text = read_compile_text(COMPILE)
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

    # AGENTS.md §4.1 曾把文件数写死（483），实际早已 532，属会误导 AI 协作者的文档漂移。
    # 现在规则是「不写死数字，以本 guard 实测为准」——这里反过来断言文档里没有写死的数字。
    agents = Path("AGENTS.md")
    if agents.exists():
        section = agents.read_text(encoding="utf-8")
        marker = "### 4.1"
        start = section.find(marker)
        if start >= 0:
            body = section[start:start + 900]
            hardcoded = re.search(r"列出\s*(\d+)\s*个", body)
            if hardcoded:
                return fail(
                    "OfficialCompileListFileExistenceGuard: AGENTS.md §4.1 又把文件数写死成 "
                    + hardcoded.group(1)
                    + " 了（实际 " + str(len(compile_sources)) + "）。"
                    "请改回不写具体数字、以本 guard 实测为准。")

    print("OfficialCompileListFileExistenceGuard: PASS ("
          + str(len(compile_sources)) + " sources)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
