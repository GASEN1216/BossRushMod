"""
Guard for concrete review findings that the structural redesign guards do not catch.

This focuses on:
- no accidental release dev-mode enable
- no leaked per-use materials in redesign paths
- curse halo child cleanup
- lingering tail roots not destroyed too early
"""

from pathlib import Path
import re
import sys


MOD = Path("ModBehaviour.cs")
REDESIGN = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
REDESIGN_PARTS = [
    REDESIGN,
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_EmittersAndTextures.cs"),
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_RuntimeComponents.cs"),
]
CURSE = Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs")


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


def read_redesign() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in REDESIGN_PARTS)


def main() -> int:
    mod_text = MOD.read_text(encoding="utf-8")
    redesign_text = read_redesign()
    curse_text = CURSE.read_text(encoding="utf-8")

    teleport_block = extract_block(redesign_text, "internal static GameObject CreateTeleportEffect")
    hit_block = extract_block(redesign_text, "internal static GameObject CreateDamageHitEffect")
    altar_block = extract_block(redesign_text, "private static GameObject CreateAltarProjection")
    realm_spawner_block = extract_block(redesign_text, "internal sealed class PhantomWitchRealmRuneFlashSpawner")
    curse_rune_block = extract_block(curse_text, "private void SpawnRuneFlash")
    curse_destroy_block = extract_block(curse_text, "private void OnDestroy")

    missing = []

    if re.search(r"private const bool HardcodedDevModeEnabled = false;", mod_text) is None:
        missing.append("HardcodedDevModeEnabled must be false")

    if "new Material(GetQuadMaterial())" in altar_block:
        missing.append("altar projection still allocates per-use material")

    if re.search(r"line\.sharedMaterial\s*=\s*new Material", realm_spawner_block):
        missing.append("realm rune flash still allocates per-line material")

    if re.search(r"line\.sharedMaterial\s*=\s*new Material", curse_rune_block):
        missing.append("curse mini rune still allocates per-line material")

    if re.search(r"Destroy\s*\(\s*halo(Renderer|Transform)?\.gameObject", curse_destroy_block) is None and "Destroy(haloObject" not in curse_destroy_block:
        missing.append("curse halo child is not explicitly destroyed")

    if "Object.Destroy(root, PhantomWitchConfig.TeleportFxDuration)" in teleport_block:
        missing.append("teleport root lifetime still truncates 1.5s residue tail")

    if "Object.Destroy(root, 0.33f)" in hit_block:
        missing.append("hit root lifetime still truncates 0.8s residue tail")

    if missing:
        return fail("PhantomWitchVfxReviewGuard: " + " | ".join(missing))

    print("PhantomWitchVfxReviewGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
