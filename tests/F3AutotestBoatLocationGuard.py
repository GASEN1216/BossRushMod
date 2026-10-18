"""F3 船点补加载须按官方缓存路径定位，并等待官方任务的真实结果。

本守卫只证明生产接线；Unity 子场景、位置和真实船点交互仍需 L3。
"""
from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "DebugAndTools/F3GameplayValidationAutotest.cs"


def method(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[opening:end])


def main():
    raw = SOURCE.read_text(encoding="utf-8-sig")
    assert raw.lstrip().startswith("#if BOSSRUSH_DEV"), "船点自动验收必须整文件 Dev-only"
    source = clean_source(raw)
    select = method(source, "private static bool TryFindAutotestBoatLocation(out MultiSceneLocation boatLocation,")
    depart = method(source, "private IEnumerator AutotestDepart()")
    assert "foreach (SubSceneEntry entry in core.SubScenes)" in select, "必须读取当前官方子场景"
    assert "foreach (SubSceneEntry.Location location in entry.cachedLocations)" in select, "必须读取真实缓存路径"
    assert "boatLocation = new MultiSceneLocation { SceneID = entry.sceneID, LocationName = location.path };" in select, "必须保留缓存原场景和完整 path，不能硬编码或截断路径"
    assert 'location.path.IndexOf("Boat", StringComparison.OrdinalIgnoreCase)' in select
    assert 'location.path.IndexOf("Dock", StringComparison.OrdinalIgnoreCase)' in select
    for forbidden in ("location.position", "new Vector3", "SetPosition(", "mapWorldCenter"):
        assert forbidden not in select + depart, "船点定位不能再换算缓存坐标或直接移动玩家: " + forbidden
    assert "if (TryFindAutotestBoatLocation(out boatLocation, notes))" in depart, "实际出发必须使用缓存选中的路径"
    assert "teleport = MultiSceneCore.Instance.LoadAndTeleport(boatLocation); started = true;" in depart, "必须调用官方 MultiSceneLocation 重载"
    assert depart.count("LoadAndTeleport(") == 1, "不能保留另一个坐标重载旁路"
    assert "bool started = false, arrived = false;" in depart, "成功标志必须从 false 开始"
    assert "while (started && teleport.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < until && !ShouldAbort()) yield return null;" in depart, "必须等待官方任务终态，同时保留看门狗与取消门"
    terminal = "if (teleport.Status == UniTaskStatus.Pending) teleport.Forget(); else { try { arrived = teleport.GetAwaiter().GetResult();"
    assert terminal in depart, "仅终态能消费实际返回值；超时 pending 不能当成功"
    assert depart.count("teleport.GetAwaiter().GetResult()") == 1, "UniTask 实际结果只能消费一次"
    assert "arrived = true;" not in depart, "不能推测船点加载成功"
    assert "while (arrived && departure == null && Time.realtimeSinceStartup < until && !ShouldAbort())" in depart, "仅实际返回 true 才能继续查找出发交互点"
    assert depart.index("arrived = teleport.GetAwaiter().GetResult();") < depart.index("while (arrived && departure == null"), "结果消费必须先于补加载后的交互查找"
    assert "CharacterMainControl.Main.Interact(departure);" in depart, "到达后仍须真实官方交互"
    assert "boatTried = started || realEntry;" in depart, "保留真实读条启动判据"
    assert 'string departResult = realEntry ? "PASS" : boatTried ? "FAIL" : "SKIP";' in depart, "加载成功不能代替真实入岛 PASS"
    assert 'SkyIslandSession.Enter(_host, delegate (string message, bool error)' in depart, "保留原生产 Enter 兜底"
    assert "finally { _autotestDepartureOpen = false; }" in depart, "取消和异常必须关闭交互放行窗口"
    print("F3AutotestBoatLocationGuard: PASS (L1 wiring; Unity L3 pending)")


if __name__ == "__main__":
    main()
