"""
Guard: Mode D / E / F 的 Boss 选取链路必须走 BossFilter 过滤接口，
只过滤 Boss，不影响小怪池。
"""

from pathlib import Path
import sys


MODED_WAVES = Path("ModeD/ModeDWaves.cs")
MODEE_BATTLE = Path("ModeE/ModeEBattle.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    mode_d_text = MODED_WAVES.read_text(encoding="utf-8")
    mode_e_text = MODEE_BATTLE.read_text(encoding="utf-8")
    mode_f_text = MODEF_RESPAWN.read_text(encoding="utf-8")

    mode_d_body = extract_method_body(mode_d_text, "private EnemyPresetInfo GetRandomBossPreset()")
    if mode_d_body is None:
        return fail("ModeDEFBossFilterGuard: missing ModeD GetRandomBossPreset body")

    if "GetFilteredEnemyPresets()" not in mode_d_body:
        return fail("ModeDEFBossFilterGuard: ModeD GetRandomBossPreset does not use filtered boss presets")

    mode_e_body = extract_method_body(mode_e_text, "private void BuildModeEFactionPresetCaches()")
    if mode_e_body is None:
        return fail("ModeDEFBossFilterGuard: missing ModeE BuildModeEFactionPresetCaches body")

    if "GetFilteredEnemyPresets()" not in mode_e_body:
        return fail("ModeDEFBossFilterGuard: ModeE faction boss cache does not use filtered boss presets")

    if "modeDMinionPool" not in mode_e_body:
        return fail("ModeDEFBossFilterGuard: ModeE minion pool handling unexpectedly changed")

    mode_f_body = extract_method_body(mode_f_text, "private EnemyPresetInfo GetRandomModeFRespawnBossPreset()")
    if mode_f_body is None:
        return fail("ModeDEFBossFilterGuard: missing ModeF GetRandomModeFRespawnBossPreset body")

    if "GetFilteredEnemyPresets()" not in mode_f_body:
        return fail("ModeDEFBossFilterGuard: ModeF respawn boss picker does not use filtered boss presets")

    print("ModeDEFBossFilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
