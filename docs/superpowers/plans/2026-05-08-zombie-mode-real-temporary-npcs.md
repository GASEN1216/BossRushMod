# ZombieMode Real Temporary NPCs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three low-probability ZombieMode reward options that summon real Goblin, Nurse, and Awen NPCs, keep their original interaction flows, and switch their paid actions to purification points without affecting non-ZombieMode NPC behavior.

**Architecture:** Extend the ZombieMode reward model with three new NPC rewards, add a ZombieMode-only runtime marker and tracking path for real summoned NPCs, and adapt Goblin/Nurse/Courier payment sites to branch to purification-point payment only when the interacting NPC carries that runtime marker. Reuse existing prefab loading, controller setup, protection tick, and run cleanup rather than creating parallel NPC systems.

**Tech Stack:** C# 7.3, Unity runtime components, Duckov NPC/economy systems, static Python guard scripts, batch compile/test scripts

---

### Task 1: Guard the reward-model contract before implementation

**Files:**
- Modify: `tests/ZombieModeRewardOptionsExpansionGuard.py`
- Modify: `tests/ZombieModeLocalizationGuard.py`
- Create: `tests/ZombieModeRealTemporaryNpcRewardGuard.py`
- Create: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
- Create: `tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
- Modify: `tests/README.md`

- [ ] **Step 1: Write the failing reward-model guard**

```python
from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")

