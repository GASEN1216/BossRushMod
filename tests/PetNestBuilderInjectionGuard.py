#!/usr/bin/env python3
"""
PetNestBuilderInjectionGuard — 遗种巢建筑注入守卫（实施计划 步骤 9）。

不变式：
- **只加一个建筑**（最小化修改）：巢/孵化/远征/博物馆四个功能挂同一个交互点，
  走多选项交互菜单，不新增第二、第三个建筑；
- 反射注入 BuildingDataCollection 的 infos + prefabs，注入前判重，注入后清 readonly 缓存；
- **早期注入**：老存档里已建过该建筑时必须赶在 BuildingArea.Start 之前注册 prefab，
  且早期注入分支不触发建筑区重绘；
- prefab 创建时序：先 SetActive(false) → 填容器字段 → 最后 SetActive(true)
  （官方 Building.Awake 会解引用 functionContainer）；
- **零新增 Unity 资源**：缺 bundle / 缺图标时走占位模型 fallback，不 fail；
  占位图元自带的 Collider 必须删（会干扰建筑放置与交互）；
- OnBuildingBuilt / OnBuildingDestroyed 订阅与退订成对；
- 交互点装配时先 SetActive(false) 再挂组件，填好字段后才激活；
- 共享反射工具不得重复定义（FindGameType 等定义在 Integration/Wedding/ 下同一 partial class）；
- 基地场景装配管线与 Mod 卸载路径都已接线。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestBuilderInjectionGuard"

# 这些共享 helper 定义在 Integration/Wedding/ 下、属于同一个 partial class ModBehaviour，
# 遗种巢再定义一份会编译报重复成员
SHARED_HELPERS = [
    "private static Type FindGameType(",
    "private static Type GetBuildingManagerType(",
    "private static MethodInfo GetBuildingManagerAnyMethod(",
    "private static MethodInfo GetBuildingDataMethod(",
    "private static Type GetBuildingType(",
    "private static PropertyInfo GetBuildingIdProperty(",
    "private void AssignBuildingContainerField(",
    "private void RequestBaseBuildingAreaRepaint(",
]


def check_builder(errors):
    text = read_petnest("PetNestBuilder.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestBuilder.cs")
        return
    code = strip_cs_comments(text)

    # 常量与身份
    for token, desc in [
        ('PETNEST_BUILDING_ID = "petnest_relic_nest"', "建筑 ID"),
        ('PETNEST_PREFAB_NAME = "PetNestRelicNest"', "prefab 名"),
        ("PETNEST_BUILDING_MAX_AMOUNT = 1", "单巢上限"),
    ]:
        if token not in code:
            errors.append("[常量] 缺少: " + desc)

    # 早期注入
    early = re.search(r"private void TryInitializePetNestEarly\(\)[\s\S]{0,1200}?\n        \}", code)
    if early is None:
        errors.append("[早期注入] 缺少 TryInitializePetNestEarly()")
    else:
        body = early.group(0)
        if "IsBaseHubSceneName(activeScene.name)" not in body:
            errors.append("[早期注入] 必须限定基地场景")
        if "InitPetNestBuilding(true)" not in body:
            errors.append("[早期注入] 必须走 isEarlyInit 分支")

    init = re.search(r"private void InitPetNestBuilding\(bool isEarlyInit\)[\s\S]{0,1600}?\n        \}", code)
    if init is None:
        errors.append("[初始化] 缺少 InitPetNestBuilding(bool)")
    else:
        body = init.group(0)
        if "if (petNestBuildingInjected)" not in body:
            errors.append("[幂等] 初始化必须幂等早返")
        if "if (!isEarlyInit && HasPendingPetNestBuildingsInManager())" not in body:
            errors.append("[早期注入] 早期注入分支不得触发建筑区重绘")
        for step in ["LoadPetNestBuildingIcon()", "LoadPetNestBuildingModel()",
                     "CreatePetNestBuildingPrefab()", "InjectPetNestBuildingData()",
                     "RegisterPetNestBuildingEvents()"]:
            if step not in body:
                errors.append("[初始化] 缺少步骤: " + step)

    # prefab 时序
    prefab = re.search(r"private void CreatePetNestBuildingPrefab\(\)[\s\S]{0,2000}?\n        \}", code)
    if prefab is None:
        errors.append("[prefab] 缺少 CreatePetNestBuildingPrefab()")
    else:
        body = prefab.group(0)
        off = body.find("petNestBuildingPrefabGO.SetActive(false);")
        on = body.find("petNestBuildingPrefabGO.SetActive(true);")
        add = body.find("AddPetNestBuildingComponent(petNestBuildingPrefabGO);")
        if off < 0 or on < 0 or add < 0:
            errors.append("[prefab] 时序锚点缺失（先 inactive、填字段、再 active）")
        elif not (off < add < on):
            errors.append("[prefab] 必须先 SetActive(false)，填完容器字段后才 SetActive(true)")

    # 零新增资源：占位 fallback
    if "CreatePetNestPlaceholderModel(" not in code:
        errors.append("[零资源] 缺少占位模型 fallback")
    placeholder = re.search(r"private static void CreatePetNestPlaceholderPart\([\s\S]{0,1400}?\n        \}", code)
    if placeholder is None:
        errors.append("[零资源] 缺少占位图元构造")
    elif "UnityEngine.Object.Destroy(collider);" not in placeholder.group(0):
        errors.append("[零资源] CreatePrimitive 自带的 Collider 必须删除（会干扰建筑放置与交互）")

    # 清理
    if not re.search(r"public void CleanupPetNestBuilding\(\)", code):
        errors.append("[清理] 缺少 CleanupPetNestBuilding()")


def check_runtime(errors):
    text = read_petnest("PetNestBuilder_DataEventsAndRuntime.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestBuilder_DataEventsAndRuntime.cs")
        return
    code = strip_cs_comments(text)

    # 共享 helper 不得重复定义
    for helper in SHARED_HELPERS:
        if helper in code:
            errors.append("[复用] 共享反射工具不得重复定义: " + helper)

    # 数据注入
    inject = re.search(r"private void InjectPetNestBuildingData\(\)[\s\S]{0,8000}?\n        \}", code)
    if inject is None:
        errors.append("[注入] 缺少 InjectPetNestBuildingData()")
    else:
        body = inject.group(0)
        for token, desc in [
            ('FindGameType("Duckov.Buildings.BuildingDataCollection")', "反射取 BuildingDataCollection"),
            ('bdcType.GetField("infos"', "取 infos 列表"),
            ('bdcType.GetField("prefabs"', "取 prefabs 列表"),
            ('bdcType.GetField("readonlyInfos"', "清 readonly 缓存"),
        ]:
            if token not in body:
                errors.append("[注入] 缺少: " + desc)
        if "跳过注入" not in text:
            errors.append("[注入] 必须先判重再注入（同进程 BuildingDataCollection 是长寿资产）")

    # Building 组件字段
    comp = re.search(r"private void AddPetNestBuildingComponent\(GameObject go\)[\s\S]{0,1600}?\n        \}", code)
    if comp is None:
        errors.append("[组件] 缺少 AddPetNestBuildingComponent()")
    else:
        body = comp.group(0)
        for field in ["id", "dimensions", "graphicsContainer", "functionContainer", "areaMesh"]:
            if '"' + field + '"' not in body:
                errors.append("[组件] 缺少 Building 私有字段赋值: " + field)
        if "AssignBuildingContainerField(" not in body:
            errors.append("[组件] 容器字段必须走共享的 AssignBuildingContainerField")

    # 事件成对
    if 'GetEvent("OnBuildingBuilt"' not in code or 'GetEvent("OnBuildingDestroyed"' not in code:
        errors.append("[事件] 必须订阅 OnBuildingBuilt / OnBuildingDestroyed")
    if "AddEventHandler(null," not in code:
        errors.append("[事件] 静态事件订阅 target 必须传 null")
    if "RemoveEventHandler(null," not in code:
        errors.append("[事件] 必须成对退订")
    if "if (petNestBuildingEventsRegistered) return;" not in code:
        errors.append("[事件] 订阅必须有防重复 bool")

    # 交互点装配时序
    ensure = re.search(r"private void EnsurePetNestFunctionPoints\(GameObject buildingGO\)[\s\S]{0,2200}?\n        \}", code)
    if ensure is None:
        errors.append("[交互点] 缺少 EnsurePetNestFunctionPoints()")
    else:
        body = ensure.group(0)
        if "if (restoreActive) interactTr.gameObject.SetActive(false);" not in body:
            errors.append("[交互点] 挂组件前必须先 SetActive(false)")
        if "if (restoreActive) interactTr.gameObject.SetActive(true);" not in body:
            errors.append("[交互点] 字段填好后必须恢复 SetActive(true)")
        if "AddComponent<PetNestInteractable>()" not in body:
            errors.append("[交互点] 必须挂 PetNestInteractable")

    # 身份判定必须排除自己的 prefab
    if "object.ReferenceEquals(buildingGO, petNestBuildingPrefabGO)" not in code:
        errors.append("[身份] 必须排除自己那份 DontDestroyOnLoad 的 prefab")

    # 恢复协程去重
    if "if (petNestRestoreCoroutine != null) return;" not in code:
        errors.append("[协程] 恢复协程必须天然去重")
    if "finally" not in code or "petNestRestoreCoroutine = null;" not in code:
        errors.append("[协程] 协程结束必须归还句柄")


def check_interactable(errors):
    text = read_petnest("PetNestInteractable.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestInteractable.cs")
        return
    code = strip_cs_comments(text)

    if "NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this" not in code:
        errors.append("[菜单] 宿主必须走 PrepareGroupedInteractionOwner")
    # 必须在 base.Awake() 之前
    prepare_pos = code.find("PrepareGroupedInteractionOwner(this")
    awake_pos = code.find("base.Awake();")
    if prepare_pos >= 0 and awake_pos >= 0 and prepare_pos > awake_pos:
        errors.append("[时序] PrepareGroupedInteractionOwner 必须在 base.Awake() 之前")

    if "NPCInteractionGroupHelper.AddSubInteractable" not in code:
        errors.append("[菜单] 子选项必须走 AddSubInteractable")
    for option in ["PetNestHatchInteractable", "PetNestExpeditionInteractable", "PetNestMuseumInteractable"]:
        if option not in code:
            errors.append("[菜单] 缺少子选项: " + option)

    # 子选项在 Start 里、base.Start() 之后建
    start = re.search(r"protected override void Start\(\)[\s\S]{0,900}?\n        \}", code)
    if start is not None:
        body = start.group(0)
        base_pos = body.find("base.Start();")
        opt_pos = body.find("EnsureGroupedInteractionOptions();")
        if base_pos >= 0 and opt_pos >= 0 and opt_pos < base_pos:
            errors.append("[时序] 子选项必须在 base.Start() 之后装配")

    # 交互点不直接依赖面板
    if "PetNestUI." in code:
        errors.append("[解耦] 交互点不得直接依赖面板类，必须经 PetNestUIBridge")
    if "PetNestUIBridge.OpenPage(" not in code:
        errors.append("[解耦] 交互必须经 PetNestUIBridge.OpenPage")


def check_wiring(errors):
    boot = read_text(repo_path("Integration", "IntegrationDeferredBootstrap.cs"))
    if boot is None:
        errors.append("[File] 缺少 Integration/IntegrationDeferredBootstrap.cs")
    else:
        bcode = strip_cs_comments(boot)
        for token in ['RunDeferredStep_Integration("InitPetNestBuilding"',
                      'RunDeferredStep_Integration("RestorePetNestBuildings"']:
            if token not in bcode:
                errors.append("[接线] 基地场景装配管线缺少: " + token)

    scene = read_text(repo_path("Integration", "BossRushIntegration_StartAndScene.cs"))
    if scene is None:
        errors.append("[File] 缺少 Integration/BossRushIntegration_StartAndScene.cs")
    else:
        scode = strip_cs_comments(scene)
        if "TryInitializePetNestEarly();" not in scode:
            errors.append("[接线] 场景回调缺少早期注入")
        if "CleanupPetNestBuilding();" not in scode:
            errors.append("[接线] Mod 卸载路径缺少建筑清理")

    loc = read_text(repo_path("Localization", "PetNestLocalization.cs"))
    if loc is None:
        errors.append("[File] 缺少 Localization/PetNestLocalization.cs")
    else:
        lcode = strip_cs_comments(loc)
        # 官方约定 Building_ + id
        for key in ['"Building_petnest_relic_nest"', '"Building_petnest_relic_nest_Desc"']:
            if key not in lcode:
                errors.append("[本地化] 缺少官方建筑键: " + key)


def main():
    errors = []
    check_builder(errors)
    check_runtime(errors)
    check_interactable(errors)
    check_wiring(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
