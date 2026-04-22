"""
Guard: PhantomWitchCurseSweatVfx 的兜底补 Buff 只能处理玩家普攻命中。

要求：
- OnGlobalHurt 必须通过专门 helper 过滤后，才允许进入 TryApplyBuff / EnqueueRetry 分支
- helper 必须同时检查：
  - 武器 ID 为噬魂挽歌
  - 不是 Buff/Effect 伤害
  - 攻击者是主玩家
  - DamageInfo 自带诅咒 Buff 元数据
  - buffChance 为正值

这样可避免 Boss 技能、诅咒领域和其它同 weapon id 的自定义伤害误触发补 Buff。
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    helper_name = "ShouldApplyFallbackCurseFromNormalAttack"
    if helper_name not in text:
        return fail("PhantomWitchCurseSweatVfx guard: missing helper " + helper_name)

    required_patterns = [
        (
            "weapon id gate",
            re.compile(r"damageInfo\.fromWeaponItemID\s*!=\s*PhantomWitchScytheIds\.WeaponTypeId"),
        ),
        (
            "effect damage gate",
            re.compile(r"damageInfo\.isFromBuffOrEffect"),
        ),
        (
            "main player attacker gate",
            re.compile(r"IsMainPlayerAttacker\s*\(\s*damageInfo\.fromCharacter\s*\)"),
        ),
        (
            "curse payload gate",
            re.compile(r"MatchesCurseBuffPayload\s*\(\s*damageInfo\s*,\s*curseBuff\s*\)"),
        ),
        (
            "positive buff chance gate",
            re.compile(r"ResolveNormalAttackCurseChance\s*\(\s*damageInfo\s*\)\s*<=\s*0f"),
        ),
    ]

    missing = []
    for description, pattern in required_patterns:
        if pattern.search(text) is None:
            missing.append(description)

    if missing:
        return fail(
            "PhantomWitchCurseSweatVfx guard: helper missing required gates | "
            + " | ".join(missing)
        )

    guarded_call = re.search(
        r"if\s*\(\s*!\s*ShouldApplyFallbackCurseFromNormalAttack\s*\(\s*damageInfo\s*,\s*curseBuff\s*\)\s*\)\s*\{\s*return\s*;\s*\}",
        text,
        re.DOTALL,
    )
    if guarded_call is None:
        return fail("PhantomWitchCurseSweatVfx guard: OnGlobalHurt is not guarded by helper")

    print("PhantomWitchCurseSweatVfxFallbackGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
