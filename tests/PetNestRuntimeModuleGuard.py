#!/usr/bin/env python3
"""
PetNestRuntimeModuleGuard — 遗种巢运行时模块守卫（实施计划 步骤 3）。

不变式：
- 全系统只有一个 PetNestRuntimeModule 实例：只在
  Common/Lifecycle/BossRushRuntimeModuleRegistration.cs 里 new 一次，
  存字段后把**同一个引用**注册给 host（Mode G 的实例分裂是反例）；
- 只读门面 ModBehaviour.PetNestRuntime 存在；
- 其余任何文件不得出现 `new PetNestRuntimeModule(`；
- 只复用 host 已有的六个回调，不新增全局 hook（模块内不得 += 到 SceneManager 等）；
- **petNestEnabled = false 时全系统 dormant**：bootstrap 被 IsEnabled 挡住，
  未 bootstrap 时 OnUpdate O(1) 早返；开关是运行时可变的，因此 bootstrap 必须幂等
  且在多个回调里重试，而不是只在 Awake 判一次；
- 领域服务写操作一律走 Commit()，玩法层不得直接碰 PetNestPersistence。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    PETNEST_DIR,
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestRuntimeModuleGuard"
REGISTRATION = os.path.join("Common", "Lifecycle", "BossRushRuntimeModuleRegistration.cs")


def check_registration(errors):
    text = read_text(repo_path(REGISTRATION))
    if text is None:
        errors.append("[File] 缺少 " + REGISTRATION)
        return
    code = strip_cs_comments(text)

    if code.count("new PetNestRuntimeModule()") != 1:
        errors.append("[单实例] 注册文件里必须且只能 new PetNestRuntimeModule() 一次")
    if not re.search(r"petNestRuntime = new PetNestRuntimeModule\(\);\s*\n\s*runtimeModuleHost\.Register\(petNestRuntime\);", code):
        errors.append("[单实例] 必须先存字段再把同一个引用注册给 host（禁止 Register(new ...)）")
    if not re.search(r"private PetNestRuntimeModule petNestRuntime;", code):
        errors.append("[单实例] 缺少 ModBehaviour 持有的字段 petNestRuntime")
    if not re.search(r"internal PetNestRuntimeModule PetNestRuntime \{ get \{ return petNestRuntime; \} \}", code):
        errors.append("[门面] 缺少只读门面 ModBehaviour.PetNestRuntime")


def check_no_second_new(errors):
    for root, _dirs, files in os.walk(REPO_ROOT):
        rel_root = os.path.relpath(root, REPO_ROOT)
        parts = rel_root.split(os.sep)
        if any(p in ("Build", ".git", ".codex_tmp", "tests", "docs", "鸭科夫源码", "wiki-site") for p in parts):
            continue
        for name in files:
            if not name.endswith(".cs"):
                continue
            rel = os.path.relpath(os.path.join(root, name), REPO_ROOT)
            if rel.replace("/", os.sep) == REGISTRATION:
                continue
            text = read_text(os.path.join(root, name))
            if text is None:
                continue
            if "new PetNestRuntimeModule(" in strip_cs_comments(text):
                errors.append("[单实例] " + rel + " 不得二次 new PetNestRuntimeModule")


def check_module(errors):
    text = read_petnest("PetNestRuntimeModule.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
        return
    code = strip_cs_comments(text)

    if "BossRushRuntimeModuleBase" not in code:
        errors.append("[宿主] 必须继承 BossRushRuntimeModuleBase，复用 host 六回调")

    # 开关只经 owner getter
    if "_owner.IsPetNestConfiguredEnabled()" not in code:
        errors.append("[开关] 必须只经 owner.IsPetNestConfiguredEnabled() 读取入口开关")
    if re.search(r"petNestEnabled", code):
        errors.append("[开关] 模块不得直接触碰配置字段 petNestEnabled")

    # dormant：bootstrap 被 IsEnabled 挡住
    boot = re.search(r"internal void EnsureBootstrapped\(\)[\s\S]{0,700}?\n        \}", code)
    if boot is None:
        errors.append("[dormant] 缺少幂等入口 EnsureBootstrapped()")
    else:
        body = boot.group(0)
        if "if (_bootstrapped) return;" not in body:
            errors.append("[dormant] EnsureBootstrapped 缺少幂等早返")
        if "if (!IsEnabled) return;" not in body:
            errors.append("[dormant] 关闭时必须直接返回，不订阅、不建目录")
        if "PetNestSaveCoordinator.EnsureSubscribed()" not in body:
            errors.append("[dormant] 订阅只能发生在 bootstrap 内")
        if "PetNestLineageCatalog.EnsureBuilt(_owner)" not in body:
            errors.append("[dormant] 血脉目录只能在 bootstrap 内构建")

    # 订阅不得出现在 bootstrap 之外
    if code.count("PetNestSaveCoordinator.EnsureSubscribed()") != 1:
        errors.append("[dormant] EnsureSubscribed 只能在 bootstrap 内出现一次")

    # 血脉目录必须能重建：目录在 bootstrap（ModBehaviour.Start）时构建，而 enemyPresets
    # 要等玩家第一次进竞技场才由 InitializeEnemyPresets 填充。没有重建入口时目录里
    # 一个官方 Boss 都没有，且 _built 置位后永不重建 —— 全谱系不掉蛋/不可孵/不可出战。
    refresh = re.search(
        r"internal void NotifyEnemyPresetsRefreshed\(\)[\s\S]{0,600}?\n        \}", code)
    if refresh is None:
        errors.append("[目录时序] 缺少血脉目录重建入口 NotifyEnemyPresetsRefreshed()")
    else:
        refresh_body = refresh.group(0)
        if "if (!_bootstrapped) return;" not in refresh_body:
            errors.append("[目录时序] 重建入口未在未 bootstrap 时早返")
        if "PetNestLineageCatalog.Invalidate();" not in refresh_body:
            errors.append("[目录时序] 重建入口必须先作废旧目录")
        if "PetNestLineageCatalog.EnsureBuilt(_owner)" not in refresh_body:
            errors.append("[目录时序] 重建入口必须紧接着重建目录")

    waves = read_text(repo_path(os.path.join("WavesArena", "WavesArena.cs")))
    if waves is None:
        errors.append("[File] 缺少 WavesArena/WavesArena.cs")
    elif not re.search(
            r"_enemyPresetsInitialized = true;[\s\S]{0,600}?"
            r"PetNestRuntime\.NotifyEnemyPresetsRefreshed\(\);", waves):
        errors.append("[目录时序] InitializeEnemyPresets 填充完成后未通知重建血脉目录")

    boss_filter = read_text(repo_path(os.path.join("BossFilter", "BossFilter.cs")))
    if boss_filter is None:
        errors.append("[File] 缺少 BossFilter/BossFilter.cs")
    elif not re.search(
            r"private void InvalidateFilteredPresetsCache\(\)[\s\S]{0,600}?"
            r"PetNestRuntime\.NotifyEnemyPresetsRefreshed\(\);", boss_filter):
        errors.append("[目录时序] Boss 池过滤变化后未通知重建血脉目录")

    # 光有「填充后重建」还不够：InitializeEnemyPresets 的调用点全在进竞技场路径与
    # 调试面板，基地启动一处都不触发。每次重启会话后、进第一次竞技场之前，
    # 目录里一个官方血脉都没有（蛋孵出 lineage_unknown / 巢页裸 key / 账本缺行）。
    # 因此基地侧必须主动预热一次（CR-2026-08-29-015）。
    prime = re.search(r"private void EnsureOfficialLineagesPrimed\(\)[\s\S]{0,800}?\n        \}", code)
    if prime is None:
        errors.append("[目录时序] 缺少基地侧预设池预热入口 EnsureOfficialLineagesPrimed()")
    else:
        prime_body = prime.group(0)
        # 4.12：没开遗种巢开关的玩家不得平白多做一次全量预设扫描
        if "if (!_bootstrapped || _owner == null) return;" not in prime_body:
            errors.append("[性能门控] 预热必须被 bootstrap（= 开关开启）挡住，"
                          "未开开关的玩家不得付出全量预设扫描成本")
        if "_owner.EnsureEnemyPresetsReadyForPetNest()" not in prime_body:
            errors.append("[目录时序] 预热必须经 owner 侧的幂等入口，不得自行重扫预设")
    if not re.search(r"if \(IsBaseScene\(\)\)\s*\{\s*EnsureOfficialLineagesPrimed\(\);", code):
        errors.append("[目录时序] 预热必须在回基地分支的最前面，"
                      "晚于任何读血脉目录的一步就等于没修")

    if waves is not None and not re.search(
            r"internal bool EnsureEnemyPresetsReadyForPetNest\(\)[\s\S]{0,900}?"
            r"InitializeEnemyPresets\(\);", waves):
        errors.append("[目录时序] WavesArena 缺少幂等的 EnsureEnemyPresetsReadyForPetNest 入口")

    # OnUpdate 未 bootstrap 时零成本早返；开关运行时打开要当帧复活
    # （EnsureBootstrapped 在开关关闭时自身就是 O(1) 早返，不破坏 dormant 零开销）
    if not re.search(
            r"public override void OnUpdate\(float deltaTime, float unscaledDeltaTime\)\s*\{\s*"
            r"if \(!_bootstrapped\)\s*\{[\s\S]{0,320}?EnsureBootstrapped\(\);\s*"
            r"if \(!_bootstrapped\) return;\s*\}", code):
        errors.append("[性能] OnUpdate 必须在未 bootstrap 时 O(1) 早返，且开关打开当帧复活")

    # 运行时关开关要能回到 dormant
    if "ShutdownIfEnabledTurnedOff" not in code:
        errors.append("[dormant] 缺少运行时关开关后的退订路径")
    # 关开关会把 OnSetFile 一起退订，之后玩家切档没人清缓存；
    # 再打开开关时缓存里还是上一个档的数据，一写就把旧档覆盖到新档上
    shutdown = re.search(
        r"private void ShutdownIfEnabledTurnedOff\(\)[\s\S]{0,1200}?\n        \}", code)
    if shutdown is None:
        errors.append("[dormant] 无法解析 ShutdownIfEnabledTurnedOff")
    elif "PetNestPersistenceAccess.ResetCachesForSlotReload()" not in shutdown.group(0):
        errors.append("[跨档] 关开关必须清 store 缓存，否则切档再开会把旧档数据覆盖到新档")

    # 不新增全局 hook
    for forbidden in ["SceneManager.sceneLoaded", "Health.OnDead +=", "Health.OnHurt +=",
                      "HarmonyPatch", "new Harmony("]:
        if forbidden in code:
            errors.append("[最小化] 模块不得新增全局 hook / 补丁: " + forbidden)


def check_service(errors):
    text = read_petnest("PetNestService.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestService.cs")
        return
    code = strip_cs_comments(text)

    if not re.search(r"internal static bool Commit\(out string failureReasonId\)", code):
        errors.append("[落档] 缺少统一落档入口 Commit(out string)")
    if "PetNestPersistence.Nest.Store(nest)" not in code:
        errors.append("[落档] Commit 必须经 store 入队")
    if "PetNestSaveCoordinator.RequestFlush();" not in code:
        errors.append("[落档] Commit 必须请求协调器落盘（best-effort）")
    # 成败以入队为准：flush 失败时若返回 false，调用方会回滚内存，
    # 而已入队的 pending 仍会被官方采集写下去，两边永久分叉
    commit = re.search(
        r"internal static bool Commit\(out string failureReasonId\)[\s\S]{0,1400}?\n        \}", code)
    if commit is not None and "flush_deferred_is_saving" in commit.group(0):
        errors.append("[落档] Commit 的成败必须以 Store 入队为准，不得因 flush 失败返回 false")

    # 内存回滚：Store 失败时什么都没入队，内存必须一并回滚，否则内存与磁盘分叉
    for fn, marker in [
        ("TryAddPet", "nest.pets.Remove(pet);"),
        ("TryRemovePet", "nest.deployedPetId = previousDeployed;"),
        ("TrySetDeployedPet", "nest.deployedPetId = previousDeployedId;"),
        ("ClearDeployedPet", "nest.deployedPetId = previousDeployedId;"),
        ("TrySpendSouls", "target.souls = previousSouls;"),
    ]:
        block = re.search(
            r"internal static bool " + fn + r"\([\s\S]{0,2600}?\n        \}", code)
        if block is None:
            errors.append("[回滚] 无法解析 " + fn)
        elif marker not in block.group(0):
            errors.append("[回滚] " + fn + " 在 Commit 失败时必须回滚内存: " + marker)

    # 单席契约
    if not re.search(r"internal static bool TrySetDeployedPet\(string petId, out string failureReasonId\)", code):
        errors.append("[单席] 缺少 TrySetDeployedPet 入口")
    if 'failureReasonId = "pet_locked_by_expedition";' not in code:
        errors.append("[远征锁定] 远征中的崽必须拒绝上席与移除")
    if 'failureReasonId = "nest_full";' not in code:
        errors.append("[容量] 超容必须显式失败，不得静默丢弃玩家的蛋")

    # 玩法层不得直接碰持久化：Service 之外的 PetNest 文件不得引用 PetNestPersistence
    allowed = {"PetNestService.cs", "PetNestPersistence.cs", "PetNestPersistenceCodec.cs",
               "PetNestSaveCoordinator.cs"}
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs") or name in allowed:
            continue
        other = read_text(os.path.join(PETNEST_DIR, name))
        if other is None:
            continue
        if "PetNestPersistence." in strip_cs_comments(other):
            errors.append("[分层] " + name + " 不得直接访问 PetNestPersistence，必须走 Service")


def main():
    errors = []
    check_registration(errors)
    check_no_second_new(errors)
    check_module(errors)
    check_service(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