REWARD_TYPES = [
    "TempGoblinNpc",
    "TempNurseNpc",
    "TempCourierNpc",
]


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcRewardGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for reward_type in REWARD_TYPES:
        if reward_type not in models:
            return fail("reward enum missing -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("reward wiring missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in rewards:
            return fail("reward display missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("reward localization missing -> " + reward_type)

    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempMerchant, ZombieModeRewardCategory.Npc, 10);" not in rewards:
        return fail("baseline merchant weight check missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempGoblinNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("goblin low-weight reward entry missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurseNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("nurse low-weight reward entry missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempCourierNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("courier low-weight reward entry missing")

    print("ZombieModeRealTemporaryNpcRewardGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 2: Run guard to verify it fails**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py`
Expected: FAIL because the new reward enum values and low-weight catalog entries do not exist yet

- [ ] **Step 3: Write the failing payment-isolation guard**

```python
from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REFORGE_UI = Path("Integration/Reforge/ReforgeUIManager.cs")
NURSE = Path("Integration/NPCs/Nurse/NurseHealInteractable.cs")
COURIER_SERVICE = Path("Integration/NPCs/Courier/CourierService.cs")
COURIER_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcPaymentGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    reforge_ui = REFORGE_UI.read_text(encoding="utf-8")
    nurse = NURSE.read_text(encoding="utf-8")
    courier_service = COURIER_SERVICE.read_text(encoding="utf-8")
    courier_sweep = COURIER_SWEEP.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeTemporaryRealNpcMarker",
        "public int RunId;",
        "public string NpcType",
        "public bool UsesPurificationPayment;",
    ]:
        result = require(models, snippet, "real NPC marker model")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc(",
        "CanAffordZombieModePurificationPointsForRealNpc(",
        "IsZombieModeTemporaryRealNpc(",
    ]:
        result = require(rewards, snippet, "reward payment helper")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "GetZombieModePurificationPointsForRealNpcUi",
    ]:
        result = require(reforge_ui, snippet, "goblin reforge purification path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "GetZombieModeNpcHealCurrencyLabel",
    ]:
        result = require(nurse, snippet, "nurse purification path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
    ]:
        result = require(courier_service, snippet, "courier purification delivery path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "RefundZombieModePurificationPointsForRealNpc",
    ]:
        result = require(courier_sweep, snippet, "courier sweep purification path")
        if result:
            return result

    print("ZombieModeRealTemporaryNpcPaymentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run guard to verify it fails**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: FAIL because the real-NPC payment marker/helper contract does not exist yet

- [ ] **Step 5: Write the failing cleanup guard**

```python
from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcCleanupGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    drops = DROPS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeTemporaryRealNpcRecord",
        "public readonly List<ZombieModeTemporaryRealNpcRecord> TemporaryRealNpcs = new List<ZombieModeTemporaryRealNpcRecord>();",
    ]:
        result = require(models, snippet, "real NPC run-state storage")
        if result:
            return result

    for snippet in [
        "RecycleZombieModeTemporaryRealNpcs(runId);",
        "RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);",
    ]:
        result = require(drops, snippet, "real NPC cleanup wiring")
        if result:
            return result

    for snippet in [
        "AttachZombieModeTemporaryRealNpcMarker(",
        "zombieModeRunState.TemporaryRealNpcs.Add(record);",
    ]:
        result = require(rewards, snippet, "real NPC spawn tracking")
        if result:
            return result

    print("ZombieModeRealTemporaryNpcCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 6: Run guard to verify it fails**

Run: `python tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
Expected: FAIL because the separate real-NPC tracking and cleanup contract does not exist yet

- [ ] **Step 7: Update test docs and aggregate reward/localization guards**

```python
REWARD_TYPES.extend([
    "TempGoblinNpc",
    "TempNurseNpc",
    "TempCourierNpc",
])
```

```python
REQUIRED_KEYS.extend([
    "BossRush_ZombieMode_Reward_TempGoblinNpc",
    "BossRush_ZombieMode_Reward_TempNurseNpc",
    "BossRush_ZombieMode_Reward_TempCourierNpc",
    "BossRush_ZombieMode_Npc_TempGoblinNpc",
    "BossRush_ZombieMode_Npc_TempNurseNpcReal",
    "BossRush_ZombieMode_Npc_TempCourierNpc",
])
```

- [ ] **Step 8: Run the three new guards plus the two updated shared guards**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRealTemporaryNpcPaymentGuard.py && python tests/ZombieModeRealTemporaryNpcCleanupGuard.py && python tests/ZombieModeRewardOptionsExpansionGuard.py && python tests/ZombieModeLocalizationGuard.py`
Expected: FAIL on the new real-NPC checks before implementation; shared guards may also fail once expanded

- [ ] **Step 9: Commit the red guard state**

```bash
git add tests/ZombieModeRealTemporaryNpcRewardGuard.py tests/ZombieModeRealTemporaryNpcPaymentGuard.py tests/ZombieModeRealTemporaryNpcCleanupGuard.py tests/ZombieModeRewardOptionsExpansionGuard.py tests/ZombieModeLocalizationGuard.py tests/README.md
git commit -m "test(zombie): add real temporary npc guard coverage"
```

### Task 2: Extend the ZombieMode reward model and localization

**Files:**
- Modify: `ZombieMode/ZombieModeModels.cs`
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeRealTemporaryNpcRewardGuard.py`
- Test: `tests/ZombieModeRewardOptionsExpansionGuard.py`
- Test: `tests/ZombieModeLocalizationGuard.py`

- [ ] **Step 1: Add the failing enum and display/localization contract**

```csharp
public enum ZombieModeRewardType
{
    PurificationPoints,
    Heal,
    RandomSupply,
    RandomHighQualityItem,
    StarterReroll,
    RandomMeleeWeapon,
    RandomGunWithAmmo,
    AmmoSupply,
    MedicalSupply,
    ArmorOrHelmet,
    CurrentNodeFreeRefresh,
    NextNodeFreeRefresh,
    HalfPricePaidRefresh,
    AttributeMaxHealth,
    AttributeMoveSpeed,
    AttributeMeleeDamage,
    AttributeRangedDamage,
    AttributeReloadSpeed,
    AttributeDamageReduction,
    TempMerchant,
    TempNurse,
    TempGoblinNpc,
    TempNurseNpc,
    TempCourierNpc,
    FortificationPack,
}
```

```csharp
AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempMerchant, ZombieModeRewardCategory.Npc, 10);
AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurse, ZombieModeRewardCategory.Npc, 10);
AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempGoblinNpc, ZombieModeRewardCategory.Npc, 3);
AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurseNpc, ZombieModeRewardCategory.Npc, 3);
AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempCourierNpc, ZombieModeRewardCategory.Npc, 3);
```

```csharp
InjectZombieModeString("BossRush_ZombieMode_Reward_TempGoblinNpc", "召唤叮当：可花净化点重铸", "Summon Dingdang: Reforge with Purification");
InjectZombieModeString("BossRush_ZombieMode_Reward_TempNurseNpc", "召唤羽织：可花净化点治疗", "Summon Yuzhi: Heal with Purification");
InjectZombieModeString("BossRush_ZombieMode_Reward_TempCourierNpc", "召唤阿稳：可花净化点使用服务", "Summon Awen: Services with Purification");
InjectZombieModeString("BossRush_ZombieMode_Npc_TempGoblinNpc", "叮当已抵达安全区", "Dingdang reached the safe zone");
InjectZombieModeString("BossRush_ZombieMode_Npc_TempNurseNpcReal", "羽织已抵达安全区", "Yuzhi reached the safe zone");
InjectZombieModeString("BossRush_ZombieMode_Npc_TempCourierNpc", "阿稳已抵达安全区", "Awen reached the safe zone");
```

- [ ] **Step 2: Run reward-model and localization guards to verify they still fail on spawn/payment wiring**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRewardOptionsExpansionGuard.py && python tests/ZombieModeLocalizationGuard.py`
Expected: reward/localization failures narrow down to missing full wiring or runtime spawn logic, not enum absence

