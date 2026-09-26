from pathlib import Path
import re
import sys

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
MODULE = "Integration/NPCs/Courier/CourierNpcRuntimeModule.cs"
BRIDGE = "Integration/IntegrationHostCompatibility.cs"


def read_source(relative_path: str) -> str:
    return clean_source((ROOT / relative_path).read_text(encoding="utf-8-sig"))


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        return ""
    brace_start = source.find("{", start)
    if brace_start < 0:
        return ""
    depth = 0
    for index in range(brace_start, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    return ""


def fail(message: str) -> int:
    print("CourierNpcRuntimeModuleOwnershipGuard: FAIL - " + message)
    return 1


def main() -> int:
    module = read_source(MODULE)
    bridge = read_source(BRIDGE)

    if module.count("internal sealed class CourierNpcRuntimeModule : BossRushRuntimeModuleBase") != 1:
        return fail("Courier runtime owner type is missing or duplicated")
    if 'return "CourierNPC";' not in module:
        return fail("courier module name changed")
    if "public override void OnAwake(ModBehaviour owner)" not in module or "this.owner = owner;" not in module:
        return fail("courier runtime owner is not attached during OnAwake")
    if "public override void OnDestroy()" not in module or "owner = null;" not in module:
        return fail("courier runtime owner is not released during OnDestroy")

    owner_fields = (
        "private GameObject courierNPCInstance = null;",
        "private CourierNPCController courierController = null;",
        "private static AssetBundle courierAssetBundle = null;",
        "private static GameObject courierPrefab = null;",
    )
    for field in owner_fields:
        if module.count(field) != 1:
            return fail("module must own exactly one " + field)
    for duplicate_field in (
        r"private\s+GameObject\s+courierNPCInstance\s*;",
        r"private\s+CourierNPCController\s+courierController\s*;",
        r"private\s+static\s+AssetBundle\s+courierAssetBundle\s*;",
        r"private\s+static\s+GameObject\s+courierPrefab\s*;",
    ):
        if re.search(duplicate_field, bridge):
            return fail("host bridge contains a mirrored mutable courier field")

    for property_source in (
        "get { return courierNpcRuntime != null ? courierNpcRuntime.CourierNPCInstance : null; }",
        "get { return courierNpcRuntime != null ? courierNpcRuntime.CourierController : null; }",
        "get { return CourierNpcRuntimeModule.CourierPrefab; }",
    ):
        if property_source not in bridge:
            return fail("host compatibility property does not forward the single owner state -> " + property_source)

    bridge_methods = {
        "private bool LoadCourierAssetBundle()": "courierNpcRuntime.LoadCourierAssetBundle()",
        "private void AddCourierInteraction(GameObject courier)": "courierNpcRuntime.AddCourierInteraction(courier)",
        "public void SpawnCourierNPC()": "courierNpcRuntime.SpawnCourierNPC()",
        "public void DestroyCourierNPC()": "courierNpcRuntime.DestroyCourierNPC()",
        "public void NotifyCourierBossFightStart()": "courierNpcRuntime.NotifyCourierBossFightStart()",
        "public void NotifyCourierBossFightEnd()": "courierNpcRuntime.NotifyCourierBossFightEnd()",
        "public void NotifyCourierNoBoss(bool noBoss)": "courierNpcRuntime.NotifyCourierNoBoss(noBoss)",
        "public void NotifyCourierBossRushCompleted()": "courierNpcRuntime.NotifyCourierBossRushCompleted()",
        "public void TeleportToCourierNPC()": "courierNpcRuntime.TeleportToCourierNPC()",
    }
    for signature, call in bridge_methods.items():
        body = extract_method(bridge, signature)
        if not body or call not in body:
            return fail("host compatibility entry does not forward -> " + signature)

    spawn = extract_method(module, "public void SpawnCourierNPC()")
    if not spawn:
        return fail("runtime module does not own SpawnCourierNPC")
    spawn_order = (
        "if (courierNPCInstance != null)",
        "if (!LoadCourierAssetBundle())",
        "SceneManager.GetActiveScene().name",
        "owner.IsAnyBossRushLikeModeActive()",
        "UnityEngine.AI.NavMesh.SamplePosition",
        "UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity)",
        "AddComponent<CourierNPCController>()",
        "AddComponent<CourierMovement>()",
        "AddCourierInteraction(courierNPCInstance)",
    )
    positions = [spawn.find(token) for token in spawn_order]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        return fail("spawn checks, placement, component, or interaction order changed")

    loader = extract_method(module, "internal bool LoadCourierAssetBundle()")
    if '"couriernpc"' not in loader or '"CourierNPC"' not in loader or '"[CourierNPC]"' not in loader:
        return fail("courier resource identifiers changed")

    destroy = extract_method(module, "public void DestroyCourierNPC()")
    release = destroy.find("CourierPaidLootSweepService.ReleasePendingSweepResultToPlayer(true, false);")
    destroy_object = destroy.find("UnityEngine.Object.Destroy(courierNPCInstance);")
    if release < 0 or destroy_object < 0 or release > destroy_object:
        return fail("destroy must release the pending sweep result before destroying the courier")

    if "Health.On" in module or "SavesSystem.On" in module or "StartCoroutine(" in module or "IEnumerator " in module:
        return fail("courier leaf unexpectedly gained an event subscription or coroutine")

    registration = read_source("ModBehaviourRuntimeModules.cs")
    if registration.count("courierNpcRuntime = new CourierNpcRuntimeModule();") != 1:
        return fail("exactly one courier module instance must be created")
    if registration.count("runtimeModuleHost.Register(courierNpcRuntime);") != 1:
        return fail("the courier runtime host must register the same module instance once")

    compile_list = read_source("compile_official.bat")
    if compile_list.count("Integration\\NPCs\\Courier\\CourierNpcRuntimeModule.cs") != 1:
        return fail("runtime module must appear exactly once in the official compile list")

    print("CourierNpcRuntimeModuleOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
