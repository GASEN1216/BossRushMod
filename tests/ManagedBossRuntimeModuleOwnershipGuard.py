#!/usr/bin/env python3
"""守卫龙王、龙裔遗族、幽灵女巫三个托管 Boss 的 runtime ownership 接线。"""
import os
import re
import sys

from cs_source_util import clean_source


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COMPILE_LIST = os.path.join(REPO_ROOT, "compile_official.bat")
REGISTRATION = os.path.join(REPO_ROOT, "Common", "Lifecycle", "BossRushRuntimeModuleRegistration.cs")

BOSSES = (
    {
        "name": "DragonKing",
        "module": "DragonKingRuntimeModule",
        "field": "dragonKingRuntimeModule",
        "files": (
            "Integration/DragonKing/DragonKingBoss.cs",
            "Integration/DragonKing/DragonKingBoss_ModeGAdapter.cs",
        ),
        "bridge": "Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs",
        "cache_reset": "ResetDragonKingRuntimeModuleStaticCaches",
        "spawn": r"public async UniTask<CharacterMainControl> SpawnDragonKing\s*\(\s*Vector3 position,\s*bool notifyBossRushOnFailure = true,\s*bool deferActivationUntilNextFrame = false,\s*bool isNonWaveSpawn = false,\s*Func<bool> isActiveCheck = null\)",
        "cleanup": "CleanupTrackedDragonKingsOnArenaExit();",
    },
    {
        "name": "DragonDescendant",
        "module": "DragonDescendantRuntimeModule",
        "field": "dragonDescendantRuntimeModule",
        "files": (
            "Integration/DragonDescendant/DragonDescendantBoss.cs",
            "Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs",
            "Integration/DragonDescendant/DragonDescendantBoss_ModeGAdapter.cs",
        ),
        "bridge": "Integration/DragonDescendant/DragonDescendantRuntimeModuleHostBridge.cs",
        "cache_reset": "ResetDragonDescendantRuntimeModuleStaticCaches",
        "spawn": r"public async UniTask<CharacterMainControl> SpawnDragonDescendant\s*\(\s*Vector3 position,\s*bool isChildProtectionSummon = false,\s*bool notifyBossRushOnFailure = true,\s*bool deferActivationUntilNextFrame = false,\s*bool isNonWaveSpawn = false,\s*Func<bool> isActiveCheck = null\)",
        "cleanup": "CleanupDragonDescendant();",
    },
    {
        "name": "PhantomWitch",
        "module": "PhantomWitchRuntimeModule",
        "field": "phantomWitchRuntimeModule",
        "files": (
            "Integration/PhantomWitch/PhantomWitchBoss.cs",
            "Integration/PhantomWitch/PhantomWitchBoss_ModeGAdapter.cs",
        ),
        "bridge": "Integration/PhantomWitch/PhantomWitchRuntimeModuleHostBridge.cs",
        "cache_reset": "ResetPhantomWitchRuntimeModuleStaticCaches",
        "spawn": r"public async UniTask<CharacterMainControl> SpawnPhantomWitch\s*\(\s*Vector3 position,\s*bool notifyBossRushOnFailure = true,\s*bool deferActivationUntilNextFrame = false,\s*PhantomWitchDeathPresentation deathPresentation = PhantomWitchDeathPresentation.Standard,\s*float extraModelScale = 1f,\s*bool isNonWaveSpawn = false,\s*Func<bool> isActiveCheck = null\)",
        "cleanup": "CleanupPhantomWitchTrackedStateOnArenaExit();",
    },
)


def read_source(relative_path, errors):
    path = os.path.join(REPO_ROOT, *relative_path.split("/"))
    if not os.path.isfile(path):
        errors.append("[MissingFile] " + relative_path)
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as source_file:
        return clean_source(source_file.read())


