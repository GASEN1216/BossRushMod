"""哥布林 RuntimeModule 单实例与宿主兼容桥守卫。"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source
sys.path.insert(0, str(ROOT / "tools"))
from compile_list import read_compile_sources

MODULE = Path("Integration/NPCs/Goblin/GoblinNPC.cs")
BRIDGE = Path("Integration/IntegrationHostCompatibility.cs")
REGISTRATION = Path("ModBehaviourRuntimeModules.cs")


def fail(message):
    print("GoblinRuntimeModuleGuard: FAIL - " + message)
    return 1


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    module_path = ROOT / MODULE
    bridge_path = ROOT / BRIDGE
    registration_path = ROOT / REGISTRATION
    if not module_path.is_file():
        return fail("missing " + MODULE.as_posix())
    if not bridge_path.is_file():
        return fail("missing " + BRIDGE.as_posix())
    if not registration_path.is_file():
        return fail("missing " + REGISTRATION.as_posix())

    module = clean_source(module_path.read_text(encoding="utf-8-sig"))
    bridge = clean_source(bridge_path.read_text(encoding="utf-8-sig"))
    registration = clean_source(registration_path.read_text(encoding="utf-8-sig"))

    try:
        require("internal sealed class GoblinNpcRuntimeModule : BossRushRuntimeModuleBase" in module,
                MODULE.as_posix() + " must own the Goblin runtime state")
        require("public partial class ModBehaviour" not in module,
                MODULE.as_posix() + " must not remain a ModBehaviour partial")
        for field in ("goblinNPCInstance", "goblinController", "goblinAssetBundle", "goblinPrefab"):
            require(re.search(r"\bprivate\s+(?:static\s+)?[\w<>]+\s+" + field + r"\s*=", module) is not None,
                    "module-owned field missing: " + field)
        require("public override void OnAwake(ModBehaviour owner)" in module
                and "this.owner = owner;" in module,
                "runtime module must bind its host owner")
        require(re.search(r"public override void OnDestroy\(\)\s*\{\s*owner\s*=\s*null;", module) is not None,
                "runtime module must release its host reference on destroy")

        require("private GoblinNpcRuntimeModule goblinNpcRuntime;" in bridge,
                "host bridge must hold the registered runtime instance")
        require(re.search(r"private\s+GameObject\s+goblinNPCInstance\s*\{\s*get\s*\{", bridge) is not None
                and not re.search(r"private\s+GameObject\s+goblinNPCInstance\s*\{[^}]*\bset\b", bridge, re.S),
                "legacy goblinNPCInstance must be a read-only forwarding property")
        require(re.search(r"private\s+GoblinNPCController\s+goblinController\s*\{\s*get\s*\{", bridge) is not None,
                "legacy goblinController must be a forwarding property")
        require(re.search(r"private\s+GameObject\s+goblinPrefab\s*\{\s*get\s*\{", bridge) is not None
                and "goblinNpcRuntime.GoblinPrefab" in bridge,
                "ZombieMode must read the module-owned shared prefab cache")

        for signature in (
                "public void SpawnGoblinNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)",
                "public void DestroyGoblinNPC()", "public void SummonGoblin()",
                "public GoblinNPCController GetGoblinController()"):
            require(signature in bridge, "legacy public signature changed: " + signature)
        for call in (
                "goblinNpcRuntime.SpawnGoblinNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn);",
                "goblinNpcRuntime.DestroyGoblinNPC();", "goblinNpcRuntime.SummonGoblin();",
                "goblinNpcRuntime.GetGoblinController()"):
            require(call in bridge, "host compatibility bridge must forward: " + call)

        require("goblinNpcRuntime = new GoblinNpcRuntimeModule();" in registration
                and "runtimeModuleHost.Register(goblinNpcRuntime);" in registration,
                "RegisterRuntimeModules must store and register the same GoblinNpcRuntimeModule instance")
        new_sites = []
        source_paths = {ROOT / line for line in read_compile_sources()}
        source_paths.update((module_path, bridge_path, registration_path))
        for path in source_paths:
            if not path.is_file():
                continue
            text = clean_source(path.read_text(encoding="utf-8-sig"))
            if "new GoblinNpcRuntimeModule(" in text:
                new_sites.append(path.relative_to(ROOT).as_posix())
        require(new_sites == [REGISTRATION.as_posix()],
                "GoblinNpcRuntimeModule must have one creation site in registration; found: "
                + ", ".join(sorted(new_sites)))
    except AssertionError as error:
        return fail(str(error))

    print("GoblinRuntimeModuleGuard: PASS (single owner + legacy forwarding + registered instance)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