- [ ] **Step 3: Finish reward-category, display, and selection wiring**

```csharp
case ZombieModeRewardType.TempGoblinNpc:
case ZombieModeRewardType.TempNurseNpc:
case ZombieModeRewardType.TempCourierNpc:
    return ZombieModeRewardCategory.Npc;
```

```csharp
case ZombieModeRewardType.TempGoblinNpc:
    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempGoblinNpc");
case ZombieModeRewardType.TempNurseNpc:
    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempNurseNpc");
case ZombieModeRewardType.TempCourierNpc:
    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempCourierNpc");
```

```csharp
private string GetZombieModePendingTemporaryNpcServiceType(ZombieModeRewardType rewardType)
{
    if (rewardType == ZombieModeRewardType.TempMerchant) return "Merchant";
    if (rewardType == ZombieModeRewardType.TempNurse) return "Nurse";
    return string.Empty;
}
```

- [ ] **Step 4: Run reward-model and localization guards to verify they pass**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRewardOptionsExpansionGuard.py && python tests/ZombieModeLocalizationGuard.py`
Expected: PASS for reward/localization coverage

- [ ] **Step 5: Commit the reward-model extension**

```bash
git add ZombieMode/ZombieModeModels.cs ZombieMode/ZombieModeRewards.cs Localization/LocalizationInjector.cs tests/ZombieModeRealTemporaryNpcRewardGuard.py tests/ZombieModeRewardOptionsExpansionGuard.py tests/ZombieModeLocalizationGuard.py
git commit -m "feat(zombie): add real temporary npc reward types"
```

### Task 3: Add ZombieMode temporary real NPC runtime state and cleanup scaffolding

**Files:**
- Modify: `ZombieMode/ZombieModeModels.cs`
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `ZombieMode/ZombieModeDropsAndPerformance.cs`
- Modify: `ZombieMode/ZombieModeExtractionController.cs`
- Test: `tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
- Test: `tests/ZombieModeTemporaryNpcProtectionGuard.py`

- [ ] **Step 1: Add failing runtime model structures**

```csharp
public sealed class ZombieModeTemporaryRealNpcRecord
{
    public GameObject GameObject;
    public string NpcType;
    public int SpawnWave;
    public bool SafeZoneBound;
}

public sealed class ZombieModeTemporaryRealNpcMarker : MonoBehaviour
{
    public int RunId;
    public string NpcType = string.Empty;
    public bool UsesPurificationPayment = true;
}
```

```csharp
public readonly List<ZombieModeTemporaryRealNpcRecord> TemporaryRealNpcs = new List<ZombieModeTemporaryRealNpcRecord>();
```

- [ ] **Step 2: Run cleanup guard to verify it fails on missing cleanup plumbing**

