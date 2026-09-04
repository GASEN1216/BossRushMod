"""Guard the canonical ZombieMode mutant encyclopedia and its generated copies.

This is a documentation/development-export check only.  It deliberately reads
the enum and tuning source, but it never loads Unity or scans combat entities.
"""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
MODELS = ROOT / "ZombieMode" / "ZombieModeModels.cs"
TUNING = ROOT / "ZombieMode" / "ZombieModeTuning.cs"
POLLUTION = ROOT / "ZombieMode" / "ZombieModePollution.cs"
SKILLS = ROOT / "ZombieMode" / "ZombieModePollution_RuntimeSkills.cs"
COMPONENTS = ROOT / "ZombieMode" / "ZombieModePollution_RuntimeComponents.cs"
LOCALIZATION = ROOT / "Localization" / "LocalizationInjector.cs"

CANONICAL = {
    "zh": ROOT / "WikiContent" / "zh" / "mode" / "mode__zombie_mode.md",
    "en": ROOT / "WikiContent" / "en" / "mode" / "mode__zombie_mode.md",
}
GENERATED = {
    "zh": ROOT / "wiki-site" / "docs" / "game-modes" / "zombie-mode.md",
    "en": ROOT / "wiki-site" / "docs" / "en" / "game-modes" / "zombie-mode.md",
}


def fail(message: str) -> int:
    print("ZombieModeMutantWikiGuard: FAIL - " + message)
    return 1


def read(path: Path) -> str:
    if not path.is_file():
        raise FileNotFoundError(path)
    return path.read_text(encoding="utf-8")


def extract_enum_members(source: str, enum_name: str):
    match = re.search(
        rf"\bpublic\s+enum\s+{re.escape(enum_name)}\s*\{{(?P<body>.*?)\}}",
        source,
        re.DOTALL,
    )
    if not match:
        return []

    members = []
    for line in match.group("body").splitlines():
        line = line.split("//", 1)[0].strip()
        if not line:
            continue
        member = re.match(r"([A-Za-z_]\w*)\s*(?:=\s*[^,]+)?\s*,?\s*$", line)
        if member:
            members.append(member.group(1))
    return members


def section_between(text: str, start_heading: str, end_heading: str) -> str:
    start = text.find(start_heading)
    if start < 0:
        return ""
    end = text.find(end_heading, start + len(start_heading))
    return text[start:] if end < 0 else text[start:end]


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start : index + 1]
    return ""


def transform_content(raw: str) -> str:
    """Mirror wiki-site/scripts/sync-content.mjs's deterministic transform."""
    # 单次回调、层级无关：三次独立 re.sub 会级联（#### → ### → ## → #），
    # 把所有层级压成 #。必须与 wiki-site/scripts/sync-content.mjs 的
    # transformContent() 逐字对应——下面的比对是逐字节的。
    content = re.sub(
        r"^(#{2,6})([ \t])",
        lambda m: "#" * (len(m.group(1)) - 1) + m.group(2),
        raw,
        flags=re.MULTILINE,
    )
    content = re.sub(
        r"^\[tip\][ \t]*(.+)$",
        r"::: tip\n\1\n:::",
        content,
        flags=re.MULTILINE,
    )
    content = re.sub(
        r"^\[warn\][ \t]*(.+)$",
        r"::: warning\n\1\n:::",
        content,
        flags=re.MULTILINE,
    )
    content = re.sub(r"\[([^\]]*)\]\(/[A-Za-z]:[^)]*\)", r"\1", content)
    return content


def extract_injected_names(source: str, key_prefix: str):
    """Map `<Kind>` -> (zh display name, en display name) from LocalizationInjector.

    WikiContent is player-facing, so the mutant tables must be keyed by the name
    the player actually sees popped over the zombie's head — never by the C#
    enum member. Reading the injection table keeps the coverage invariant while
    additionally pinning the Wiki wording to the shipped localization.
    """
    names = {}
    pattern = re.compile(
        r'InjectZombieModeString\(\s*"'
        + re.escape(key_prefix)
        + r'(\w+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*\)'
    )
    for member, zh_name, en_name in pattern.findall(source):
        names[member] = (zh_name, en_name)
    return names


