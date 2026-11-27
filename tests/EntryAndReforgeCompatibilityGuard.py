"""入场回调必须先过滤附加资源；重铸数值、颜色与揭晓必须共用收益方向。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def method(path, signature):
    text = read(path)
    start = text.index(signature)
    opening = text.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[opening:end]


def main():
    maps = "MapSelection/BossRushMapSelectionHelper.cs"
    root = method("ModBehaviour.cs", "private void OnSceneLoaded(")
    assert re.search(r"if\s*\(BossRushMapSelectionHelper\.ShouldIgnoreAuxiliarySceneLoad\(scene,\s*mode,\s*bossRushArenaPlanned\s*\|\|\s*bossRushArenaActive\)\)\s*return;", root), "必须在宿主入口过滤附加资源"
    assert root.index("ShouldIgnoreAuxiliarySceneLoad(") < root.index("PrepareSceneRuntimeForLoad();"), "不能在清理模式之后才过滤"
    selection = method(maps, "internal static void RecordSelection(MapSelectionEntry")
    assert "!mapSelectionEntry.Cost.Enough" in selection and "!mapSelectionEntry.ConditionsSatisfied" in selection
    assert "BossRushMapSelectionHelper.SetPendingMapEntryIndex(marker.entryIndex);" in selection
    fallback = method(maps, "public void OnPointerClick(")
    assert "BossRushMapEntrySelectionPatch.RecordSelection(GetComponent<MapSelectionEntry>());" in fallback, "点击回退必须共用入场门"
    assert '[HarmonyPatch(typeof(MapSelectionView), "NotifyEntryClicked", new Type[] { typeof(MapSelectionEntry), typeof(PointerEventData) })]' in read(maps)
    callback = method("Integration/BossRushIntegration_StartAndScene.cs", "private void OnSceneLoaded_Integration(")
    assert "(!bossRushArenaPlanned || BossRushMapSelectionHelper.IsPendingTargetScene(scene.name))" in callback, "DEMO 接管必须匹配选中地图"
    travel = method("Integration/BossRushIntegration_TravelAndSetup.cs", "private System.Collections.IEnumerator TeleportPlayerToCustomPosition(")
    assert "yield return bossRushIntegrationRuntime.WaitForCustomTeleportSceneReady();" in travel, "宿主等待必须到达真实 Integration owner"
    assert "bossRushIntegrationRuntime.ApplyCustomTeleportPosition(" in travel, "宿主位置写入必须到达真实 Integration owner"
    owner = "Integration/BossRushIntegrationRuntimeModule_Travel.cs"
    wait = method(owner, "internal IEnumerator WaitForCustomTeleportSceneReady(")
    assert re.search(r"if\s*\(SceneLoader.IsSceneLoading\s*\|\|\s*\(entryCore != null && entryCore.IsLoading\)\)\s*\{\s*yield return null;\s*elapsed \+= Mathf.Max\(0f, Time.unscaledDeltaTime\);\s*continue;", wait), "最终出场结束前不能移动玩家，且加载也计入有限超时"
    assert wait.index("entryCore.IsLoading") < wait.index('ReadMainExistsWithWarning(') < wait.index("elapsed += interval;"), "官方加载门须位于就绪读取及超时推进之前"
    assert "if (!bossRushIntegrationRuntime.IsCustomTeleportReady()) yield break;" in travel, "超时或失去 owner 不得继续搬人"
    landing = method(owner, "internal Vector3 ApplyCustomTeleportPosition(")
    gate = re.search(r"if\s*\(isModeEEntry\s*\|\|\s*BossRushInitialSpawn.HasArrived\(targetPosition\)\)\s*\{\s*return finalPosition;\s*\}", landing)
    assert gate and gate.start() < landing.index("Physics.RaycastAll("), "首次已到达时不能再次搬人"
    spawn = "MapSelection/BossRushInitialSpawn.cs"
    select = method(spawn, "internal static void Select(LevelManager")
    assert "BossRushMapSelectionHelper.TakeInitialSpawnSelection()" in select
    assert "p.PatchMethod == prefix" in select, "配套位置路由缺失时不能返回虚构 path"
    assert "position = entry.Position" in select
    load = method(spawn, "internal async UniTask<bool> Load(")
    assert "await core.LoadAndTeleport(SceneId, Position, false)" in load, "出生配置必须按世界坐标交给官方"
    confirm = method(spawn, "internal static async UniTask<bool> Observe(")
    assert confirm.index("await confirmation") < confirm.index("ConfirmInitialSpawnSelection("), "必须等确认，不能选图即授权出生"
    assert "BossRushMapSelectionHelper.CancelUnstartedMapSelection();" in method(maps, "internal static void OnClosed()")
    assert "pendingPrepaidTicketForCurrentEntry" in method(maps, "internal static void CancelUnstartedMapSelection("), "迟到的关闭回调不能取消已开始入場的预扣票所有权"
    reforge = method("Integration/Reforge/ReforgeSystem.cs", "public static ReforgeResult Reforge(")
    assert "int sign = RollSign(tendencyChance) * GetBeneficialValueDirection(prop.Key);" in reforge, "正向倾向必须换算属性极性"
    # 2026-11-25 下调：每次以预制体原值为基准重抽，不在当前值上累加；读档把旧超限值夹进新上下限
    assert "newValue = prefabValue + sign * mag01 * rollRange;" in reforge, "重铸必须以预制体原值为基准重抽"
    assert "originalValue + sign" not in reforge and "originalValue + delta" not in reforge, "重铸不能在当前值上累加"
    # 2026-11-27：上限是重抽分布的端点，顶档幅度必须原样取到边界（不再舍入），否则 Max/Min 抽不到
    snap = re.search(r"if\s*\(mag01 >= BOUND_SNAP_MAGNITUDE\)\s*\{\s*newValue = sign > 0 \? maxValue : minValue;\s*\}\s*else\s*\{", reforge)
    assert snap, "顶档幅度必须直接取到上下限，且这一支不舍入"
    assert snap.end() < reforge.index("newValue = prefabValue + sign * mag01 * rollRange;") < reforge.index("System.Math.Round(newValue, 2)"), "舍入只能在普通幅度那一支"
    assert snap.start() < reforge.index("newValue = Mathf.Clamp(newValue, minValue, maxValue);"), "顶档之后仍要夹进上下限"
    snap_const = re.search(r"private const float BOUND_SNAP_MAGNITUDE = ([0-9.]+)f;", read("Integration/Reforge/ReforgeSystem.cs"))
    assert snap_const and 0.9 <= float(snap_const.group(1)) < 1.0, "顶档阈值必须落在 [0.9, 1)，等于或大于 1 就又抽不到 Max"
    # 详情对比与重铸核心同口径：Stat 一律比 BaseValue；Value 叠着配件修正，会把配件算成重铸差值、Max/Min 判错
    setup = method("Integration/Reforge/ReforgeUIManager_RuntimeAndCleanup.cs", "private static void SetupDetailsWithPrefabComparison(")
    assert setup.count("= stat.BaseValue;") == 2 and "= stat.Value;" not in setup, "预制体与玩家 Stat 都要按 BaseValue 比"
    threshold = re.search(r"private const float DIFF_THRESHOLD = ([0-9.]+)f;", read("Integration/Reforge/ReforgeUIManager.cs"))
    assert threshold and 0 < float(threshold.group(1)) <= 0.005, "差值阈值必须小于 0.01 的一格，否则一格变化与小数值的 Max 会被吞"
    persistence = read("Integration/Reforge/ReforgeDataPersistenceCleanup.cs")
    for signature in ("private static bool TryRestoreModifier(", "private static bool TryRestoreStat(", "private static bool TryRestoreVariable("):
        body = method("Integration/Reforge/ReforgeDataPersistence.cs", signature)
        assert "if (prefab != null) newValue = ReforgeSystem.ClampToReforgeBounds(" in body, signature + " 读档恢复必须夹进当前上下限"
    sync = method("Integration/Reforge/ReforgeDataPersistence.cs", "public static void SyncCurrentReforgeState(")
    assert sync.count("SyncTrackedReforgeValue(item, ") == 3 and "SaveReforgeData" not in sync, "存档同步不能把被夹住的旧差值改写掉"
    assert "ReforgeSystem.ClampToReforgeBounds(propertyKey, prefabValue, prefabValue + storedDelta)" in persistence
    for file, signature in (("ReforgeUIManager_ComparisonAndState.cs", "private static void ShowPropertyChanges("),
                            ("ReforgeUIManager_RuntimeAndCleanup.cs", "private static System.Collections.IEnumerator ModifyPropertyTextsDelayed(")):
        body = method("Integration/Reforge/" + file, signature)
        assert "ReforgeSystem.IsBeneficialChange(key, diff)" in body, file + " 颜色仍按数值正负"
        assert "diff > 0 ? IntegrationUIFeedback.SuccessHex" not in body
    reveal = method("Integration/Reforge/ReforgeUIManager_Feel.cs", "private static void QueueReforgeReveal(")
    assert "Beneficial = ReforgeSystem.IsBeneficialChange(key, diff)" in reveal
    assert "ReforgeSystem.IsValueAtLowerBound(key, prefabValue, newValue)" in reveal, "后坐力最优边界应为下限"
    print("EntryAndReforgeCompatibilityGuard: PASS")


if __name__ == "__main__":
    main()