def main():
    errors = []
    compile_list = read_source("compile_official.bat", errors)
    registration = read_source("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs", errors)

    for boss in BOSSES:
        module = boss["module"]
        files = [(path, read_source(path, errors)) for path in boss["files"]]
        bridge = read_source(boss["bridge"], errors)

        for path, source in files:
            if source and not re.search(
                    r"internal sealed partial class\s+" + re.escape(module)
                    + r"(?:\s*:\s*BossRushRuntimeModuleBase)?", source):
                errors.append("[ModuleOwnership] {} 未归属 {}".format(path, module))

        if bridge:
            if not re.search(
                    r"internal sealed partial class\s+" + re.escape(module)
                    + r"[\s\S]*?public override string ModuleName\s*\{\s*get\s*\{\s*return\s*\""
                    + re.escape(boss["name"]) + r"\";\s*\}\s*\}", bridge):
                errors.append("[ModuleName] {} 未声明稳定模块名".format(boss["bridge"]))
            if "public override void OnAwake(ModBehaviour owner)" not in bridge \
                    or "Attach{}RuntimeModule(this)".format(boss["name"]) not in bridge:
                errors.append("[Attach] {} 未在 OnAwake 接入宿主".format(boss["bridge"]))
            if "public override void OnDestroy()" not in bridge or boss["cleanup"] not in bridge:
                errors.append("[LifecycleCleanup] {} 未在 OnDestroy 清理运行态".format(boss["bridge"]))
            if "{}.{}();".format(module, boss["cache_reset"]) not in bridge:
                errors.append("[ModuleStaticCacheCleanup] {} 未在 OnDestroy 清理本地静态缓存".format(boss["bridge"]))
            if not re.search(boss["spawn"], bridge):
                errors.append("[PublicSpawnCompatibility] {} 旧公开 Spawn 签名缺失".format(boss["bridge"]))

            compile_entry = boss["bridge"].replace("/", "\\")
            if compile_list.count(compile_entry) != 1:
                errors.append("[CompileRegistration] {} 编译清单登记次数不是 1".format(compile_entry))

        compatibility_methods = {
            "DragonKing": ("FindDragonKingBasePreset",),
            "DragonDescendant": ("FindQuestionMarkPreset", "FindFallbackPreset"),
            "PhantomWitch": ("FindPhantomWitchBasePreset", "CleanupFailedPhantomWitchSpawn"),
        }[boss["name"]]
        for method in compatibility_methods:
            if not re.search(r"\b" + re.escape(method) + r"\s*\(", bridge):
                errors.append("[HostCompatibility] {} 未保留宿主入口 {}".format(boss["bridge"], method))

        field = boss["field"]
        new_expression = "{} = new {}();".format(field, module)
        register_expression = "runtimeModuleHost.Register({});".format(field)
        if registration.count(new_expression) != 1 or registration.count(register_expression) != 1:
            errors.append("[UniqueRegistration] {} 必须先创建并只注册一次".format(module))

    # DragonDescendantAbilities 和旧外部调用点仍使用公开嵌套类型。
    dd_bridge = read_source("Integration/DragonDescendant/DragonDescendantRuntimeModuleHostBridge.cs", errors)
    dd_abilities = read_source("Integration/DragonDescendant/DragonDescendantAbilities.cs", errors)
    if dd_bridge and not re.search(r"public class OriginalWeaponData\s*\{", dd_bridge):
        errors.append("[NestedTypeCompatibility] ModBehaviour.OriginalWeaponData 公共嵌套类型丢失")
    if dd_abilities and "ModBehaviour.OriginalWeaponData" not in dd_abilities:
        errors.append("[NestedTypeConsumers] DragonDescendantAbilities 未保留旧类型引用")

    # 事件委托必须仍指向配对退订的同一方法；实例死亡回调保留原捕获对象。
    king = read_source("Integration/DragonKing/DragonKingBoss.cs", errors)
    descendant = read_source("Integration/DragonDescendant/DragonDescendantBoss.cs", errors)
    descendant_lifecycle = read_source(
        "Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs", errors)
    witch = read_source("Integration/PhantomWitch/PhantomWitchBoss.cs", errors)
    event_pairs = (
        ("DragonKing", king, "Health.OnHurt += owner.OnDragonKingBossHurt;",
         "Health.OnHurt -= owner.OnDragonKingBossHurt;"),
        ("DragonDescendant", descendant_lifecycle, "Health.OnHurt += OnDragonDescendantHurt;",
         "Health.OnHurt -= OnDragonDescendantHurt;"),
    )
    for name, source, subscribe, unsubscribe in event_pairs:
        if subscribe not in source or unsubscribe not in source:
            errors.append("[EventPair] {} 的静态伤害订阅/退订委托不匹配".format(name))
    if king and ("OnDeadEvent.AddListener(deathHandler);" not in king
                 or "OnDeadEvent.RemoveListener(deathHandler);" not in king):
        errors.append("[DeathCallback] DragonKing 的实例死亡回调未保留同一委托清理")
    if descendant and "OnDragonDescendantDeath(capturedDescendant, info)" not in descendant:
        errors.append("[DeathCallback] DragonDescendant 未保留实例捕获死亡回调")
    if witch and "OnPhantomWitchDeath(capturedChar, dmgInfo)" not in witch:
        errors.append("[DeathCallback] PhantomWitch 未保留实例捕获死亡回调")

    # 共享托管生成器必须只有真实 host 实现，runtime bridge 只能转交，不能自递归。
    dd_adapter = read_source("Integration/DragonDescendant/DragonDescendantBoss_ModeGAdapter.cs", errors)
    if dd_adapter:
        helper_signatures = (
            "internal static bool IsManagedOwnerValid(ManagedBossSpawnContext ctx)",
            "internal async UniTask<CharacterMainControl> CreateModeGManagedCharacterAsync(",
            "internal void BeginActivateModeGManagedCharacter(CharacterMainControl character)",
            "internal void CompleteActivateModeGManagedCharacter(CharacterMainControl character)",
            "internal void CleanupModeGManagedCharacter(CharacterMainControl character,",
        )
        for signature in helper_signatures:
            if signature not in dd_adapter:
                errors.append("[SharedModeGHelper] DragonDescendant adapter 缺少 {}".format(signature))
        if "stagingBossRegistered = state.RegisterStagingBoss(character.Health, character);" not in dd_adapter \
                or "character.Health.SetInvincible(true);" not in dd_adapter \
                or "character.gameObject.SetActive(false);" not in dd_adapter:
            errors.append("[ModeGFactorySequence] shared factory 登记/冻结序列缺失")

    king_bridge = read_source("Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs", errors)
    if king_bridge and ("owner.FindQuestionMarkPreset()" not in king_bridge
                        or "owner.FindFallbackPreset()" not in king_bridge):
        errors.append("[SharedPresetLookup] DragonKing 模块未经 owner 调用共享预设查找入口")

    if errors:
        print("ManagedBossRuntimeModuleOwnershipGuard: FAIL ({} errors)".format(len(errors)))
        for error in errors:
            print("  - " + error)
        return 1

    print("ManagedBossRuntimeModuleOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