def require_pair(source: str, zh: str, en: str, source_fragment: str,
                 zh_fragment: str, en_fragment: str) -> str:
    if source_fragment not in source:
        return "missing source token -> " + source_fragment
    if zh_fragment not in zh:
        return "missing zh data token -> " + zh_fragment
    if en_fragment not in en:
        return "missing en data token -> " + en_fragment
    return ""


def main() -> int:
    try:
        models = read(MODELS)
        tuning = read(TUNING)
        pollution = read(POLLUTION)
        skills = read(SKILLS)
        components = read(COMPONENTS)
        localization = read(LOCALIZATION)
        zh = read(CANONICAL["zh"])
        en = read(CANONICAL["en"])
    except FileNotFoundError as error:
        return fail("missing required source/page -> " + str(error))

    special_members = [
        member
        for member in extract_enum_members(models, "ZombieModeSpecialKind")
        if member != "None"
    ]
    affix_members = extract_enum_members(models, "ZombieModeEliteAffix")
    if not special_members or not affix_members:
        return fail("could not extract ZombieMode enum members")

    zh_special = section_between(zh, "#### 特殊丧尸", "#### 精英丧尸")
    en_special = section_between(en, "#### Special Zombies", "#### Elite Zombies")
    zh_elite = section_between(zh, "#### 精英丧尸", "#### 出现概率")
    en_elite = section_between(en, "#### Elite Zombies", "#### Spawn Probability")
    special_names = extract_injected_names(
        localization, "BossRush_ZombieMode_Special_"
    )
    affix_names = extract_injected_names(localization, "BossRush_ZombieMode_Affix_")
    for member in special_members:
        if member not in special_names:
            return fail("special kind has no injected display name -> " + member)
        zh_name, en_name = special_names[member]
        if zh_name not in zh_special or en_name not in en_special:
            return fail("special kind missing from both tables -> " + member)
    for member in affix_members:
        if member not in affix_names:
            return fail("elite affix has no injected display name -> " + member)
        zh_name, en_name = affix_names[member]
        if zh_name not in zh_elite or en_name not in en_elite:
            return fail("elite affix missing from both tables -> " + member)

    source = "\n".join((tuning, pollution, skills, components))
    pairs = [
        # Shared combat and pollution framing.
        ("SpecialHealthMultiplier = 1.4f", "生命 ×1.40", "HP ×1.40"),
        ("SpecialDamageMultiplier = 1.2f", "伤害 ×1.20", "damage ×1.20"),
        ("SpecialMoveSpeedMultiplier = 1.1f", "移速 ×1.10", "speed ×1.10"),
        ("EliteHealthMultiplier = 2.5f", "生命 ×2.50", "HP ×2.50"),
        ("EliteDamageMultiplier = 1.5f", "伤害 ×1.50", "damage ×1.50"),
        ("EliteMoveSpeedMultiplier = 1.1f", "移速 ×1.10", "speed ×1.10"),
        ("EnhancedEliteHealthMultiplier = 3.2f", "生命 ×3.20", "HP ×3.20"),
        ("EnhancedEliteDamageMultiplier = 1.7f", "伤害 ×1.70", "damage ×1.70"),
        ("EnhancedEliteMoveSpeedMultiplier = 1.3f", "移速 ×1.30", "speed ×1.30"),
        ("PollutionHealthScalePerPoint = 0.05f", "污染 × 0.05", "pollution × 0.05"),
        ("PollutionDamageScalePerPoint = 0.04f", "污染 × 0.04", "pollution × 0.04"),
        # Visual identity constants.
        ("SprinterScale = 1.35f", "×1.35", "×1.35"),
        ("ExploderScale = 1.60f", "×1.60", "×1.60"),
        ("PlagueScale = 1.80f", "×1.80", "×1.80"),
        ("SummonerScale = 2.00f", "×2.00", "×2.00"),
        ("HarasserScale = 1.45f", "×1.45", "×1.45"),
        ("EliteOneAffixScale = 1.65f", "×1.65", "×1.65"),
        ("EliteTwoAffixScale = 1.95f", "×1.95", "×1.95"),
        ("EliteThreeAffixScale = 2.25f", "×2.25", "×2.25"),
        ("HighThreatAffixBonus = 0.20f", "+0.20", "+0.20"),
        ("MaxVisualScale = 3.00f", "×3.00", "×3.00"),
        # Special skill constants.
        ("SprinterDashDistance = 12f", "12 米", "12m"),
        ("SprinterDashStartupSeconds = 0.5f", "0.5 秒", "0.5s"),
        ("SprinterCooldownSeconds = 8f", "8 秒", "8s"),
        ("ExploderTriggerDistance = 2.5f", "2.5 米", "2.5m"),
        ("ExploderDetonationDelaySeconds = 1.0f", "1 秒", "1s"),
        ("ExploderDeathRadius = 4f", "4 米", "4m"),
        ("ExploderDeathDamage = 80f", "80 伤害", "80 damage"),
        ("ExploderCooldownSeconds = 9f", "技能周期 9 秒", "9s skill cycle"),
        ("PoisonCooldownSeconds = 12f", "12 秒", "12s"),
        ("ThreatTelegraphDelaySeconds = 0.9f", "0.9 秒", "0.9s"),
        ("PlagueCloudRadius = 4f", "4 米", "4m"),
        ("PlagueCloudDurationSeconds = 3f", "3 秒", "3s"),
        ("PlagueCloudDamagePerSecond = 8f", "8 伤害", "8 DPS"),
        ("SummonerSpawnCount = 2", "召唤 2 只", "summons 2"),
        ("SummonerCooldownSeconds = 15f", "15 秒", "15s"),
        ("zombie.transform.localScale = zombie.transform.localScale * 0.6f", "×0.60", "×0.60"),
        ("HarasserProjectileSpeed = 10f", "速度 10", "speed 10"),
        ("HarasserProjectileDamage = 25f", "伤害 25", "damage 25"),
        ("HarasserProjectileLifetimeSeconds = 3.5f", "寿命 3.5 秒", "lifetime 3.5s"),
        ("HarasserCooldownSeconds = 4f", "4 秒", "4s"),
        ("HarasserSlowRadius = 3.5f", "3.5 米", "3.5m"),
        ("HarasserSlowPercent = 0.50f", "50%", "50%"),
        ("HarasserSlowDurationSeconds = 2f", "2 秒", "2s"),
        # Elite affix constants and source-only literals.
        ("speedMultiplier *= 1.30f", "移速额外 ×1.30", "Additional speed ×1.30"),
        ("damageMultiplier *= 1.15f", "伤害额外 ×1.15", "Additional damage ×1.15"),
        ("speedMultiplier *= 1.10f", "移速额外 ×1.10", "speed ×1.10"),
        ("healthMultiplier *= 1.40f", "生命额外 ×1.40", "Additional HP ×1.40"),
        ("healthMultiplier *= 1.15f", "生命额外 ×1.15", "Additional HP ×1.15"),
        ("healthMultiplier *= 1.25f", "生命额外 ×1.25", "Additional HP ×1.25"),
        ("StalwartRangedDamageMultiplier = 0.10f", "只结算 10%", "reduced to 10%"),
        ("BurstAffixDeathRadius = 4f", "半径 4 米", "4m radius"),
        ("BurstAffixDeathDamage = 40f", "40 伤害", "40 damage"),
        ("SplittingAffixSpawnCount = 2", "生成 2 只", "spawns 2"),
        ("AdaptiveAffixHitThreshold = 5", "5 次近战", "5 consecutive"),
        ("AdaptiveAffixReductionPercent = 0.60f", "减伤 60%", "reduced by 60%"),
        ("AdaptiveAffixDurationSeconds = 8f", "持续 8 秒", "for 8s"),
        ("ShieldedAffixCooldownSeconds = 12f", "12 秒获得", "every 12s"),
        ("ShieldedAffixShieldPercent = 0.25f", "25%", "25%"),
        ("ShieldedAffixDurationSeconds = 5f", "持续 5 秒", "for 5s"),
        ("CommanderAffixAuraRadius = 8f", "8 米光环", "8m aura"),
        ("CommanderAffixMoveSpeedBonus = 0.20f", "移速 +20%", "+20%"),
        ("CommanderAffixDamageBonus = 0.15f", "伤害 +15%", "+15%"),
        ("CommanderAuraTickIntervalSeconds = 0.5f", "0.5 秒刷新", "refreshes every 0.5s"),
        ("MaxHealth * 0.025f", "2.5%", "2.5%"),
        ("float eliteCloudDps = 26f", "总伤害 26", "26 total damage"),
        ("5.5f,", "半径 5.5 米", "5.5m radius"),
    ]
    for source_fragment, zh_fragment, en_fragment in pairs:
        error = require_pair(source, zh, en, source_fragment, zh_fragment, en_fragment)
        if error:
            return fail(error)

    # Color values and priority are sourced from the runtime visual method.
    elite_color = extract_method(
        pollution, "private static Color GetZombieModeEliteVisualColor("
    )
    color_pairs = [
        ("new Color(0.75f, 0.30f, 1f", "#BF4DFF"),
        ("new Color(0.18f, 1f, 0.35f", "#2EFF59"),
        ("new Color(0.15f, 0.95f, 1f", "#26F2FF"),
        ("new Color(1f, 0.30f, 0.08f", "#FF4D14"),
        ("new Color(1f, 0.85f, 0.12f", "#FFD91F"),
        ("new Color(1f, 0.65f, 0.12f", "#FFA61F"),
    ]
    previous = -1
    for source_fragment, hex_value in color_pairs:
        if source_fragment not in elite_color and source_fragment not in pollution:
            return fail("missing runtime color source -> " + source_fragment)
        if hex_value not in zh or hex_value not in en:
            return fail("missing Wiki color mapping -> " + hex_value)
        position = elite_color.rfind(source_fragment)
        if position >= 0 and position < previous:
            return fail("elite color priority order changed -> " + hex_value)
        if position >= 0:
            previous = position

    for source_fragment in (
        "Color.Lerp(current, targetColor, 0.65f)",
        "marker.IsBoss",
        "HashSet<Transform> safeVisualRoots",
        "HasZombieModeUnsafeVisualAncestor",
        "ZombieModeFootMarkerPool.Acquire",
        "marker.VisualFootMarkerFallbackApplied",
    ):
        if source_fragment not in pollution:
            return fail("missing visual identity source invariant -> " + source_fragment)
    # The three visual-identity facts must stay documented, but WikiContent is a
    # player-facing page: assert the player-facing wording, never the engine symbols
    # (CharacterModel / pooled marker) that used to leak into it.
    for page, language, blend_token, safe_token, fallback_token in (
        (zh, "zh", "原本的配色",
         "攻击范围、碰撞和走位都不受影响",
         "脚下会有一个常驻标记"),
        (en, "en", "blended toward",
         "Attack range, collision and pathing are unaffected",
         "persistent marker appears at its feet"),
    ):
        if blend_token not in page or safe_token not in page or fallback_token not in page:
            return fail("visual blending/safe subtree/fallback note missing -> " + language)
        if "Boss" not in page or "Titan" not in page:
            return fail("Boss exclusion note missing -> " + language)

    # This feature is a canonical Markdown/development export. Runtime code must
    # not grow a WikiContent/site scanner or a combat-time mutant Wiki exporter.
    for path in (ROOT / "ZombieMode").glob("*.cs"):
        runtime_text = path.read_text(encoding="utf-8")
        if re.search(r"WikiContent|wiki-site|MutantWiki|变异僵尸图鉴", runtime_text):
            return fail("runtime Wiki scanner/export reference found -> " + str(path))

    for language in ("zh", "en"):
        try:
            generated = read(GENERATED[language])
        except FileNotFoundError as error:
            return fail("missing generated page -> " + str(error))
        expected = transform_content(read(CANONICAL[language]))
        if generated != expected:
            return fail("generated page is out of sync -> " + language)

    print(
        "ZombieModeMutantWikiGuard: PASS - "
        + str(len(special_members))
        + " specials, "
        + str(len(affix_members))
        + " affixes, both generated pages synchronized"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
