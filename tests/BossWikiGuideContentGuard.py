"""Guard the six player-facing custom-boss guides and their generated copies."""

from pathlib import Path
import re
import sys


ROOT = Path(".")
BOSSES = {
    "boss__dragon_descendant": "dragon-descendant.md",
    "boss__dragon_king": "dragon-king.md",
    "boss__phantom_witch": "phantom-witch.md",
}

REQUIRED_SECTIONS = {
    "zh": (
        ("概述", re.compile(r"^###\s+概述\s*$", re.MULTILINE)),
        ("基础数据", re.compile(r"^###\s+基础数据\s*$", re.MULTILINE)),
        (
            "技能或阶段",
            re.compile(r"^###\s+(?:攻击技能|战斗阶段|阶段划分)\s*$", re.MULTILINE),
        ),
        ("掉落", re.compile(r"^###\s+掉落物?\s*$", re.MULTILINE)),
        ("战斗策略", re.compile(r"^###\s+战斗策略\s*$", re.MULTILINE)),
        ("出现限制", re.compile(r"^###\s+出现限制\s*$", re.MULTILINE)),
    ),
    "en": (
        ("overview", re.compile(r"^###\s+Overview\s*$", re.MULTILINE)),
        ("base stats", re.compile(r"^###\s+Base Stats\s*$", re.MULTILINE)),
        (
            "skills or phases",
            re.compile(r"^###\s+(?:Attack Skills|Combat Phases|Phase Thresholds)\s*$", re.MULTILINE),
        ),
        ("drops", re.compile(r"^###\s+Drops\s*$", re.MULTILINE)),
        (
            "combat advice",
            re.compile(r"^###\s+(?:Combat Strategy|Combat Tips)\s*$", re.MULTILINE),
        ),
        (
            "spawn restrictions",
            re.compile(r"^###\s+Spawn (?:Limits|Restrictions)\s*$", re.MULTILINE),
        ),
    ),
}


def fail(message: str) -> int:
    print("BossWikiGuideContentGuard: FAIL - " + message)
    return 1


def normalize_generated_content(raw: str) -> str:
    """Ignore only the heading and callout markup changed by the site sync."""
    normalized = []
    for line in raw.splitlines():
        line = re.sub(r"^#{1,6}\s+", "", line)
        line = re.sub(r"^\[(?:tip|warn)\]\s*", "", line)
        if line.strip() in {"::: tip", "::: warning", ":::"}:
            continue
        line = re.sub(r"\[([^\]]*)\]\(/[A-Za-z]:[^\)]*\)", r"\1", line)
        normalized.append(line.rstrip())
    return "\n".join(normalized).strip()


def main() -> int:
    catalog_path = ROOT / "WikiContent" / "catalog.tsv"
    if not catalog_path.is_file():
        return fail("missing WikiContent/catalog.tsv")
    catalog_ids = {
        fields[1]
        for line in catalog_path.read_text(encoding="utf-8").splitlines()[1:]
        if len(fields := line.split("\t")) > 1
    }

    for entry_id, route_name in BOSSES.items():
        if entry_id not in catalog_ids:
            return fail("catalog missing -> " + entry_id)

        for language, sections in REQUIRED_SECTIONS.items():
            canonical = ROOT / "WikiContent" / language / "boss" / f"{entry_id}.md"
            generated = ROOT / "wiki-site" / "docs" / ("en" if language == "en" else "") / "bosses" / route_name
            if not canonical.is_file() or not generated.is_file():
                return fail(f"missing {language} canonical/generated pair -> {entry_id}")

            source = canonical.read_text(encoding="utf-8")
            site = generated.read_text(encoding="utf-8")
            if not source.strip():
                return fail(f"guide is empty -> {language}/{entry_id}")
            for section_name, pattern in sections:
                if not pattern.search(source):
                    return fail(f"missing player section {section_name} -> {language}/{entry_id}")
            if normalize_generated_content(source) != normalize_generated_content(site):
                return fail(f"generated site page is not synchronized -> {language}/{entry_id}")

    print("BossWikiGuideContentGuard: PASS - 6 canonical + 6 generated player guides")
    return 0


if __name__ == "__main__":
    sys.exit(main())
