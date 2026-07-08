"""Guard: PWS energy rounds keep short lifetime, orbit warmup, and trace fallback."""

from pathlib import Path
import sys


PROFILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
AGENT_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
RUNTIME_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
RUNTIME_CORE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")
ASSET_SOURCE = Path("Integration/DragonKing/DragonKingAssetManager.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunEnergyPwsGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[brace_start:idx + 1]

    return None


def extract_profile_block(text: str, profile_id: str) -> str | None:
    id_pos = text.find("Id = DragonKingBossGunProfileId." + profile_id)
    if id_pos < 0:
        return None

    start = text.rfind("new DragonKingBossGunShotProfile", 0, id_pos)
    if start < 0:
        return None

    return extract_block(text[start:], "new DragonKingBossGunShotProfile")


def main() -> int:
    profile_text = PROFILE_SOURCE.read_text(encoding="utf-8-sig")
    energy_block = extract_profile_block(profile_text, "Energy")
    if energy_block is None:
        return fail("missing Energy profile block")

    required_profile_snippets = [
        "Calibers = new[] { \"PWS\" }",
        "LifetimeFactor = 0.5f",
        "SplitActivationDelay = 0.1f",
        "SplitInvulnerableDuration = 0.1f",
        "SplitOrbitRadius = 0.65f",
        "SplitOrbitAngularSpeed = 900f",
        "SplitTraceAbility = 0.65f",
        "SplitMaxLifetimeSeconds = 2f",
    ]
    for snippet in required_profile_snippets:
        if snippet not in energy_block:
            return fail("missing Energy profile snippet -> " + snippet)

    runtime_text = RUNTIME_SOURCE.read_text(encoding="utf-8-sig")
    if "Mathf.Max(0.05f, profile.LifetimeFactor)" not in runtime_text:
        return fail("projectile distance does not apply profile LifetimeFactor")
    if "isSecondary && profile.SplitMaxLifetimeSeconds > 0f" not in runtime_text:
        return fail("secondary projectile distance does not apply SplitMaxLifetimeSeconds")
    if "context.distance = Mathf.Max(0.4f, context.speed * profile.SplitMaxLifetimeSeconds)" not in runtime_text:
        return fail("SplitMaxLifetimeSeconds must be converted to distance from current speed")
    if "bool forceHorizontalScatter = profile.Id == DragonKingBossGunProfileId.Energy;" not in runtime_text:
        return fail("Energy split directions must force world-horizontal scatter")
    if "else if (forceHorizontalScatter)" not in runtime_text:
        return fail("Energy split directions must keep non-gravity split bullets on the horizontal plane")

    core_text = RUNTIME_CORE_SOURCE.read_text(encoding="utf-8-sig")
    if "internal static CharacterMainControl GetTraceTarget(ItemAgent_Gun gun)" not in core_text:
        return fail("trace target accessor must stay available to projectile agent")

    asset_text = ASSET_SOURCE.read_text(encoding="utf-8-sig")
    required_asset_snippets = [
        "private static HashSet<string> missingPrefabCache = new HashSet<string>();",
        "if (missingPrefabCache.Contains(name))",
        "missingPrefabCache.Add(name);",
        "private static GameObject CreateFallbackEffect(string prefabName, Vector3 position, Quaternion rotation, Transform parent)",
        "return CreateFallbackEffect(prefabName, position, rotation, parent);",
        "case \"Fx_DragonGun_Energy_Trail\":",
        "case \"Fx_DragonGun_Energy_Hit\":",
        "case \"Fx_DragonGun_Energy_Explosion\":",
    ]
    for snippet in required_asset_snippets:
        if snippet not in asset_text:
            return fail("missing asset fallback snippet -> " + snippet)

    agent_text = AGENT_SOURCE.read_text(encoding="utf-8-sig")
    required_agent_snippets = [
        "private bool UpdateSplitWarmup(float deltaTime)",
        "distanceThisFrameRef(projectile) = 0f;",
        "return true;",
        "if (UpdateSplitWarmup(deltaTime))",
        "firstFrameRef(projectile) = false;",
        "private void TryRefreshTraceTarget(float deltaTime)",
        "private bool mandatorySplitTraceRefresh;",
        "private Transform explicitTraceTargetTransform;",
        "private struct TraceTargetCandidate",
        "mandatorySplitTraceRefresh = isSecondary &&",
        "currentTraceTargetUsable && !mandatorySplitTraceRefresh",
        "TraceTargetCandidate target = mandatorySplitTraceRefresh ? FindNearestTraceTarget() : GetGunTraceTargetCandidate();",
        "profile.Id != DragonKingBossGunProfileId.Energy",
        "DragonKingBossGunRuntime.GetTraceTarget(sourceGun)",
        "private TraceTargetCandidate FindNearestTraceTarget()",
        "private TraceTargetCandidate FindNearestTraceTargetAround(Vector3 center, float radius)",
        "private bool IsTraceReceiverUsable(DamageReceiver receiver, CharacterMainControl candidateCharacter, bool allowBaseSceneReceivers)",
        "private bool IsBaseScene()",
        "if (splitSourceReceiverId >= 0 && receiverId == splitSourceReceiverId)",
        "return Team.IsEnemy(projectile.context.team, receiver.Team);",
        "private bool TryGetOfficialTraceCenter(out Vector3 traceCenter)",
        "LevelManager.Instance.InputManager.InputAimPoint",
        "FindNearestTraceTargetAround(traceCenter, 8f)",
        "Physics.OverlapSphereNonAlloc(",
        "private void SetTraceTarget(CharacterMainControl target)",
        "private void SetTraceTarget(Transform targetTransform)",
        "private void ApplyTraceTargetContext(CharacterMainControl traceTarget)",
        "context.traceTarget = traceTarget;",
        "context.ignoreHalfObsticle = true;",
        "context.critRate = 1f;",
        "explicitTraceTargetTransform = targetTransform;",
        "HasUsableTraceTarget()",
        "Vector3.Lerp(projectile.context.direction, targetDirection, customTraceLerp)",
    ]
    for snippet in required_agent_snippets:
        if snippet not in agent_text:
            return fail("missing projectile agent snippet -> " + snippet)

    trace_refresh_body = extract_block(agent_text, "private void TryRefreshTraceTarget(float deltaTime)")
    if trace_refresh_body is None:
        return fail("missing TryRefreshTraceTarget body")
    if "!secondaryProjectile" in trace_refresh_body:
        return fail("Energy primary projectile trace fallback must not be blocked by secondary-only guard")

    print("DragonKingBossGunEnergyPwsGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
