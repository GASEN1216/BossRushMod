"""ArchitectureStructureGuard: core architecture helpers must stay wired without gameplay rewrites."""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")
MOD = Path("ModBehaviour.cs")

REQUIRED_COMPILE_SOURCES = [
    "Common/Lifecycle/IBossRushRuntimeModule.cs",
    "Common/Lifecycle/SceneRuntimeContext.cs",
    "Common/Lifecycle/BossRushRuntimeModuleHost.cs",
    "Common/Lifecycle/BossRushRuntimeModuleBase.cs",
    "Common/Lifecycle/ArchitectureSentinelRuntimeModule.cs",
    "Common/Lifecycle/BossRushRuntimeModuleRegistration.cs",
    "ModeD/ModeDRuntimeModule.cs",
    "Utilities/RuntimeScope.cs",
    "Utilities/SceneRuntimeGate.cs",
]

REQUIRED_MOD_NEEDLES = [
    "private readonly BossRushRuntimeModuleHost runtimeModuleHost = new BossRushRuntimeModuleHost();",
    "RegisterRuntimeModules();",
    "runtimeModuleHost.OnAwake(this);",
    "runtimeModuleHost.OnUpdate(Time.deltaTime, Time.unscaledDeltaTime);",
    "runtimeModuleHost.OnLateUpdate();",
    "runtimeModuleHost.OnStart();",
    "runtimeModuleHost.OnDestroy();",
    "runtimeModuleHost.OnSceneLoaded(new SceneRuntimeContext(scene, mode));",
    "return SceneRuntimeGate.IsBaseHubSceneName(sceneName);",
    "return SceneRuntimeGate.IsGameplaySceneName(sceneName);",
    "return SceneRuntimeGate.CanRunGameplayRuntimeNow(sceneName);",
]


def fail(message: str) -> int:
    print(message)
    return 1


def normalize_slashes(text: str) -> str:
    return re.sub(r"/+", "/", text.replace("\\", "/"))


def main() -> int:
    compile_text = normalize_slashes(COMPILE.read_text(encoding="utf-8", errors="ignore"))
    mod_text = MOD.read_text(encoding="utf-8", errors="ignore")

    missing_files = [path for path in REQUIRED_COMPILE_SOURCES if not Path(path).exists()]
    if missing_files:
        return fail("ArchitectureStructureGuard: missing architecture source file(s): " + ", ".join(missing_files))

    missing_compile_entries = [path for path in REQUIRED_COMPILE_SOURCES if path not in compile_text]
    if missing_compile_entries:
        return fail("ArchitectureStructureGuard: compile_official.bat missing architecture source(s): " + ", ".join(missing_compile_entries))

    missing_mod_hooks = [needle for needle in REQUIRED_MOD_NEEDLES if needle not in mod_text]
    if missing_mod_hooks:
        return fail("ArchitectureStructureGuard: ModBehaviour missing architecture hook(s): " + " | ".join(missing_mod_hooks))

    register_index = mod_text.find("RegisterRuntimeModules();")
    awake_index = mod_text.find("runtimeModuleHost.OnAwake(this);")
    if register_index < 0 or awake_index < 0 or register_index > awake_index:
        return fail("ArchitectureStructureGuard: RegisterRuntimeModules must run before runtimeModuleHost.OnAwake")

    registration_text = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs").read_text(encoding="utf-8", errors="ignore")
    if "runtimeModuleHost.Register(new ArchitectureSentinelRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing ArchitectureSentinelRuntimeModule")
    if "runtimeModuleHost.Register(new ModeDRuntimeModule());" not in registration_text:
        return fail("ArchitectureStructureGuard: runtime module registration missing ModeDRuntimeModule")

    scene_gate = Path("Utilities/SceneRuntimeGate.cs").read_text(encoding="utf-8", errors="ignore")
    for required in [
        'SceneNameEquals(sceneName, "MainMenu")',
        "SceneLoader.IsSceneLoading",
        "MultiSceneCore.Instance",
        "Base_SceneV2",
        "Level_HiddenWarehouse_CellarUnderGround",
    ]:
        if required not in scene_gate:
            return fail("ArchitectureStructureGuard: SceneRuntimeGate missing behavior-preserving token: " + required)

    print("ArchitectureStructureGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
