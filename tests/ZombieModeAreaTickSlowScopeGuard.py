"""Guard: ZombieMode area tick slow must stay scoped to the visible area."""

from pathlib import Path
import sys
from cs_source_util import clean_source


BOSS = Path("ZombieMode/ZombieModeBossController.cs")
BOSS_MODULE = Path("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
RUNTIME = Path("ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeAreaTickSlowScopeGuard: FAIL - " + message)
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


def extract_class_body(text: str, class_name: str, next_class_name: str) -> str | None:
    start_token = "public sealed class " + class_name
    end_token = "\n    public sealed class " + next_class_name
    start = text.find(start_token)
    if start < 0:
        return None

    end = text.find(end_token, start + len(start_token))
    if end < 0:
        return None

    return text[start:end]


def main() -> int:
    boss = BOSS.read_text(encoding="utf-8-sig")
    boss_module = BOSS_MODULE.read_text(encoding="utf-8-sig")
    runtime = clean_source(RUNTIME.read_text(encoding="utf-8-sig"))

    area_runtime = extract_class_body(boss, "ZombieModeAreaTickRuntime", "ZombieModeBossShieldRuntime")
    if area_runtime is None:
        return fail("missing ZombieModeAreaTickRuntime class")

    for token in [
        "float startupDelay = 0f",
        "bool followSource = false",
        "private bool followSourcePosition;",
        "nextTickTime = Time.unscaledTime + Mathf.Max(Mathf.Max(0f, startupDelay), tickInterval);",
        "if (tickDamage > 0f)",
        "inst.DealZombieModeRuntimeAreaDamageToPlayer(RuntimeRunId, source, transform.position, radius, tickDamage);",
        "inst.TryApplyZombieModePlayerSlowInPersistentArea(RuntimeRunId, transform.position, radius, slowPercent, tickInterval * 2f, this);",
    ]:
        if token not in area_runtime:
            return fail("area tick runtime missing scoped slow/startup token -> " + token)

    if "inst.TryApplyZombieModePlayerSlow(RuntimeRunId, slowPercent" in area_runtime:
        return fail("area tick runtime reverted to global player slow")

    for token in ["isActiveAndEnabled && armed && slowPercent > 0f ? RuntimeRemainingSeconds : 0f",
                  "source.transform.position + Vector3.up * 0.04f : transform.position",
                  "runtimePauseStartTime >= 0f ? runtimePauseStartTime : Time.unscaledTime"]:
        if token not in boss:
            return fail("persistent slow source must expose armed/living/current center and pause-safe lifetime -> " + token)

    bridge = clean_source(Path("ZombieMode/ZombieModeCombatHostBridge.cs").read_text(encoding="utf-8-sig"))
    forward = extract_method_body(bridge, "internal void TryApplyZombieModePlayerSlowInPersistentArea(")
    if forward is None or "module.TryApplyZombieModePlayerSlowInArea(runId, origin, radius, percent, duration, area);" not in forward:
        return fail("persistent slow must forward the real source component through its internal bridge")

    corruption_zone = extract_method_body(boss_module, "private void SpawnZombieModeCorruptionZone(")
    if corruption_zone is None:
        return fail("missing SpawnZombieModeCorruptionZone body")
    for token in [
        "ZombieModeTuning.CorruptorZoneStartupSeconds + ZombieModeTuning.CorruptorZoneDurationSeconds",
        "ZombieModeTuning.CorruptorZoneSlowPercent,",
        "ZombieModeTuning.CorruptorZoneStartupSeconds",
    ]:
        if token not in corruption_zone:
            return fail("corruption zone no longer passes startup/slow tuning -> " + token)

    slow_area = extract_method_body(runtime, "internal void TryApplyZombieModePlayerSlowInArea(")
    if slow_area is None:
        return fail("missing TryApplyZombieModePlayerSlowInArea helper")
    for token in [
        "foreach (CharacterMainControl player in EnumerateZombieModeZoneTargets(runId))",
        "Vector3 delta = player.transform.position - origin;",
        "if (delta.sqrMagnitude > radius * radius) continue;",
        "TryApplyZombieModePlayerSlow(runId, percent, duration);",
        "SendCoopZombieZoneSlow(runId, player, origin, radius, percent, duration, area);",
    ]:
        if token not in slow_area:
            return fail("area slow helper missing range check token -> " + token)

    zones = clean_source(Path("ZombieMode/ZombieModeCoopZoneEffects.cs").read_text(encoding="utf-8"))
    receipt = extract_method_body(zones, "private bool ApplyCoopZombieZoneSlow(")
    for token in ["effect.Run != runState.RunId", "effect.Wave < runState.CurrentWave",
                  "effect.Generation != BossRushCoop.SceneGeneration", "effect.SceneToken != BossRushCoop.SceneToken",
                  "!IsCoopZombieLocalAssetCurrent()", "player.Health.IsDead",
                  "(player.transform.position - effect.Origin).sqrMagnitude > effect.Radius * effect.Radius",
                  "TryApplyZombieModePlayerSlow(effect.Run, effect.Percent, duration);",
                  "Time.unscaledTime - pending.RequestedAt", "if (duration <= 0f) return true;"]:
        if receipt is None or token not in receipt:
            return fail("slow receipt must recheck battle, owner, life and area scope -> " + token)

    for signature, tokens in [
        ("private void TickCoopZombieZoneEffects(", ["Time.unscaledTime - fact.LastOfferedAt >= 0.25f"]),
        ("private float GetCoopZombieZoneFactRemaining(", ["if (!fact.Persistent)", "fact.Area != null ? fact.Area.CoopSlowRemainingSeconds : 0f"]),
        ("private void ConfirmCoopZombieZoneProbe(", ["GetCoopZombieZoneFactRemaining(fact)", "fact.Area.CoopSlowOrigin", "Duration = fact.Persistent ? effect.Duration : remaining, Validity = remaining"]),
        ("private bool ApplyCoopZombieZoneSlow(", ["Mathf.Min(effect.Duration,", "GetCoopZombieZoneValidity(effect) - Mathf.Max(0f, Time.unscaledTime - pending.RequestedAt)"]),
        ("private void TryRequestCoopZombieZoneConfirmation(", ["now - pending.LastRequestedAt < 0.25f", "if (!pending.Requested) pending.RequestedAt = now;"]),
    ]:
        body = extract_method_body(zones, signature)
        for token in tokens:
            if body is None or token not in body:
                return fail("source lifetime, unchanged tick strength and original nonce RTT must stay separate -> " + token)

    print("ZombieModeAreaTickSlowScopeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
