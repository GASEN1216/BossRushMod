"""玩家整备选择必须可达，并由赔率、摘要、锁盘消费同一口令。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def main():
    files = {name: clean_source((ROOT / "ModeH" / name).read_text(encoding="utf-8")) for name in [
        "ModeHRuntimeModule_LoadoutEditing.cs", "ModeHRuntimeModule_MatchFlow.cs",
        "ModeHRuntimeModule_CombatFlow.cs", "ModeHUIPages.cs", "ModeHRuntimeModule_MatchPages.cs"]}
    editor, match_flow, combat, ui, pages = files.values()
    # 2026-09-24：赔率页组装从 MatchFlow 移到 MatchPages（MatchFlow 贴着行数预算）
    flow = match_flow + pages
    required = [
        (flow, "_showLoadoutEditor = true;"), (flow, "return BuildLoadoutEditorPage();"),
        (editor, "ReferenceEquals(_season.matchRoster, roster)"),
        (editor, "roster.matchStarterProfileId = profileId;"),
        (editor, "roster.matchRelayProfileId = string.Empty;"),
        (editor, "selected.Remove(choice.Spec.KitId)"),
        (editor, "GetSelectableKits("), (editor, "GetSelectableCommands("),
        (editor, "_selectedMatchCommandId = selected;"),
        (editor, "input.commandId = _selectedMatchCommandId;"),
        (combat, "string commandId = _selectedMatchCommandId;"),
        (combat, "commands.Contains(_selectedMatchCommandId) ? _selectedMatchCommandId : null"),
        (combat, "ModeHCommandController.ResolveCommandOwner(lockedCommand, starter, commandRelay)"),
        (combat, "lockedCommand, commandOwner,"),
        (ui, "CreatePreparationOptions(surface, panelSize, content, cursorY)"),
        # 2026-09-25：整备选项可分多列（阵容页左首发右接力、配装两列），滚动高度按行数 = 选项数 / 列数
        (ui, "(content.PreparationOptions.Count + columns - 1) / columns * rowHeight"),
    ]
    missing = [token for source, token in required if token not in source]
    if missing:
        print("ModeHLoadoutEditingGuard: FAIL " + ", ".join(missing))
        return 1
    print("ModeHLoadoutEditingGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
