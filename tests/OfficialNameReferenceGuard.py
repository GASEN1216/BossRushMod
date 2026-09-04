"""Guard the official (base-game) names quoted by the in-game Wiki.

WikiContent/ is a player-facing encyclopedia. Whenever it mentions a base-game
Boss, NPC, buff or quest, it must use the name the player actually sees in the
game -- never a localization key, never a design-doc nickname. This guard reads
the shipped official localization table and asserts both directions:

  1. the key still maps to the zh/en name registered in
     tests/official_name_references.tsv (catches an official rename);
  2. the registered pages still contain the wording the registry says they use
     (catches Wiki drift);
  3. no player page contains the localization key itself (catches a key leaking
     back into player-facing copy).

Table resolution order: $GAME_PATH (the repo's existing convention, see
compile_official.bat) -> docs/官方本地化表/ snapshot -> skip.

Skipping is deliberate: the tables are base-game assets and are git-ignored
(.gitignore /docs/*), so a fresh clone or CI has neither. A hard failure there
would be noise, not signal -- see the .gitignore note about guards that read
docs/. This is documentation-only; it never loads Unity or scans runtime state.
"""

from pathlib import Path
import csv
import os
import sys


ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "tests" / "official_name_references.tsv"
WIKI = ROOT / "WikiContent"
SNAPSHOT = ROOT / "docs" / "官方本地化表"

TABLES = {
    "zh": "ChineseSimplified.csv",
    "en": "English.csv",
}

# 官方表相对游戏根目录的位置
GAME_RELATIVE = Path("Duckov_Data") / "StreamingAssets" / "Localization"


def fail(message):
    print("OfficialNameReferenceGuard: FAIL - " + message)
    return 1


def skip(message):
    print("OfficialNameReferenceGuard: SKIP - " + message)
    return 0


def unescape(value):
    """还原官方表的转义：`\\?` `\\ ` `\\.` `\\,` 分别是 ? 空格 . 和逗号。"""
    return (value
            .replace("\\?", "?")
            .replace("\\ ", " ")
            .replace("\\.", ".")
            .replace("\\,", ","))


def resolve_table_dir():
    """返回 (目录, 来源说明)。找不到返回 (None, None)。"""
    game_path = os.environ.get("GAME_PATH")
    if game_path:
        candidate = Path(game_path) / GAME_RELATIVE
        if all((candidate / name).is_file() for name in TABLES.values()):
            return candidate, "GAME_PATH"

    if all((SNAPSHOT / name).is_file() for name in TABLES.values()):
        return SNAPSHOT, "docs snapshot"

    return None, None


def load_table(path):
    """key -> 显示文本。同 key 多行时以第一行为准（官方表偶有重复行）。"""
    names = {}
    with path.open(encoding="utf-8", errors="replace", newline="") as handle:
        for row in csv.reader(handle):
            if len(row) < 2:
                continue
            key = row[0].strip()
            if not key or key.startswith("#"):
                continue
            names.setdefault(key, unescape(row[1]))
    return names


def load_registry():
    entries = []
    with REGISTRY.open(encoding="utf-8") as handle:
        for lineno, raw in enumerate(handle, 1):
            line = raw.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) != 6:
                raise ValueError("line %d: expected 6 tab-separated columns" % lineno)
            key, zh_name, en_name, zh_token, en_token, pages = (p.strip() for p in parts)
            # `=` 表示页面写法与官方名逐字相同
            if zh_token == "=":
                zh_token = zh_name
            if en_token == "=":
                en_token = en_name
            page_list = [] if pages == "-" else [p.strip() for p in pages.split(",") if p.strip()]
            entries.append((lineno, key, zh_name, en_name, zh_token, en_token, page_list))
    return entries


def main():
    if not REGISTRY.is_file():
        return fail("missing registry -> " + str(REGISTRY))

    try:
        entries = load_registry()
    except ValueError as error:
        return fail("malformed registry -> " + str(error))

    if not entries:
        return fail("registry is empty; it must list every official name the Wiki quotes")

    table_dir, source = resolve_table_dir()
    if table_dir is None:
        return skip(
            "official localization tables not found, so nothing was verified. Set "
            "GAME_PATH to your Escape from Duckov install, or copy the tables into "
            "docs/官方本地化表/ (see its README).")

    tables = {}
    for lang, filename in TABLES.items():
        try:
            tables[lang] = load_table(table_dir / filename)
        except OSError as error:
            return fail("cannot read official table -> " + str(error))

    for lineno, key, zh_name, en_name, zh_token, en_token, pages in entries:
        official = {"zh": zh_name, "en": en_name}
        page_token = {"zh": zh_token, "en": en_token}

        # 方向 1：官方表里这个 key 还是不是登记的那个名字
        for lang in ("zh", "en"):
            actual = tables[lang].get(key)
            if actual is None:
                return fail(
                    "registry line %d: key not in official %s table -> %s "
                    "(official removed or renamed the key)" % (lineno, lang, key))
            if actual != official[lang]:
                return fail(
                    "registry line %d: official %s name changed -> %s is now %r, "
                    "registry says %r (update the registry AND every Wiki page listed "
                    "on that line)" % (lineno, lang, key, actual, official[lang]))

        # 方向 2：登记的页面里还写着这个名字
        for page in pages:
            for lang in ("zh", "en"):
                path = WIKI / lang / (page + ".md")
                if not path.is_file():
                    return fail(
                        "registry line %d: page does not exist -> %s"
                        % (lineno, path.relative_to(ROOT)))
                text = path.read_text(encoding="utf-8")
                if page_token[lang] not in text:
                    return fail(
                        "registry line %d: %s no longer mentions the official %s name %r "
                        "for %s (Wiki drifted, or the mention moved to another page)"
                        % (lineno, path.relative_to(ROOT), lang, page_token[lang], key))

    # 反向卫生检查：玩家页面里不许出现登记表覆盖的本地化 key 本身。
    for lineno, key, _zh, _en, _zh_token, _en_token, _pages in entries:
        for lang in ("zh", "en"):
            for path in sorted((WIKI / lang).rglob("*.md")):
                if key in path.read_text(encoding="utf-8"):
                    return fail(
                        "%s leaks the localization key %r; player pages must use the "
                        "display name instead" % (path.relative_to(ROOT), key))

    print("OfficialNameReferenceGuard: PASS - %d official names verified against %s"
          % (len(entries), source))
    return 0


if __name__ == "__main__":
    sys.exit(main())
