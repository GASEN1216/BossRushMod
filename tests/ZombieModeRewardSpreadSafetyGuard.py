from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
REFORGE = Path("Integration/Reforge/ReforgeSystem.cs")


def fail(message: str) -> int:
    print("ZombieModeRewardSpreadSafetyGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace + 1:index]
    return ""


def require_before(text: str, before: str, after: str, label: str) -> int:
    before_index = text.find(before)
    after_index = text.find(after)
    if before_index < 0 or after_index < 0:
        return fail("missing ordering token for " + label)
    if before_index > after_index:
        return fail("wrong ordering for " + label + ": " + before + " must appear before " + after)
    return 0


def main() -> int:
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""
    reforge = REFORGE.read_text(encoding="utf-8") if REFORGE.exists() else ""
    if not effects:
        return fail("missing ZombieModeRewardEffects.cs")

    required_tokens = [
        "RestoreZombieModeProjectileSpreadStateExcept(holdItem);",
        "ZombieModeProjectileSpreadSnapshot snapshot = CaptureZombieModeProjectileSpreadSnapshot(holdItem);",
        "float currentShotCount = shotCountStat.Value;",
        "float currentShotAngle = shotAngleStat.Value;",
        "Stat damageStat = holdItem.Stats.GetStat(\"Damage\");",
        "int shotCount = Mathf.Max(1, Mathf.RoundToInt(currentShotCount));",
        "float shotAngle = Mathf.Max(0f, currentShotAngle);",
        "shotCount = Mathf.Max(shotCount, 3);",
        "shotAngle = Mathf.Max(shotAngle, 8f);",
        "shotCount = Mathf.Max(shotCount, 5);",
        "shotAngle = Mathf.Max(shotAngle, 18f);",
        "float damageSplitMultiplier = CalculateZombieModeProjectileSpreadDamageMultiplier(currentShotCount, shotCount);",
        "TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, \"ShotCount\", shotCount - currentShotCount);",
        "TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, \"ShotAngle\", shotAngle - currentShotAngle);",
        "TryAddZombieModeGunStatRuntimePercentageModifier(snapshot, holdItem, \"Damage\", damageSplitMultiplier - 1f);",
        "private float CalculateZombieModeProjectileSpreadDamageMultiplier(float originalShotCount, int appliedShotCount)",
        "Order 300 is intentional: spread overlays are a late runtime delta",
        "new Modifier(ModifierType.Add, delta, true, 300, snapshot);",
        "new Modifier(ModifierType.PercentageAdd, percent, true, 300, snapshot);",
        "RuntimeStatModifierTracker.RemoveAll(snapshot.ModifierRecords, \"ZombieMode Projectile Spread\");",
        "private void RestoreZombieModeProjectileSpreadStateExcept(Item exceptItem)",
        "RestoreExistingZombieModeProjectileSpreadSnapshot(holdItem);",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("spread state must preserve base stats and restore old held weapons -> " + token)

    rebuild_body = extract_method_body(effects, "private void RebuildZombieModeProjectileSpreadState()")
    if not rebuild_body:
        return fail("missing RebuildZombieModeProjectileSpreadState body")
    result = require_before(
        rebuild_body,
        "RestoreExistingZombieModeProjectileSpreadSnapshot(holdItem);",
        "Stat shotCountStat = holdItem.Stats.GetStat(\"ShotCount\");",
        "same held weapon rebuild must clear old spread modifiers before reading stats")
    if result:
        return result

    capture_body = extract_method_body(effects, "private ZombieModeProjectileSpreadSnapshot CaptureZombieModeProjectileSpreadSnapshot(Item item)")
    if not capture_body:
        return fail("missing CaptureZombieModeProjectileSpreadSnapshot body")
    result = require_before(
        capture_body,
        "RestoreZombieModeProjectileSpreadSnapshot(existing);",
        "return existing;",
        "reused spread snapshot must remove previous modifiers before reapplying")
    if result:
        return result

    banned_tokens = [
        "int shotCount = 1;",
        "float shotAngle = 0f;",
        "TrySetZombieModeGunStatBaseValue",
        "ApplyStatValueChangePublic(stat",
        "ShotCountBaseValue",
        "ShotAngleBaseValue",
    ]
    for token in banned_tokens:
        if token in effects:
            return fail("spread overlay still resets weapon base stats absolutely -> " + token)

    for token in ['"ShotCount"', '"ShotAngle"']:
        if token not in reforge:
            return fail("spread stats must be excluded from persistent reforge stats -> " + token)

    print("ZombieModeRewardSpreadSafetyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
