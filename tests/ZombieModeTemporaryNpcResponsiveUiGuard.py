from pathlib import Path
import sys


REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)

HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")


def fail(message: str) -> int:
    print("ZombieModeTemporaryNpcResponsiveUiGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    rewards = read_rewards()
    helper = HELPER.read_text(encoding="utf-8")

    for snippet in [
        "tmp.enableAutoSizing = true;",
        "tmp.fontSizeMin = Mathf.Max(10f, fontSize * 0.65f);",
        "scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;",
        "scaler.referenceResolution = new Vector2(1920f, 1080f);",
    ]:
        result = require(helper, snippet, "shared responsive UI helper")
        if result:
            return result

    for snippet in [
        "ScrollRect scrollRect = body.AddComponent<ScrollRect>();",
        "Mask viewportMask = viewport.AddComponent<Mask>();",
        "viewportMask.showMaskGraphic = false;",
        "GridLayoutGroup grid = parent.gameObject.AddComponent<GridLayoutGroup>();",
        "grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;",
        "grid.constraintCount = 4;",
        "ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();",
        "VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();",
        "LayoutElement layoutElement = obj.AddComponent<LayoutElement>();",
    ]:
        result = require(rewards, snippet, "temporary NPC responsive layout")
        if result:
            return result

    print("ZombieModeTemporaryNpcResponsiveUiGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
