"""
Guard: 噬魂挽歌 EnsureMeleeAttackFx 必须为 slashFx 与 hitFx 同时补 fallback。

背景修订：
- 早期方案曾考虑只让 `PhantomWitchScytheSwingFx` 单独作为挥砍 overlay、不再绑定
  任何近战 fallback slashFx；但实测发现：在 slashFx == null 时原版
  ItemAgent_MeleeWeapon 的挥砍流程会提前 return，导致专用 overlay 触发时机错位、
  首挥朝向解算异常。
- 因此当前确定的契约为：
    * slashFx 为 null 时必须回退到 GetFallbackSlashFx()，保住原版动画事件链路；
    * hitFx 为 null 时必须回退到 GetFallbackHitFx()，命中反馈不能丢；
    * 专用挥砍特效 (`PhantomWitchScytheSwingFx`) 在 fallback 之上叠加，互不干扰。
- 本 guard 锁定该契约，防止后续任何"瘦身"重构悄悄把 fallback 抽掉而引发回归。
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

    if re.search(r"meleeAgent\.slashFx\s*=\s*GetFallbackSlashFx\s*\(", block) is None:
        return fail(
            "PhantomWitchScytheSlashFxGuard: slashFx fallback unexpectedly missing "
            "(原版动画事件链路依赖 slashFx 非空)"
        )

    if re.search(r"meleeAgent\.hitFx\s*=\s*GetFallbackHitFx\s*\(", block) is None:
        return fail(
            "PhantomWitchScytheSlashFxGuard: hitFx fallback unexpectedly missing"
        )

    print("PhantomWitchScytheSlashFxGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
