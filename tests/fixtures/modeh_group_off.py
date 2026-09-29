"""鸭王杯单挑版回归夹具共用：把群战流程（2026-09-29 改版）关掉的最小替身。

这些夹具只抽取单挑版的生产方法验证旧路径；抽出来的方法里现在带着 `GroupModeEnabled` 分流与群战入口，
这里给出「群战关闭」的替身成员，让单挑版分支照旧被执行。群战本身的逻辑不在这些夹具的覆盖范围内。
"""

SNIPPETS = {
    "enabled": "internal static bool GroupModeEnabled { get { return false; } }",
    "odds": 'private bool EnsureGroupOddsQuote(out string failureReasonId) { failureReasonId = "group_mode_off"; return false; }',
    "lock": 'private bool PrepareGroupLockedMatch(out string failureReasonId) { failureReasonId = "group_mode_off"; return false; }',
    "route_page": "private bool RouteGroupPage(ModeHLifecycle lifecycle) { return false; }",
    "route_after": "private void RouteGroupAfterIntermission() { }",
    "hall_record": "private ModeHHallOfFameRecordDto BuildGroupHallOfFameRecord() { return null; }",
    "spawn": "private System.Collections.IEnumerator DriveGroupMatchSpawning() { yield break; }",
    "release": "private void ReleaseGroupBattle() { }",
    "tick": "private void TickGroupCombat(float deltaTime) { }",
    "plan": "private void EnsureGroupMatchPlan() { }",
    "bell": "private void OnGroupBellPressed() { }",
    "result_lines": "private void AppendGroupResultLines(ModeHPageContent page) { }",
    "battle_ref": "private object _groupBattle = null;",
    "battle": ("private GroupBattleOff _groupBattle = null; private sealed class GroupBattleOff { "
               "internal CharacterMainControl CameraFocus { get { return null; } } "
               "internal bool TrySurrender() { return false; } }"),
}


def members(*names):
    """按名字拼出替身成员（放进对应 partial class 的类体里）。"""
    return "\n".join(SNIPPETS[name] for name in names) + "\n"
