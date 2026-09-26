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
    assert re.search(r"if\s*\(SceneLoader.IsSceneLoading\s*\|\|\s*\(entryCore != null && entryCore.IsLoading\)\)\s*\{\s*yield return null;\s*continue;", travel), "最终出场结束前不能移动玩家"
    assert "!BossRushInitialSpawn.HasArrived(targetPosition)" in travel, "首次已到达时不能再次搬人"
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
