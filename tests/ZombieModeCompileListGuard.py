from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")

REQUIRED_FILES = [
    "ZombieMode\\ZombieModeModels.cs",
    "ZombieMode\\ZombieModeTuning.cs",
    "ZombieMode\\ZombieModeRuntimeModule.cs",
    "ZombieMode\\ZombieModeRuntimeModule_HostLifecycle.cs",
    "ZombieMode\\ZombieModeEntryHostBridge.cs",
    "ZombieMode\\ZombieModeMapSelectionHelper.cs",
    "ZombieMode\\ZombieModeMapIsolation.cs",
    "ZombieMode\\ZombieModeSpawner.cs",
    "ZombieMode\\ZombieModeCombatHostBridge.cs",
    "ZombieMode\\ZombieModeEnemyRuntime.cs",
    "ZombieMode\\ZombieModeRewards.cs",
    "ZombieMode\\ZombieModeRewardHostBridge.cs",
    "ZombieMode\\ZombieModeRewardEffectsAndNpc.cs",
    "ZombieMode\\ZombieModeRewardItemGrants.cs",
    "ZombieMode\\ZombieModeRewardNpcServices.cs",
    "ZombieMode\\ZombieModeRewardOptionCore.cs",
    "ZombieMode\\ZombieModeRewardProjectileSpread.cs",
    "ZombieMode\\ZombieModeRewardRuntimeModifiers.cs",
    "ZombieMode\\ZombieModeRewardTriggerEffects.cs",
    "ZombieMode\\ZombiePurificationPointController.cs",
    "ZombieMode\\ZombieModeSafeZoneController.cs",
    "ZombieMode\\ZombieModeExtractionController.cs",
    "ZombieMode\\ZombieModeHudController.cs",
    "ZombieMode\\ZombieModeCashInvestmentView.cs",
    # 2026-09-23 审美审查新增：表现层与从宿主 partial 拆出的视图（不登记就不会编进 DLL，也不报错）。
    "ZombieMode\\ZombieModeZoneVisuals.cs",
    "ZombieMode\\ZombieModeUiWidgets.cs",
    "ZombieMode\\ZombieModeRewardSelectionView.cs",
    "ZombieMode\\ZombieModeTemporaryNpcServiceView.cs",
    "Integration\\Items\\ZombieTideInvitationConfig.cs",
    "Integration\\Items\\ZombieTideInvitationUsage.cs",
    "Integration\\Items\\ZombieTideBeaconConfig.cs",
    "Integration\\Items\\ZombieTideBeaconUsage.cs",
    "Integration\\Items\\PortableSafeZoneDeviceConfig.cs",
    "Integration\\Items\\PortableSafeZoneDeviceUsage.cs",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = COMPILE.read_text(encoding="utf-8")
    missing = [path for path in REQUIRED_FILES if path not in text]
    if missing:
        return fail("ZombieModeCompileListGuard: missing compile entries: " + ", ".join(missing))

    ordered = [
        "ZombieModeModels.cs", "ZombieModeTuning.cs", "ZombieModeRuntimeModule.cs",
        "ZombieModeEntryHostBridge.cs", "ZombieModeCombatHostBridge.cs",
        "ZombieModeRewardHostBridge.cs", "ZombieModeRuntimeModule_HostLifecycle.cs",
    ]
    positions = [text.find("ZombieMode\\" + name) for name in ordered]
    if min(positions) < 0 or positions != sorted(positions) or len(set(positions)) != len(ordered):
        return fail("ZombieModeCompileListGuard: state, runtime, host bridges and lifecycle compile order changed")
    for name in ordered:
        if text.count("ZombieMode\\" + name) != 1:
            return fail("ZombieModeCompileListGuard: duplicate compile entry -> " + name)

    print("ZombieModeCompileListGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
