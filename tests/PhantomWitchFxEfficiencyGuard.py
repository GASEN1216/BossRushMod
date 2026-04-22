"""
Guard: Phantom Witch 常驻 FX 需要遵守降级策略并避免长局无意义积累。

要求：
- AmbientPresence 的 ground mist 不能在 RefreshLayerState 中无条件常驻全速
- AmbientPresence 必须清理已销毁的 transientEffects 引用
- CurseSweatVfx 必须清理已销毁的 transientRuneObjects 引用
"""

from pathlib import Path
import re
import sys


AMBIENT = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")
CURSE = Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    ambient_text = AMBIENT.read_text(encoding="utf-8")
    curse_text = CURSE.read_text(encoding="utf-8")

    if "ResolveGroundMistEmissionRate" not in ambient_text:
        return fail("PhantomWitchFxEfficiencyGuard: missing ResolveGroundMistEmissionRate helper")

    if re.search(r"SetParticleEmission\s*\(\s*groundMistParticles\s*,\s*DefaultGroundMistEmissionRate\s*\)", ambient_text):
        return fail("PhantomWitchFxEfficiencyGuard: ground mist is still hardcoded to always-on full rate")

    if not re.search(r"SetParticleEmission\s*\(\s*groundMistParticles\s*,\s*ResolveGroundMistEmissionRate\s*\(\s*\)\s*\)", ambient_text):
        return fail("PhantomWitchFxEfficiencyGuard: ground mist is not routed through ResolveGroundMistEmissionRate")

    if "PruneDestroyedTransientEffects" not in ambient_text:
        return fail("PhantomWitchFxEfficiencyGuard: missing ambient transient prune helper")

    if not re.search(r"PruneDestroyedTransientEffects\s*\(\s*\)\s*;\s*[\s\S]*transientEffects\.Add\s*\(\s*root\s*\)", ambient_text):
        return fail("PhantomWitchFxEfficiencyGuard: ambient transient effects are added without pruning dead entries first")

    if "PruneDestroyedTransientRunes" not in curse_text:
        return fail("PhantomWitchFxEfficiencyGuard: missing curse transient rune prune helper")

    if not re.search(r"PruneDestroyedTransientRunes\s*\(\s*\)\s*;\s*[\s\S]*transientRuneObjects\.Add\s*\(\s*rune\s*\)", curse_text):
        return fail("PhantomWitchFxEfficiencyGuard: curse transient runes are added without pruning dead entries first")

    print("PhantomWitchFxEfficiencyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
