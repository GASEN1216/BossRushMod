#!/usr/bin/env python3
"""
PetNestSaveCoordinatorGuard — 遗种巢落盘协调器守卫（实施计划 步骤 2）。

不变式：
- PetNestSaveCoordinator 是遗种巢**唯一**的物理落盘入口：它经共享引擎
  Common/Lifecycle/BossRushSaveCoordinatorEngine.cs 落盘（引擎内恰好一处 SaveFile），
  整个 PetNest/ 目录零处 SavesSystem.SaveFile 直调；
- 每批至多一次 SaveFile；
- SavesSystem.IsSaving 时不强写，只登记 deferred；
- deferred 重试有预算上限，超预算不静默丢弃 pending；
- 切档/删档有 NotifySlotChanged 清 deferred 状态；
- 宿主销毁有 TryFlushOnHostDestroy 且 no-throw；
- 遗种巢不设基地闸（实物资产屏障要求任何场景立即试写），实物屏障绕过每帧闸；
- 清理 owner 唯一化：宿主销毁经 runtimeModuleHost.OnDestroy() 到达
  PetNestRuntimeModule.OnDestroy，落盘与静态复位接在模块里。
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

GUARD = "PetNestSaveCoordinatorGuard"
COORDINATOR_FILE = "PetNestSaveCoordinator.cs"
ENGINE_FILE = os.path.join("Common", "Lifecycle", "BossRushSaveCoordinatorEngine.cs")


def main():
    errors = []

    text = read_petnest(COORDINATOR_FILE)
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/" + COORDINATOR_FILE])
    code = strip_cs_comments(text)

    engine_text = read_text(repo_path(ENGINE_FILE))
    if engine_text is None:
        return report(GUARD, ["[File] 缺少 " + ENGINE_FILE])
    engine = strip_cs_comments(engine_text)

    # 1. 唯一写点：门面经引擎落盘；引擎恰好一次 SaveFile；整个 PetNest/ 零处直调
    if not re.search(r"new BossRushSaveCoordinatorEngine\(new Source\(\), false\)", code):
        errors.append("[唯一写点] 协调器必须持有共享引擎，且 deferOutsideBaseScene=false（实物资产屏障不设基地闸）")
    if "IBossRushSaveBatchSource" not in code:
        errors.append("[唯一写点] 协调器必须实现 IBossRushSaveBatchSource 数据源")
    engine_calls = len(re.findall(r"SavesSystem\.SaveFile\(", engine))
    if engine_calls != 1:
        errors.append("[唯一写点] 引擎里 SaveFile 必须有且只有一次调用，当前有 " + str(engine_calls) + " 处")
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs"):
            continue
        other = read_text(os.path.join(PETNEST_DIR, name))
        if other is None:
            continue
        if "SavesSystem.SaveFile" in strip_cs_comments(other):
            errors.append("[唯一写点] " + name + " 不得直接调用 SavesSystem.SaveFile")

    # 2. 实物资产屏障：RequestAssetFlush 绕过每帧闸立即写；CollectPendingAssets 先于 Bundle.FlushPending
    if not re.search(r"internal static bool RequestAssetFlush\(out string error\)[\s\S]{0,200}?_engine\.RequestFlush\(out error, true\)", code):
        errors.append("[资产屏障] RequestAssetFlush 必须经引擎的 bypassGates=true 路径")
    if not re.search(r"CollectPendingAssets\(out error\)[\s\S]*?Bundle\.FlushPending\(\)", code):
        errors.append("[资产屏障] 数据源必须先采集实物快照再交出 Bundle pending")

    # 3. IsSaving 时不强写
    if not re.search(r"if \(SavesSystem\.IsSaving\)", engine):
        errors.append("[并发] 引擎必须在 IsSaving 时改走 deferred，不强写")
    if not re.search(r'"flush_deferred_is_saving"', engine):
        errors.append("[并发] deferred 必须给出可诊断的原因串")

    # 4. deferred 预算
    if not re.search(r"private const int MaxDeferredRetries", engine):
        errors.append("[预算] deferred 重试必须有上限常量")
    if not re.search(r'_lastError = "flush_deferred_budget_exhausted";', engine):
        errors.append("[预算] 超预算必须报告失败，不得静默丢弃 pending")

    # 5. 切档 / 宿主销毁入口
    if not re.search(r"internal static void NotifySlotChanged\(\)", code):
        errors.append("[生命周期] 缺少 NotifySlotChanged() 切档清理入口")
    if not re.search(r"internal static bool TryFlushOnHostDestroy\(\)", code):
        errors.append("[生命周期] 缺少 TryFlushOnHostDestroy() 宿主销毁入口")
    destroy = re.search(r"internal bool TryFlushOnHostDestroy\(\)[\s\S]{0,400}?\n        \}", engine)
    if destroy is None or "catch (Exception)" not in destroy.group(0):
        errors.append("[生命周期] 引擎的 TryFlushOnHostDestroy 必须 no-throw")

    # 6. Tick 未 deferred 时零成本早返
    if not re.search(r"if \(!_deferredFlushPending\) return;", engine):
        errors.append("[性能] Tick 未 deferred 时必须 O(1) 早返")

    # 7. 宿主销毁实际接线 —— 清理 owner 唯一化：
    #    宿主 OnDestroy 只经 runtimeModuleHost.OnDestroy() 到达 PetNestRuntimeModule.OnDestroy，
    #    落盘与静态复位必须接在模块里，且 ModBehaviour.OnDestroy 不得再内联一份
    #    （曾经两处各写一份、宿主先清，模块自己的落盘随即空转）。
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
    else:
        mcode = strip_cs_comments(module)
        destroy = re.search(r"public override void OnDestroy\(\)[\s\S]*?\n        \}", mcode)
        body = destroy.group(0) if destroy else ""
        if not body:
            errors.append("[接线] 无法解析 PetNestRuntimeModule.OnDestroy")
        flush_call = "PetNestSaveCoordinator.TryFlushOnHostDestroy()"
        reset_call = "PetNestSaveCoordinator.ResetStaticCaches()"
        for call in [flush_call, reset_call]:
            if call not in body:
                errors.append("[接线] PetNestRuntimeModule.OnDestroy 缺少: " + call)
        if flush_call in body and reset_call in body and body.find(flush_call) > body.find(reset_call):
            errors.append("[接线] 模块 OnDestroy 必须先落盘再复位协调器")
        # 协调器复位必须传递到持久层，否则模块不直接触碰 PetNestPersistence（分层约定）就没人清它
        if "PetNestPersistence.ResetStaticCaches()" not in code:
            errors.append("[接线] PetNestSaveCoordinator.ResetStaticCaches 必须连带复位 PetNestPersistence")

    host = read_text(repo_path("ModBehaviour.cs"))
    if host is None:
        errors.append("[File] 缺少 ModBehaviour.cs")
    else:
        hcode = strip_cs_comments(host)
        host_destroy = re.search(r"void OnDestroy\(\)[\s\S]*?\n        \}", hcode)
        hbody = host_destroy.group(0) if host_destroy else ""
        if "runtimeModuleHost.OnDestroy()" not in hbody:
            errors.append("[owner] ModBehaviour.OnDestroy 必须经 runtimeModuleHost.OnDestroy() 到达模块清理")
        for forbidden in ["PetNestSaveCoordinator.", "PetNestPersistence."]:
            if forbidden in hbody:
                errors.append("[owner] ModBehaviour.OnDestroy 不得再内联遗种巢清理（" + forbidden
                              + "），唯一 owner 是 PetNestRuntimeModule.OnDestroy")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
