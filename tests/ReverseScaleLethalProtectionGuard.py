"""Guard: Reverse Scale survives the new Health.Hurt lethal-event order."""

from pathlib import Path
import sys


PATCH = Path("Patches/Combat/BossLethalHealthProtectionPatch.cs")
MANAGER = Path("Integration/ReverseScale/ReverseScaleAbilityManager.cs")
CONFIG = Path("Integration/ReverseScale/ReverseScaleConfig.cs")


def fail(message: str) -> int:
    print("ReverseScaleLethalProtectionGuard: FAIL - " + message)
    return 1


def require(text: str, token: str, message: str) -> int:
    if token not in text:
        return fail(message + " -> missing token: " + token)
    return 0


def main() -> int:
    patch_text = PATCH.read_text(encoding="utf-8", errors="ignore")
    manager_text = MANAGER.read_text(encoding="utf-8", errors="ignore")
    config_text = CONFIG.read_text(encoding="utf-8", errors="ignore")

    for token, message in [
        ("private static bool Prefix(Health __instance, ref bool __result)", "Health.Hurt prefix must be able to skip damage during post-trigger invincibility"),
        ("ReverseScaleAbilityManager.IsPostTriggerInvincible(__instance)", "Health.Hurt prefix must honor Reverse Scale invincibility"),
        ("if (TryClampReverseScale(__instance, ref value))", "CurrentHealth setter patch must clamp Reverse Scale lethal damage"),
        ("ReverseScaleAbilityManager.TryPrepareLethalProtectionDuringHurt(health)", "Reverse Scale clamp must prepare the OnHurt trigger path"),
    ]:
        result = require(patch_text, token, message)
        if result:
            return result

    reverse_pos = patch_text.find("if (TryClampReverseScale(__instance, ref value))")
    dragon_king_pos = patch_text.find("if (TryClampDragonKing(__instance, ref value))")
    if reverse_pos < 0 or dragon_king_pos < 0 or reverse_pos > dragon_king_pos:
        return fail("Reverse Scale lethal clamp should run before boss-specific clamps")

    for token, message in [
        ("internal static bool TryPrepareLethalProtectionDuringHurt(Health health)", "manager must expose lethal-protection preparation for the patch"),
        ("EnsureInstance();", "lethal protection must recover if the manager missed normal equipment registration"),
        ("manager.PrepareLethalProtection(main, foundSlot, foundItem);", "lethal protection must register the OnHurt handler before Health.Hurt invokes it"),
        ("private static bool TryFindEquippedReverseScale", "equipped Reverse Scale detection should be shared"),
        ("BeginPostTriggerInvincibility(health, config.ReviveInvincibilityDuration);", "effect trigger must start the post-trigger invincibility window"),
        ("internal static bool IsPostTriggerInvincible(Health health)", "manager must expose the post-trigger invincibility check"),
        ("Time.time > postTriggerInvincibleUntil", "invincibility window must expire by time"),
    ]:
        result = require(manager_text, token, message)
        if result:
            return result

    if "public float ReviveInvincibilityDuration => 0.5f;" not in config_text:
        return fail("Reverse Scale revive invincibility duration must remain 0.5 seconds")

    print("ReverseScaleLethalProtectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
