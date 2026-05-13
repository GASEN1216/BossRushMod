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



def fail(message: str) -> int:
    print("ZombieModeTemporaryNpcUiOpenGuard: FAIL - " + message)
    return 1


def extract_block(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = read_rewards()

    open_ui = extract_block(text, "public void OpenZombieModeTemporaryNpcServiceUi")
    if "FindObjectsOfType<ZombieModeTemporaryNpcServiceView>" not in open_ui:
        return fail("service UI open path must dedupe existing views")
    if 'Destroy(existingViews[i].gameObject);' not in open_ui:
        return fail("service UI open path must destroy stale duplicate views before opening")

    interactable = extract_block(text, "public sealed class ZombieModeTemporaryNpcInteractable")
    if not interactable:
        return fail("ZombieModeTemporaryNpcInteractable not found")

    on_interact = extract_block(interactable, "protected override void OnInteractStart")
    on_timeout = extract_block(interactable, "protected override void OnTimeOut")
    if "OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);" not in on_interact:
        return fail("OnInteractStart must remain the single UI-open entry")
    if "OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);" in on_timeout:
        return fail("OnTimeOut must not open duplicate temporary NPC UI")

    service_view = extract_block(text, "public sealed class ZombieModeTemporaryNpcServiceView")
    if "new Vector2(820f, 620f)" not in service_view:
        return fail("service UI panel size update missing")
    if "new Vector2(168f, 102f)" not in service_view:
        return fail("service UI button size update missing")
    if "new Vector2(700f, 60f)" not in service_view:
        return fail("service UI title width update missing")

    print("ZombieModeTemporaryNpcUiOpenGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