Run: `python tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
Expected: FAIL because cleanup registration and spawn tracking are still missing

- [ ] **Step 3: Add cleanup helpers and protection reuse plumbing**

```csharp
private void RecycleZombieModeTemporaryRealNpcs(int runId)
{
    if (!IsZombieModeRunValid(runId) || zombieModeRunState.TemporaryRealNpcs.Count <= 0)
    {
        return;
    }

    for (int i = zombieModeRunState.TemporaryRealNpcs.Count - 1; i >= 0; i--)
    {
        ZombieModeTemporaryRealNpcRecord record = zombieModeRunState.TemporaryRealNpcs[i];
        if (record == null || record.GameObject == null)
        {
            zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
            continue;
        }

        UnityEngine.Object.Destroy(record.GameObject);
        zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
    }
}
```

```csharp
private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)
{
    if (!IsZombieModeRunValid(runId) || zombieModeRunState.TemporaryRealNpcs.Count <= 0)
    {
        return;
    }

    for (int i = zombieModeRunState.TemporaryRealNpcs.Count - 1; i >= 0; i--)
    {
        ZombieModeTemporaryRealNpcRecord record = zombieModeRunState.TemporaryRealNpcs[i];
        if (record == null || !record.SafeZoneBound)
        {
            continue;
        }

        if (record.GameObject != null)
        {
            UnityEngine.Object.Destroy(record.GameObject);
        }
        zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
    }
}
```

- [ ] **Step 4: Register cleanup calls in the same ZombieMode teardown paths as temporary terminals**

Run: `python tests/ZombieModeRealTemporaryNpcCleanupGuard.py && python tests/ZombieModeTemporaryNpcProtectionGuard.py`
Expected: cleanup guard passes; existing protection guard still passes

- [ ] **Step 5: Commit the runtime-state scaffolding**

```bash
git add ZombieMode/ZombieModeModels.cs ZombieMode/ZombieModeRewards.cs ZombieMode/ZombieModeDropsAndPerformance.cs ZombieMode/ZombieModeExtractionController.cs tests/ZombieModeRealTemporaryNpcCleanupGuard.py tests/ZombieModeTemporaryNpcProtectionGuard.py
git commit -m "refactor(zombie): add temporary real npc runtime state"
```

### Task 4: Implement real NPC reward application and spawn tracking

**Files:**
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `ZombieMode/ZombieModeNpcCatalog.cs`
- Modify: `ZombieMode/ZombieModeModels.cs`
- Test: `tests/ZombieModeRealTemporaryNpcRewardGuard.py`
- Test: `tests/ZombieModeRealTemporaryNpcCleanupGuard.py`

- [ ] **Step 1: Add the failing real-NPC reward application path**

```csharp
case ZombieModeRewardType.TempGoblinNpc:
    SpawnZombieModeTemporaryRealNpc(runId, "Goblin");
    return;
case ZombieModeRewardType.TempNurseNpc:
    SpawnZombieModeTemporaryRealNpc(runId, "NurseNpc");
    return;
case ZombieModeRewardType.TempCourierNpc:
    SpawnZombieModeTemporaryRealNpc(runId, "Courier");
    return;
