"""Guard: Zombie Mode HUD should avoid redundant TMP assignments."""

from pathlib import Path
import sys


VIEW = Path("ZombieMode/ZombieModeHudController.cs")
MODULE = Path("ZombieMode/ZombieModeRuntimeModule_Hud.cs")


def fail(message: str) -> int:
    print("ZombieModeHudTextAssignmentGuard: FAIL - " + message)
    return 1


def main() -> int:
    view = VIEW.read_text(encoding="utf-8-sig")
    module = MODULE.read_text(encoding="utf-8-sig")

    required = [
        "internal string LastMainText;",
        "internal string LastSafeZoneText;",
        "internal string LastStageText;",
        # 2026-09-23 审美审查 UC-04：主面板带上 HUD 滚动中的净化点显示值，仍走缓存比较。
        "owner.GetZombieModeHudMainText(runId, state.ShownPurification)",
        "owner.GetZombieModeHudSafeZoneText(runId)",
        "owner.GetZombieModeHudStageText(runId)",
        "private static bool SetZombieModeHudText(ref string lastValue, string value)",
        "string.Equals(lastValue, nextValue, StringComparison.Ordinal)",
        "lastValue = nextValue;",
        "controller.SetMainText(state.LastMainText",
        "controller.SetSafeZoneText(state.LastSafeZoneText",
        "controller.SetStageText(state.LastStageText)",
    ]
    for needle in required:
        if needle not in module:
            return fail(f"missing expected HUD assignment cache pattern: {needle}")

    for needle in [
        "mainText.text = value ?? string.Empty;",
        "safeZoneText.text = value ?? string.Empty;",
        "stageText.text = value ?? string.Empty;",
    ]:
        if needle not in view:
            return fail(f"view is missing the module-directed TMP assignment: {needle}")

    for needle in ("private string lastMainText;", "private string lastSafeZoneText;", "private string lastStageText;",
                   "private float nextRefreshTime;", "private int lastActualPurification = -1;",
                   "private float preparationTotal;"):
        if needle in view:
            return fail(f"HUD runtime cache remained on the view component: {needle}")

    tick = module[module.index("internal void TickZombieModeHud("):module.index("private ZombieModeHudRuntimeState GetZombieModeHudState(")]
    for needle in ("SetZombieModeHudText(ref state.LastMainText", "SetZombieModeHudText(ref state.LastSafeZoneText",
                   "SetZombieModeHudText(ref state.LastStageText"):
        if needle not in tick:
            return fail(f"module refresh path is missing cache comparison: {needle}")

    forbidden = [
        "mainText.text = inst.GetZombieModeHudMainText(RunId);",
        "safeZoneText.text = inst.GetZombieModeHudSafeZoneText(RunId);",
        "stageText.text = inst.GetZombieModeHudStageText(RunId);",
    ]
    for needle in forbidden:
        if needle in view or needle in module:
            return fail(f"still has direct repeated TMP assignment: {needle}")

    print("ZombieModeHudTextAssignmentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
