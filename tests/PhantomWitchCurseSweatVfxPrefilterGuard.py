"""Guard: PhantomWitch curse sweat hook should skip unrelated hits before resolving curse buff."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs")


def fail(message: str) -> int:
    print("PhantomWitchCurseSweatVfxPrefilterGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    on_hurt = extract_method_body(text, "private static void OnGlobalHurt(")
    could_apply = extract_method_body(text, "private static bool CouldApplyFallbackCurseFromNormalAttack(")
    fallback = extract_method_body(text, "private static bool ShouldApplyFallbackCurseFromNormalAttack(")
    if on_hurt is None:
        return fail("missing OnGlobalHurt body")
    if could_apply is None:
        return fail("missing CouldApplyFallbackCurseFromNormalAttack body")
    if fallback is None:
        return fail("missing ShouldApplyFallbackCurseFromNormalAttack body")

    required_on_hurt = [
        "CharacterMainControl character = TryGetTargetCharacter(hurtHealth);",
        "CharacterBuffManager buffMgr = TryGetBuffManager(character);",
        "bool hasCurse = buffMgr != null && buffMgr.HasBuff(PhantomWitchConfig.CurseBuffID);",
        "if (!hasCurse && !CouldApplyFallbackCurseFromNormalAttack(damageInfo))",
        "Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();",
        "if (hasCurse)",
        "if (!ShouldApplyFallbackCurseFromNormalAttack(damageInfo, curseBuff))",
    ]
    for snippet in required_on_hurt:
        if snippet not in on_hurt:
            return fail("missing OnGlobalHurt prefilter snippet -> " + snippet)

    prefilter_index = on_hurt.find("if (!hasCurse && !CouldApplyFallbackCurseFromNormalAttack(damageInfo))")
    curse_buff_index = on_hurt.find("Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();")
    if prefilter_index < 0 or curse_buff_index < 0 or prefilter_index > curse_buff_index:
        return fail("unrelated-hit prefilter should run before resolving curse buff")

    required_could_apply = [
        "damageInfo.fromWeaponItemID != PhantomWitchScytheIds.WeaponTypeId",
        "damageInfo.isFromBuffOrEffect",
        "!IsMainPlayerAttacker(damageInfo.fromCharacter)",
        "damageInfo.buff == null",
        "ResolveNormalAttackCurseChance(damageInfo) <= 0f",
    ]
    for snippet in required_could_apply:
        if snippet not in could_apply:
            return fail("missing cheap fallback prefilter gate -> " + snippet)

    if "MatchesCurseBuffPayload" in could_apply:
        return fail("cheap prefilter should not require resolved curse buff payload matching")

    if "MatchesCurseBuffPayload(damageInfo, curseBuff)" not in fallback:
        return fail("exact fallback helper must still verify curse buff payload")

    print("PhantomWitchCurseSweatVfxPrefilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