```

```csharp
private void SpawnZombieModeTemporaryRealNpc(int runId, string npcType)
{
    if (!IsZombieModeRunValid(runId) || string.IsNullOrEmpty(npcType))
    {
        return;
    }

    if (FindZombieModeTemporaryRealNpc(npcType) != null)
    {
        return;
    }

    GameObject npc = CreateZombieModeTemporaryRealNpc(runId, npcType);
    if (npc == null)
    {
        GrantZombieModeFallbackPurificationReward("TempRealNpcSpawnFail_" + npcType, 120);
        return;
    }

    AttachZombieModeTemporaryRealNpcMarker(npc, runId, npcType);
    ApplyZombieModeTemporaryNpcProtection(npc, runId, npcType);

    ZombieModeTemporaryRealNpcRecord record = new ZombieModeTemporaryRealNpcRecord();
    record.GameObject = npc;
    record.NpcType = npcType;
    record.SpawnWave = zombieModeRunState.CurrentWave;
    record.SafeZoneBound = zombieModeRunState.ActiveSafeZoneActive;
    zombieModeRunState.TemporaryRealNpcs.Add(record);
    RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, null);
}
```

- [ ] **Step 2: Run reward/cleanup guards to verify they still fail on concrete prefab creation**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
Expected: FAIL because `CreateZombieModeTemporaryRealNpc(...)` and duplicate-filter wiring are still incomplete

- [ ] **Step 3: Implement the three prefab-backed spawn branches**

```csharp
private GameObject CreateZombieModeTemporaryRealNpc(int runId, string npcType)
{
    Vector3 spawnPos = GetZombieModeTemporaryNpcAnchorPosition(npcType);

    if (string.Equals(npcType, "Goblin", System.StringComparison.Ordinal))
    {
        return CreateZombieModeTemporaryGoblinNpc(spawnPos);
    }

    if (string.Equals(npcType, "NurseNpc", System.StringComparison.Ordinal))
    {
        return CreateZombieModeTemporaryNurseNpc(spawnPos);
    }

    if (string.Equals(npcType, "Courier", System.StringComparison.Ordinal))
    {
        return CreateZombieModeTemporaryCourierNpc(spawnPos);
    }

    return null;
}
```

```csharp
private GameObject CreateZombieModeTemporaryGoblinNpc(Vector3 spawnPos)
{
    if (!LoadGoblinAssetBundle() || goblinPrefab == null)
    {
        return null;
    }

    GameObject npc = UnityEngine.Object.Instantiate(goblinPrefab, spawnPos, Quaternion.identity);
    npc.name = "ZombieMode_TemporaryRealNpc_Goblin";
    npc.SetActive(true);
    foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
    {
        child.gameObject.SetActive(true);
    }

    NPCCommonUtils.FixShaders(npc, "[ZombieModeTempGoblin]");
    NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

    GoblinNPCController controller = npc.GetComponent<GoblinNPCController>();
    if (controller == null)
    {
        controller = npc.AddComponent<GoblinNPCController>();
    }

    GoblinMovement movement = npc.GetComponent<GoblinMovement>();
    if (movement == null)
    {
        movement = npc.AddComponent<GoblinMovement>();
    }

    movement.StopMove();
    movement.enabled = false;
    controller.EnterStationaryIdleState();

    GoblinInteractable interactable = npc.GetComponent<GoblinInteractable>();
    if (interactable == null)
    {
        interactable = npc.AddComponent<GoblinInteractable>();
    }

    return npc;
}
```

```csharp
private GameObject CreateZombieModeTemporaryNurseNpc(Vector3 spawnPos)
{
    if (!LoadNurseAssetBundle() || nursePrefab == null)
    {
        return null;
    }

    GameObject npc = UnityEngine.Object.Instantiate(nursePrefab, spawnPos, Quaternion.identity);
    npc.name = "ZombieMode_TemporaryRealNpc_Nurse";
    npc.SetActive(true);
    foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
    {
        child.gameObject.SetActive(true);
    }

    NPCCommonUtils.FixShaders(npc, "[ZombieModeTempNurse]");
    NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

    NurseNPCController controller = npc.GetComponent<NurseNPCController>();
    if (controller == null)
    {
        controller = npc.AddComponent<NurseNPCController>();
    }

    NurseMovement movement = npc.GetComponent<NurseMovement>();
    if (movement == null)
    {
        movement = npc.AddComponent<NurseMovement>();
    }

    movement.StopMove();
    movement.enabled = false;

    NurseInteractable interactable = npc.GetComponent<NurseInteractable>();
    if (interactable == null)
    {
        interactable = npc.AddComponent<NurseInteractable>();
    }

    return npc;
}
```

```csharp
private GameObject CreateZombieModeTemporaryCourierNpc(Vector3 spawnPos)
{
    if (!LoadCourierAssetBundle() || courierPrefab == null)
    {
        return null;
    }

    GameObject npc = UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity);
    npc.name = "ZombieMode_TemporaryRealNpc_Courier";
    npc.SetActive(true);
    foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
    {
        child.gameObject.SetActive(true);
    }

    NPCCommonUtils.FixShaders(npc, "[ZombieModeTempCourier]");
    NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

    CourierNPCController controller = npc.GetComponent<CourierNPCController>();
    if (controller == null)
    {
        controller = npc.AddComponent<CourierNPCController>();
    }

    CourierMovement movement = npc.GetComponent<CourierMovement>();
    if (movement == null)
    {
        movement = npc.AddComponent<CourierMovement>();
    }

    movement.SetStationary(true);
    controller.StartTalking(false);

    AddCourierInteraction(npc);
    return npc;
}
```

- [ ] **Step 4: Add duplicate exclusion from future reward rolls**

```csharp
private bool IsZombieModeRewardAtSelectionCap(ZombieModeRewardType rewardType)
{
    switch (rewardType)
    {
        case ZombieModeRewardType.TempGoblinNpc:
            return FindZombieModeTemporaryRealNpc("Goblin") != null;
        case ZombieModeRewardType.TempNurseNpc:
            return FindZombieModeTemporaryRealNpc("NurseNpc") != null;
        case ZombieModeRewardType.TempCourierNpc:
            return FindZombieModeTemporaryRealNpc("Courier") != null;
    }
    return false;
}
```

- [ ] **Step 5: Run reward/cleanup guards to verify they pass**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
Expected: PASS for reward spawn-tracking and duplicate-exclusion invariants

- [ ] **Step 6: Commit the real NPC spawn implementation**

```bash
git add ZombieMode/ZombieModeRewards.cs ZombieMode/ZombieModeNpcCatalog.cs ZombieMode/ZombieModeModels.cs tests/ZombieModeRealTemporaryNpcRewardGuard.py tests/ZombieModeRealTemporaryNpcCleanupGuard.py
git commit -m "feat(zombie): spawn real temporary npc rewards"
```

### Task 5: Add the shared purification-payment adapter for real temporary NPCs

**Files:**
- Modify: `ZombieMode/ZombieModeRewards.cs`
- Modify: `ZombieMode/ZombieModeModels.cs`
- Test: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`

