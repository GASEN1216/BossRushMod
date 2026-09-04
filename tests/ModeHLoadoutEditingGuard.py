"""玩家整备选择必须可达，并由赔率、摘要、锁盘消费同一口令。"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def main():
    files = {name: (ROOT / "ModeH" / name).read_text(encoding="utf-8") for name in [
        "ModeHRuntimeModule_LoadoutEditing.cs", "ModeHRuntimeModule_MatchFlow.cs",
        "ModeHRuntimeModule_CombatFlow.cs", "ModeHUIPages.cs"]}
    editor, flow, combat, ui = files.values()
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
        (combat, "lockedCommand == commandRelay.signatureCommandId"),
        (combat, "lockedCommand, commandOwner,"),
        (ui, "CreatePreparationOptions(surface, panelSize, content, cursorY)"),
        (ui, "content.PreparationOptions.Count * rowHeight"),
    ]
    missing = [token for source, token in required if token not in source]
    if missing:
        print("ModeHLoadoutEditingGuard: FAIL " + ", ".join(missing))
        return 1
    print("ModeHLoadoutEditingGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
