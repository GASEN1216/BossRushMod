"""RandomEventMerchantSingleEntryGuard: 神秘商人只保留「神秘商人」一个交互入口。

官方 InteractableBase.GetInteractableList 总会把根交互本身列进去；只删 StockShop
或只往根的子列表塞选项，都会留下一个点了没反应的原版「交易」。
生成流程必须：先收集原版交互 → 挂独立入口 → 把原版交互全部停用并销毁；
F3 验收以「商人身上启用的交互数 == 1」判合格。
"""

from pathlib import Path
import sys

from cs_source_util import clean_source

SPAWN = Path("RandomEvents/RandomEventEffectsBridge_Spawn.cs")
CATALOG = Path("RandomEvents/RandomEventCatalog.cs")


def fail(message: str) -> int:
    print("RandomEventMerchantSingleEntryGuard: FAIL - " + message)
    return 1


def body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    opening = text.find("{", start)
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    spawn = clean_source(SPAWN.read_text(encoding="utf-8-sig"))
    build = body(spawn, "private StockShop BuildRandomEventMerchantShop(GameObject npcGo)")
    if not build:
        return fail("BuildRandomEventMerchantShop not found")
    collect = build.find("InteractableBase[] originalInteractions = npcGo.GetComponentsInChildren<InteractableBase>(true);")
    add_entry = build.find("shopObj.AddComponent<RandomEventMerchantShopInteractable>()")
    destroy = build.find("UnityEngine.Object.Destroy(original);")
    if collect < 0 or add_entry < 0 or destroy < 0:
        return fail("merchant build must collect original interactions, add its own entry and destroy the originals")
    if not (collect < add_entry < destroy):
        return fail("original interactions must be captured before the new entry is added and destroyed after it")
    for token in ("original.enabled = false;", "group.Clear();", "original.interactCollider.enabled = false;"):
        if token not in build:
            return fail("original interaction must be fully disabled before destroy -> " + token)
    if "groupList.Add(interact)" in build or "mainInteract.interactableGroup = true" in build:
        return fail("the new entry must not be injected back into the original root group")

    catalog = clean_source(CATALOG.read_text(encoding="utf-8-sig"))
    outcome = body(catalog, "private int CountActiveMerchantInteractions()")
    if "GetComponentsInChildren<InteractableBase>(false)" not in outcome:
        return fail("F3 merchant validation must count active interactions")
    if "entries > 0 && interactions == 1" not in catalog:
        return fail("F3 merchant validation must require exactly one active interaction")

    print("RandomEventMerchantSingleEntryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
