from pathlib import Path
import re
import sys
from cs_source_util import clean_source


REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
    # 2026-09-23 审美审查：奖励选择面板与终端服务面板 / 交互体从 ZombieModeRewards.cs（宿主 partial）拆到独立文件，
    # 结构断言照旧覆盖它们。
    Path("ZombieMode/ZombieModeRewardSelectionView.cs"),
    Path("ZombieMode/ZombieModeTemporaryNpcServiceView.cs"),
]


def read_rewards() -> str:
    return "\n".join(clean_source(path.read_text(encoding="utf-8")) for path in REWARD_PARTS)

CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
ASSEMBLY = Path("ModBehaviourRuntimeModules.cs")
NPC_EFFECTS = Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs")
NURSE = Path("Integration/NPCs/Nurse/NurseNPC.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def compact(text: str) -> str:
    return re.sub(r"\s+", "", clean_source(text))


def method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    opening = text.find("{", start)
    depth = 0
    for index in range(opening, len(text)):
        depth += (text[index] == "{") - (text[index] == "}")
        if depth == 0:
            return text[opening:index + 1]
    return ""


def check_runtime_binding() -> int:
    assembly = clean_source(ASSEMBLY.read_text(encoding="utf-8-sig"))
    effects = clean_source(NPC_EFFECTS.read_text(encoding="utf-8-sig"))
    nurse = clean_source(NURSE.read_text(encoding="utf-8-sig"))
    registration = compact(method(assembly, "private void RegisterRuntimeModules()"))
    expected_registration = compact("""
        runtimeModuleHost.Register(modeFRuntime);
        var zombieRuntime = new ZombieModeRuntimeModule();
        zombieRuntime.BindEnemyRecoveryUnregister(UnregisterEnemyRecovery);
        zombieRuntime.BindZombieModeTemporaryNpcServices(ResolveZombieModeTemporaryNpcPrefab, courierNpcRuntime.AddCourierInteraction);
        runtimeModuleHost.Register(zombieRuntime);
        runtimeModuleHost.Register(new ModeGRuntimeModule());
    """)
    if expected_registration not in registration or registration.count("newZombieModeRuntimeModule()") != 1:
        return fail("ZombieModeTemporaryNpcBoundaryGuard: same Zombie runtime must bind NPC services before registration between Mode F and Mode G")
    resolver = compact(method(assembly, "private UnityEngine.GameObject ResolveZombieModeTemporaryNpcPrefab(string kind)"))
    if resolver != compact("""{
        switch (kind) {
            case "Goblin": return goblinNpcRuntime.LoadGoblinAssetBundle() ? goblinNpcRuntime.GoblinPrefab : null;
            case "Nurse": return NurseNpcRuntimeModule.GetPrefabForRuntime();
            case "Courier": return courierNpcRuntime.LoadCourierAssetBundle() ? CourierNpcRuntimeModule.CourierPrefab : null;
            default: return null;
        }
    }"""):
        return fail("ZombieModeTemporaryNpcBoundaryGuard: NPC resolver must preserve each original loader and prefab query exactly once")
    if compact(method(nurse, "internal static GameObject GetPrefabForRuntime()")) != compact("{ return LoadNurseAssetBundle() ? nursePrefab : null; }"):
        return fail("ZombieModeTemporaryNpcBoundaryGuard: Nurse prefab query must load once before reading the prefab")
    if "internal sealed partial class ZombieModeRuntimeModule" not in effects or "partial class ModBehaviour" in effects:
        return fail("ZombieModeTemporaryNpcBoundaryGuard: temporary NPC business and delegates must belong to the runtime module")
    for field in ["private System.Func<string, GameObject> resolveZombieModeNpcPrefab;",
                  "private System.Action<GameObject> addZombieModeCourierInteraction;"]:
        if field not in effects:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: required NPC dependency must have no fallback initializer -> " + field)
    if compact(method(effects, "internal void BindZombieModeTemporaryNpcServices(")) != compact("""{
        resolveZombieModeNpcPrefab = resolvePrefab;
        addZombieModeCourierInteraction = addCourierInteraction;
    }"""):
        return fail("ZombieModeTemporaryNpcBoundaryGuard: module must retain both injected NPC services")
    for kind in ["Goblin", "Nurse", "Courier"]:
        create = compact(method(effects, "private GameObject CreateZombieModeTemporary" + kind + "Npc("))
        expected = compact('GameObject prefab = resolveZombieModeNpcPrefab("' + kind + '"); if (prefab == null) { return null; } GameObject npc = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);')
        if expected not in create or create.count("resolveZombieModeNpcPrefab(") != 1:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: " + kind + " creation must resolve once at the original load point before instantiation")
        if kind == "Courier" and compact("controller.StartTalking(false); addZombieModeCourierInteraction(npc); return npc;") not in create:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: Courier interaction binding must follow the original controller setup")
    terminal = compact(method(effects, "private GameObject CreateZombieModeTemporaryServiceTerminal("))
    if compact('GameObject lookPrefab = nurseTerminal ? resolveZombieModeNpcPrefab("Nurse") : resolveZombieModeNpcPrefab("Courier");') not in terminal:
        return fail("ZombieModeTemporaryNpcBoundaryGuard: terminal appearance must resolve only the selected original NPC prefab")
    return 0


def main() -> int:
    result = check_runtime_binding()
    if result:
        return result
    rewards = read_rewards()
    catalog = CATALOG.read_text(encoding="utf-8")

    for token in [
        "private GameObject CreateZombieModeTemporaryServiceTerminal(",
        "private ZombieModeTemporaryNpc CreateZombieModeTemporaryNpcRecord(",
        "SpawnZombieModeTemporaryNpc(runId, pendingTemporaryNpcServiceType, extractionOpportunity)",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc",
        "record.ServiceState = CreateZombieModeNpcServiceState(serviceType, bossNodeStock, runState.ActiveSafeZoneActive)",
        "ZombieModeNpcCatalog.NormalWaveStock",
        "ZombieModeNpcCatalog.BossNodeStock",
        "ZombieModeNpcCatalog.NurseServices",
    ]:
        if token not in rewards:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: missing run-only service terminal token -> " + token)

    for token in [
        "NPCModuleRegistry",
        "DuckovDialogueActor",
        "StockShop",
        "NPCShopInteractable",
        "NPCGiftInteractable",
        "AffinityManager",
    ]:
        if token in rewards:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: temporary service terminal must not bind normal NPC module systems -> " + token)

    for token in [
        "public static readonly MerchantStockEntry[] NormalWaveStock",
        "public static readonly MerchantStockEntry[] BossNodeStock",
        "public static readonly NurseServiceEntry[] NurseServices",
        "public static float GetPollutionPriceMultiplier",
    ]:
        if token not in catalog:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: service catalog missing token -> " + token)

    print("ZombieModeTemporaryNpcBoundaryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