- [ ] **Step 1: Add the failing shared adapter contract**

```csharp
public bool IsZombieModeTemporaryRealNpc(Component component)
{
    if (component == null)
    {
        return false;
    }

    ZombieModeTemporaryRealNpcMarker marker = component.GetComponentInParent<ZombieModeTemporaryRealNpcMarker>();
    return marker != null &&
           marker.UsesPurificationPayment &&
           IsZombieModeRunValid(marker.RunId);
}
```

```csharp
public bool CanAffordZombieModePurificationPointsForRealNpc(Component component, int cost)
{
    if (!IsZombieModeTemporaryRealNpc(component))
    {
        return false;
    }

    return cost <= 0 || zombieModeRunState.PurificationPoints >= cost;
}
```

```csharp
public bool TrySpendZombieModePurificationPointsForRealNpc(Component component, int cost, string reason)
{
    if (!IsZombieModeTemporaryRealNpc(component))
    {
        return false;
    }

    return SpendZombieModePurificationPoints(cost, reason);
}
```

```csharp
public void RefundZombieModePurificationPointsForRealNpc(Component component, int cost, bool shouldRefund)
{
    if (!shouldRefund || cost <= 0 || !IsZombieModeTemporaryRealNpc(component))
    {
        return;
    }

    zombieModeRunState.PurificationPoints += cost;
}
```

- [ ] **Step 2: Run payment guard to verify it fails on missing call sites**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: FAIL because Goblin/Nurse/Courier call sites are not yet using the adapter

- [ ] **Step 3: Finish helper coverage for UI-only access**

```csharp
public int GetZombieModePurificationPointsForRealNpcUi(Component component)
{
    return IsZombieModeTemporaryRealNpc(component)
        ? zombieModeRunState.PurificationPoints
        : 0;
}

public string GetZombieModeNpcHealCurrencyLabel(Component component, int cost)
{
    return IsZombieModeTemporaryRealNpc(component)
        ? L10n.T("治疗（净化点 " + cost + "）", "Heal (Purification " + cost + ")")
        : L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")");
}
```

- [ ] **Step 4: Run payment guard to verify helper contract passes**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: still FAIL until service call sites are integrated, but helper-model failures should be gone

- [ ] **Step 5: Commit the shared adapter**

```bash
git add ZombieMode/ZombieModeRewards.cs ZombieMode/ZombieModeModels.cs tests/ZombieModeRealTemporaryNpcPaymentGuard.py
git commit -m "refactor(zombie): add real npc purification payment helpers"
```

### Task 6: Adapt nurse healing to purification points for real temporary nurses

