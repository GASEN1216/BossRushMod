"""捏脸永久 NPC 的「喜欢的礼物标签」必须是官方真实存在的 Tag 名。

NPCGiftSystem.HasPositiveTag 按 `item.Tags` 里 Tag 资产的 `.name` 精确比对；写一个不存在的名字不会报错，
只会让这条喜好永远不命中。2026-09-27 发版复审查实：苇白 / 浮舟写的是 "Tools"，官方 Tag 叫 "Tool"，
苇白又没有按 TypeID 的喜好兜底，于是她「喜欢工具」这条 Wiki 承诺从来没兑现过。

官方 Tag 名单取自官方本地化表的 `Tag_<名>` 键（docs/reference/official-localization/English.csv，local-only，
所以这里内嵌一份）。官方更新新增 Tag 时把名字补进 OFFICIAL_TAGS。
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "Data" / "DuckNpcs.json"

OFFICIAL_TAGS = {
    "Accessory", "Armor", "Backpack", "Blueprint", "Bullet", "Character", "Continer", "Daily", "DecorateEquipment",
    "DestroyOnLootBox", "DontDropOnDead", "DontDropOnDeadInSlot", "Drink", "DuckStone", "Electric", "Equipment",
    "Explosive", "Food", "Gem", "Grip", "Gun", "GunType_Rifle", "GunType_Shot", "GunType_SMG", "GunType_Sniper",
    "Helmat", "Information", "Key", "Luxury", "Magazine", "Material", "Medic", "Misc", "Muzzle", "Quest", "Scope",
    "Special", "Stock", "TecEquip", "Tool", "Weapon", "Weapon_LV1", "Totem", "DestroyInBase", "MeleeWeapon",
    "Formula", "Formula_Blueprint", "Formula_Normal", "Formula_Medic", "ComputerParts_GPU", "Repairable",
}
# 2026-09-27 owner 拍板：旧的 "Consumable"（官方没有这个 Tag）已全部换成官方 "Drink"；不再留白名单。
KNOWN_NOOP = set()


def main():
    data = json.loads(DATA.read_text(encoding="utf-8"))
    errors = []
    for npc in data.get("npcs", []):
        permanent = npc.get("permanent") or {}
        tags = permanent.get("positiveTags") or []
        items = permanent.get("positiveItemTypeIds") or []
        live = [t for t in tags if t in OFFICIAL_TAGS]
        for tag in tags:
            if tag not in OFFICIAL_TAGS and tag not in KNOWN_NOOP:
                errors.append("%s 的 positiveTags 含官方不存在的 Tag %r（官方单数形式见 OFFICIAL_TAGS）" % (npc.get("id"), tag))
        if npc.get("isPermanent") and not live and not items:
            errors.append("%s 没有任何能命中的喜欢礼物（有效 Tag 与 positiveItemTypeIds 都为空）" % npc.get("id"))
    if errors:
        for e in errors:
            print("FAIL:", e)
        sys.exit(1)
    print("PASS: DuckNpcGiftTagGuard（永久 NPC 的喜欢标签都是官方 Tag，且每位至少有一条能命中的喜好）")


if __name__ == "__main__":
    main()
