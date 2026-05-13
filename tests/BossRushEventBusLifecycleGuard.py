"""Guard: BossRushEventBus is a narrow low-risk notification pilot."""

from pathlib import Path
import re
import sys


BUS = Path("Common/Events/BossRushEventBus.cs")
COMPILE = Path("compile_official.bat")
ALWAYS_ON = Path("Utilities/AlwaysOnRuntimeHooks.cs")
ACHIEVEMENT_MANAGER = Path("Achievement/BossRushAchievementManager.cs")
ACHIEVEMENT_RUNTIME = Path("Achievement/AchievementRuntimeHooks.cs")
NON_GOAL = Path("tests/LongTermGoalNonGoalGuard.py")


def fail(message: str) -> int:
    print("BossRushEventBusLifecycleGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not BUS.exists():
        return fail("Common/Events/BossRushEventBus.cs is missing")

    bus_text = BUS.read_text(encoding="utf-8")
    for snippet in [
        "internal static class BossRushEventBus",
        "Subscribe<TEvent>",
        "Unsubscribe<TEvent>",
        "Publish<TEvent>",
        "ClearRuntimeSubscribers()",
        "ResetStaticCaches()",
        "Dictionary<Type, List<Delegate>> runtimeSubscribers",
        "BossRushAchievementUnlockedEvent",
    ]:
        if snippet not in bus_text:
            return fail("event bus missing snippet: " + snippet)

    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    if "Common\\Events\\BossRushEventBus.cs" not in compile_text:
        return fail("compile_official.bat does not compile BossRushEventBus.cs")

    always_on_text = ALWAYS_ON.read_text(encoding="utf-8")
    if "BossRushEventBus.ResetStaticCaches();" not in always_on_text:
        return fail("BossRushEventBus static subscribers are not cleared on destroy")

    manager_text = ACHIEVEMENT_MANAGER.read_text(encoding="utf-8")
    if "BossRushEventBus.Publish(new BossRushAchievementUnlockedEvent(achievement));" not in manager_text:
        return fail("achievement unlock notification is not published through BossRushEventBus")
    if "SteamAchievementPopup.Show(achievement);" in manager_text:
        return fail("achievement manager still directly owns popup notification")

    runtime_text = ACHIEVEMENT_RUNTIME.read_text(encoding="utf-8")
    for snippet in [
        "BossRushEventBus.Subscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);",
        "BossRushEventBus.Unsubscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);",
        "private void OnBossRushAchievementUnlockedEvent(BossRushAchievementUnlockedEvent eventData)",
        "SteamAchievementPopup.Show(eventData.Achievement);",
    ]:
        if snippet not in runtime_text:
            return fail("achievement runtime missing event-bus lifecycle snippet: " + snippet)

    non_goal_text = NON_GOAL.read_text(encoding="utf-8")
    forbidden_list = re.search(r"FORBIDDEN_TYPE_NAMES\s*=\s*\[(?P<body>.*?)\]", non_goal_text, re.S)
    if not forbidden_list:
        return fail("LongTermGoalNonGoalGuard forbidden list not found")
    if '"BossRushEventBus"' in forbidden_list.group("body"):
        return fail("LongTermGoalNonGoalGuard still forbids the controlled BossRushEventBus pilot")
    for still_forbidden in ['"EventBus"', '"IGameWorldProbe"', '"IBossRushEventSubscriber"']:
        if still_forbidden not in forbidden_list.group("body"):
            return fail("LongTermGoalNonGoalGuard lost forbidden type: " + still_forbidden)

    print("BossRushEventBusLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