**Files:**
- Modify: `Integration/NPCs/Nurse/NurseHealInteractable.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
- Test: `tests/ZombieModeLocalizationGuard.py`

- [ ] **Step 1: Add the failing nurse UI text branch**

```csharp
string healText = cost <= 0
    ? L10n.T("治疗（不需要）", "Heal (Not needed)")
    : (ModBehaviour.Instance != null && ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(this)
        ? ModBehaviour.Instance.GetZombieModeNpcHealCurrencyLabel(this, cost)
        : L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")"));
```

- [ ] **Step 2: Run payment guard to verify it still fails on payment deduction**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: FAIL because the actual nurse payment path still uses the old cash-only healing service

- [ ] **Step 3: Route nurse affordability and spend through the shared adapter**

```csharp
if (ModBehaviour.Instance != null && ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(this))
{
    if (!ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(this, cost, "ZombieModeTempNurseHeal"))
    {
        if (controller != null)
        {
            controller.ShowDialogueBubble(L10n.T("净化点不够。", "Not enough purification."));
            EndDialogueWithMark(10f);
        }
        return;
    }
}
else
{
    if (!NurseHealingService.TryConsumeHealingCost(cost))
    {
        if (controller != null)
        {
            controller.ShowDialogueBubble(NurseHealingService.GetHealingDialogue(NurseHealingService.HealingStatus.InsufficientFunds));
            EndDialogueWithMark(10f);
        }
        return;
    }
}
```

- [ ] **Step 4: Run nurse-related guards**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py && python tests/ZombieModeLocalizationGuard.py`
Expected: PASS for nurse purification routing and localization coverage

- [ ] **Step 5: Commit the nurse adaptation**

```bash
git add Integration/NPCs/Nurse/NurseHealInteractable.cs Localization/LocalizationInjector.cs tests/ZombieModeRealTemporaryNpcPaymentGuard.py tests/ZombieModeLocalizationGuard.py
git commit -m "feat(zombie): use purification for temporary nurse services"
```

### Task 7: Adapt courier delivery and paid sweep to purification points for real temporary Awen

**Files:**
- Modify: `Integration/NPCs/Courier/CourierService.cs`
- Modify: `Integration/NPCs/Courier/CourierPaidLootSweepService.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`

- [ ] **Step 1: Add the failing courier delivery branch**

```csharp
if (courierNPCTransform != null &&
    ModBehaviour.Instance != null &&
    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
{
    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, fee);
}
```

```csharp
if (courierNPCTransform != null &&
    ModBehaviour.Instance != null &&
    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
{
    return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(courierNPCTransform, fee, "ZombieModeTempCourierDelivery");
}
```

- [ ] **Step 2: Run payment guard to verify it still fails on sweep/refund path**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: FAIL because paid sweep still uses `EconomyManager` directly

- [ ] **Step 3: Adapt paid sweep fee, payment, and refund**

```csharp
if (activeServiceController != null &&
    ModBehaviour.Instance != null &&
    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController))
{
    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(activeServiceController, cost);
}
```

```csharp
if (activeServiceController != null &&
    ModBehaviour.Instance != null &&
    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController))
{
    return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(activeServiceController, cost, "ZombieModeTempCourierSweep");
}
```

```csharp
if (activeServiceController != null &&
    ModBehaviour.Instance != null)
{
    ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(activeServiceController, cost, shouldRefund);
    return;
}
```

- [ ] **Step 4: Run courier payment guard**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: PASS for courier delivery/sweep purification routing

- [ ] **Step 5: Commit the courier adaptation**

```bash
git add Integration/NPCs/Courier/CourierService.cs Integration/NPCs/Courier/CourierPaidLootSweepService.cs Localization/LocalizationInjector.cs tests/ZombieModeRealTemporaryNpcPaymentGuard.py
git commit -m "feat(zombie): use purification for temporary courier services"
```

### Task 8: Adapt goblin reforge UI and payment to purification points for real temporary goblins

