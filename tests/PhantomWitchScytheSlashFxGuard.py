"""
Guard: 噬魂挽歌左键挥砍不应再绑定任意近战 fallback slashFx。

原因：
- 当前左键已经有 `PhantomWitchScytheSwingFx` 作为专用挥击 overlay
- 若再自动绑定一个来源未知的 `slashFx`，就会把别的近战黑烟/黑斩击一起带进来
- `hitFx` 仍可保留用于命中反馈；本 guard 只约束挥砍起手的 `slashFx`
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs")


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


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "internal static void EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)")
    if not block:
        return fail("PhantomWitchScytheSlashFxGuard: missing EnsureMeleeAttackFx block")

    if re.search(r"meleeAgent\.slashFx\s*=\s*GetFallbackSlashFx\s*\(", block):
        return fail(
            "PhantomWitchScytheSlashFxGuard: scythe still assigns fallback slashFx in EnsureMeleeAttackFx"
        )

    if re.search(r"meleeAgent\.hitFx\s*=\s*GetFallbackHitFx\s*\(", block) is None:
        return fail(
            "PhantomWitchScytheSlashFxGuard: hitFx fallback unexpectedly missing"
        )

    print("PhantomWitchScytheSlashFxGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
