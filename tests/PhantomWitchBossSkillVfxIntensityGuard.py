"""
Guard the Phantom Witch boss skill VFX intensity upgrade.

This source-level guard ensures the boss now borrows the scythe's
high-readability visual language:
- scythe swing overlay on boss slash/sweep skills
- boss realm visual aligned with the weapon right-click realm
- warning / charge / summon effects include brighter requiem accents
"""

from pathlib import Path
import re
import sys


ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
REDESIGN = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def require(text: str, pattern: str, description: str) -> str | None:
    if re.search(pattern, text, re.MULTILINE | re.DOTALL) is None:
        return description
    return None


def main() -> int:
    asset_text = ASSET.read_text(encoding="utf-8")
    redesign_text = REDESIGN.read_text(encoding="utf-8")

    boss_realm_block = extract_block(asset_text, "public static GameObject CreateBossCurseRealmVisual")
    sweep_block = extract_block(redesign_text, "internal static GameObject CreateScytheSweepEffect")
    heavy_block = extract_block(redesign_text, "internal static GameObject CreateHeavySlashEffect")
    curse_aura_block = extract_block(redesign_text, "internal static GameObject CreateCurseAuraEffect")
    warning_block = extract_block(redesign_text, "internal static GameObject CreateCurseRealmWarningCircle")
    summon_block = extract_block(redesign_text, "internal static GameObject CreateSummonCircleEffect")
    minion_block = extract_block(redesign_text, "internal static GameObject CreateMinionSpawnEffect")

    missing = [
        require(
            boss_realm_block,
            r"PhantomWitchCurseRealmVisual\.Create\s*\(",
            "boss curse realm still does not reuse the stronger scythe realm visual",
        ),
        require(
            sweep_block,
            r"(PhantomWitchScytheSwingFx\.PlayAt|PlayBossScytheSwingOverlay)\s*\(",
            "boss sweep still misses scythe left-click swing overlay",
        ),
        require(
            heavy_block,
            r"(PhantomWitchScytheSwingFx\.PlayAt|PlayBossScytheSwingOverlay)\s*\(",
            "boss heavy slash still misses scythe left-click swing overlay",
        ),
        require(
            curse_aura_block,
            r"(CreateRequiemLine|AttachTransientRequiemLine)\s*\(",
            "curse aura still misses requiem line accent",
        ),
        require(
            curse_aura_block,
            r"(CreateSilverCrossFlash|AttachTransientSilverCrossFlash)\s*\(",
            "curse aura still misses silver cross flash",
        ),
        require(
            warning_block,
            r"CreateSoulMistEmitter\s*\(",
            "curse realm warning still misses stronger mist telegraph",
        ),
        require(
            warning_block,
            r"CreateStardustEmitter\s*\(",
            "curse realm warning still misses brighter particle telegraph",
        ),
        require(
            summon_block,
            r"(CreateRequiemLine|AttachTransientRequiemLine)\s*\(",
            "summon circle still misses requiem line accent",
        ),
        require(
            summon_block,
            r"(CreateSilverCrossFlash|AttachTransientSilverCrossFlash)\s*\(",
            "summon circle still misses silver cross flash",
        ),
        require(
            minion_block,
            r"(CreateSilverCrossFlash|AttachTransientSilverCrossFlash)\s*\(",
            "minion spawn still misses strong flash accent",
        ),
    ]

    missing = [item for item in missing if item is not None]
    if missing:
        return fail("PhantomWitchBossSkillVfxIntensityGuard: " + " | ".join(missing))

    print("PhantomWitchBossSkillVfxIntensityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