**Files:**
- Modify: `Integration/Reforge/ReforgeUIManager.cs`
- Modify: `Localization/LocalizationInjector.cs`
- Test: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`

- [ ] **Step 1: Add the failing max-budget UI branch**

```csharp
private static int GetPlayerMoney()
{
    if (currentController != null &&
        ModBehaviour.Instance != null &&
        ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController))
    {
        return ModBehaviour.Instance.GetZombieModePurificationPointsForRealNpcUi(currentController);
    }

    return (int)EconomyManager.Money;
}
```

- [ ] **Step 2: Run payment guard to verify it still fails on final spend path**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: FAIL because the reforge confirmation still uses `EconomyManager.Pay`

- [ ] **Step 3: Switch final pay path to purification points for marked temporary goblins**

```csharp
if (totalCost > 0)
{
    bool paid = false;
    if (currentController != null &&
        ModBehaviour.Instance != null &&
        ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController))
    {
        paid = ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
            currentController,
            totalCost,
            "ZombieModeTempGoblinReforge");
    }
    else
    {
        Cost cost = new Cost((long)totalCost);
        paid = EconomyManager.Pay(cost, true, true);
    }

    if (!paid)
    {
        ModBehaviour.DevLog("[ReforgeUI] 资金不足");
        isReforging = false;
        return;
    }
}
```

- [ ] **Step 4: Update visible UI copy for purification-backed goblins**

```csharp
string currencyLabel = currentController != null &&
    ModBehaviour.Instance != null &&
    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController)
    ? L10n.T("净化点", "Purification")
    : L10n.T("金钱", "Money");
```

- [ ] **Step 5: Run goblin payment guard**

Run: `python tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
Expected: PASS for goblin purification routing

- [ ] **Step 6: Commit the goblin adaptation**

```bash
git add Integration/Reforge/ReforgeUIManager.cs Localization/LocalizationInjector.cs tests/ZombieModeRealTemporaryNpcPaymentGuard.py
git commit -m "feat(zombie): use purification for temporary goblin reforge"
```

### Task 9: Wire compile/test scripts and run full verification

**Files:**
- Modify: `compile_official.bat`
- Modify: `test_logic_official.bat`
- Modify: `tests/README.md`
- Test: `tests/ZombieModeCompileListGuard.py`
- Test: `tests/ZombieModeRealTemporaryNpcRewardGuard.py`
- Test: `tests/ZombieModeRealTemporaryNpcPaymentGuard.py`
- Test: `tests/ZombieModeRealTemporaryNpcCleanupGuard.py`
- Test: `tests/ZombieModeRewardOptionsExpansionGuard.py`
- Test: `tests/ZombieModeLocalizationGuard.py`

- [ ] **Step 1: Add any new source files to the compile list**

```bat
    ZombieMode\ZombieModeRewards.cs ^
    ZombieMode\ZombieModeRewardEffects.cs ^
    ZombieMode\ZombieModeRewardProjectilePatch.cs ^
```

- [ ] **Step 2: Add the new guard scripts to the documented verification flow**

```md
| `ZombieModeRealTemporaryNpcRewardGuard.py` | 真人临时 NPC 奖励类型、低权重、终端共存契约。 |
| `ZombieModeRealTemporaryNpcPaymentGuard.py` | 真人临时 NPC 的净化点支付隔离契约。 |
| `ZombieModeRealTemporaryNpcCleanupGuard.py` | 真人临时 NPC 的 run cleanup 与保护追踪契约。 |
```

- [ ] **Step 3: Run the focused guard suite**

Run: `python tests/ZombieModeRealTemporaryNpcRewardGuard.py && python tests/ZombieModeRealTemporaryNpcPaymentGuard.py && python tests/ZombieModeRealTemporaryNpcCleanupGuard.py && python tests/ZombieModeRewardOptionsExpansionGuard.py && python tests/ZombieModeLocalizationGuard.py && python tests/ZombieModeCompileListGuard.py`
Expected: PASS

- [ ] **Step 4: Run logic tests**

Run: `cmd.exe /c test_logic_official.bat`
Expected: `Logic tests passed.`

- [ ] **Step 5: Run full compile**

Run: `cmd.exe /c compile_official.bat`
Expected: successful DLL compilation with updated ZombieMode sources

- [ ] **Step 6: Commit the verification wiring**

```bash
git add compile_official.bat test_logic_official.bat tests/README.md
git commit -m "test(zombie): wire real temporary npc verification"
```
