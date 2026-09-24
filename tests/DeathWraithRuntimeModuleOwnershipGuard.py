from pathlib import Path
from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
LEAF_FILES = [
    "Integration/DeathWraith/DeathWraithCombatLoadout.cs",
    "Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs",
    "Integration/DeathWraith/DeathWraithOriginalDeadBodyBridge.cs",
    "Integration/DeathWraith/DeathWraithRecording.cs",
    "Integration/DeathWraith/DeathWraithSpawnFlow.cs",
    "Integration/DeathWraith/DeathWraithSystem.cs",
]
BRIDGE = "Integration/DeathWraith/DeathWraithRuntimeModuleHostBridge.cs"


def fail(message: str) -> int:
    print("DeathWraithRuntimeModuleOwnershipGuard: FAIL - " + message)
    return 1


def read_source(relative: str) -> str:
    return clean_source((ROOT / relative).read_text(encoding="utf-8-sig"))


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        return ""
    brace = source.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    return ""


def main() -> int:
    leaf_sources = {relative: read_source(relative) for relative in LEAF_FILES}
    for relative, source in leaf_sources.items():
        if "internal sealed partial class DeathWraithRuntimeModule" not in source:
            return fail(relative + " is not owned by DeathWraithRuntimeModule")
        if "partial class ModBehaviour" in source:
            return fail(relative + " still declares a ModBehaviour partial")

    system = leaf_sources["Integration/DeathWraith/DeathWraithSystem.cs"]
    for token in [
        "internal sealed partial class DeathWraithRuntimeModule : BossRushRuntimeModuleBase",
        "public override void OnAwake(ModBehaviour owner)",
        "public override void OnDestroy()",
        "owner.AttachDeathWraithRuntimeModule_DeathWraith(this);",
        "UnsubscribeDeathWraithEvents_DeathWraith();",
        "currentOwner.DetachDeathWraithRuntimeModule_DeathWraith(this);",
    ]:
        if token not in system:
            return fail("DeathWraithSystem missing runtime lifecycle token -> " + token)

    refresh = method_body(system, "internal void RefreshDeathWraithEventBindings_DeathWraith()")
    if not refresh:
        return fail("missing RefreshDeathWraithEventBindings_DeathWraith")
    expected_order = [
        "UnsubscribeDeathWraithEvents_DeathWraith();",
        "Health.OnHurt += currentOwner.PrimeDeathWraithData_DeathWraith;",
        "Health.OnDead += currentOwner.RecordDeathWraithData_DeathWraith;",
        "Health.OnDead += currentOwner.OnWraithDied_DeathWraith;",
        "SavesSystem.OnCollectSaveData += currentOwner.OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;",
        "SavesSystem.OnCollectSaveData += currentOwner.FlushDeathWraithListIfDirty_DeathWraith;",
    ]
    positions = [refresh.find(token) for token in expected_order]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        return fail("event unbind and bind order changed in RefreshDeathWraithEventBindings_DeathWraith")

    bridge = read_source(BRIDGE)
    if "private DeathWraithRuntimeModule deathWraithRuntimeModule;" not in bridge:
        return fail("host bridge must hold the single runtime module reference")
    if bridge.count("DeathWraithRuntimeModule deathWraithRuntimeModule;") != 1:
        return fail("host bridge contains more than one DeathWraith module field")
    for token in [
        "internal void PrimeDeathWraithData_DeathWraith(Health hurtHealth, DamageInfo damageInfo)",
        "internal void RecordDeathWraithData_DeathWraith(Health deadHealth, DamageInfo damageInfo)",
        "internal void RecordManualDeathWraithData_DeathWraith(CharacterMainControl main, DamageInfo damageInfo, string source)",
        "internal void OnWraithDied_DeathWraith(Health deadHealth, DamageInfo damageInfo)",
        "internal void UpdateDeferredDeathWraithSave_DeathWraith()",
        "internal void NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith(DeadBodyManager.DeathInfo info)",
        "internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(DeadBodyManager.DeathInfo info)",
        "internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info)",
    ]:
        if token not in bridge:
            return fail("host compatibility bridge missing entrypoint -> " + token)

    compile_text = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    if "Integration\\DeathWraith\\DeathWraithRuntimeModuleHostBridge.cs" not in compile_text:
        return fail("compile_official.bat does not include " + BRIDGE)

    registration = read_source("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
    if registration.count("deathWraithRuntimeModule = new DeathWraithRuntimeModule();") != 1:
        return fail("runtime registration must create exactly one DeathWraith module instance")
    if registration.count("runtimeModuleHost.Register(deathWraithRuntimeModule);") != 1:
        return fail("runtime registration must register the same DeathWraith module instance")

    consumers = {
        "Utilities/PlayerLifecycleRuntimeHooks.cs": [
            "Health.OnHurt += PrimeDeathWraithData_DeathWraith;",
            "Health.OnDead += RecordDeathWraithData_DeathWraith;",
            "Health.OnHurt -= PrimeDeathWraithData_DeathWraith;",
            "Health.OnDead -= RecordDeathWraithData_DeathWraith;",
        ],
        "Integration/BossRushIntegration_StartAndScene.cs": [
            "RefreshDeathWraithEventBindings_DeathWraith();",
            "SavesSystem.OnSetFile += OnSetFile_DeathWraith;",
            "FlushDeathWraithListIfDirty_DeathWraith();",
            "ClearDeathWraithState_DeathWraith();",
        ],
        "Utilities/AlwaysOnRuntimeHooks.cs": ["UpdateDeferredDeathWraithSave_DeathWraith();"],
        "ModeF/ModeFPhases.cs": ["RecordManualDeathWraithData_DeathWraith("],
        "WavesArena/WavesArenaEnemyMaintenance.cs": ["IsDeathWraithCharacter_DeathWraith(c)"],
    }
    for relative, tokens in consumers.items():
        source = read_source(relative)
        for token in tokens:
            if token not in source:
                return fail(relative + " no longer reaches the compatibility bridge -> " + token)

    print("DeathWraithRuntimeModuleOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
