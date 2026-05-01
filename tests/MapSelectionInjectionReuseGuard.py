from pathlib import Path
import sys


BOSSRUSH = Path("MapSelection/BossRushMapSelectionHelper.cs")
ZOMBIE = Path("ZombieMode/ZombieModeMapSelectionHelper.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, marker: str) -> str:
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
    bossrush = BOSSRUSH.read_text(encoding="utf-8")
    zombie = ZOMBIE.read_text(encoding="utf-8")

    for label, text in [("BossRush", bossrush), ("ZombieMode", zombie)]:
        if "MapSelectionEntryInjectionHelper.InjectEntries(" not in text:
            return fail("MapSelectionInjectionReuseGuard: " + label + " does not use shared InjectEntries")

    inject = extract_method(bossrush, "private static bool InjectBossRushEntry")
    if not inject:
        return fail("MapSelectionInjectionReuseGuard: cannot extract InjectBossRushEntry")

    banned_tokens = [
        "List<MapSelectionEntry> templateCandidates",
        "Dictionary<string, MapSelectionEntry> templateBySceneID",
        "Dictionary<Transform, List<MapSelectionEntry>> columnGroups",
        "UnityEngine.Object.Instantiate(templateToUse.gameObject, targetParent)",
        "CreateOverflowContainer(",
    ]
    for token in banned_tokens:
        if token in inject:
            return fail("MapSelectionInjectionReuseGuard: BossRush injection still has duplicated template logic -> " + token)

    for token in [
        "private static Transform CreateOverflowContainer",
        "private static void ClearCostDisplayItems(CostDisplay costDisplay)",
        "private static void SetEntryDisplayNameDirect(GameObject clonedObject",
    ]:
        if token in bossrush:
            return fail("MapSelectionInjectionReuseGuard: BossRush still has local helper duplicated from MapSelectionEntryInjectionHelper -> " + token)

    for token in [
        "ConfigureBossRushEntryWithMapConfig",
        "GetBossRushEntryDisplayName",
        "SetupBossRushCostDisplay",
        "OnBossRushEntryCreated",
        "UpdateEntryThumbnailWithImage(uiEntry, mapConfig.previewImageName)",
        "bossRushEntryObject = bossRushEntryObjects.Count > 0 ? bossRushEntryObjects[0] : null;",
    ]:
        if token not in bossrush:
            return fail("MapSelectionInjectionReuseGuard: missing BossRush shared-injection token -> " + token)

    print("MapSelectionInjectionReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
