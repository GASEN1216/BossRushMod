"""Guard: LootBlacklist runtime data must be JSON-first with a hardcoded fallback."""

from pathlib import Path
import json
import re
import sys


REGISTRY = Path("Config/LootBlacklistRegistry.cs")
DATA_FILE = Path("Assets/Data/LootBlacklist.json")
COMPILE = Path("compile_official.bat")

CONSTANT_VALUES = {
    # 2026-09 新增内容的九件物品（遗种巢 / 词缀锻造 / 图鉴 / 后山种子与出击餐）。
    # 它们都带 Special tag、品质 4-5，而日报签到池只按掉落黑名单过滤，必须登记。
    "BossRushItemIds.RelicEgg": 500059,
    "BossRushItemIds.AffixForgeStone": 500060,
    "BossRushItemIds.CodexBook": 500061,
    "BossRushItemIds.DragonSeed": 500062,
    "BossRushItemIds.EmberSeed": 500063,
    "BossRushItemIds.PhantomSpore": 500064,
    "BossRushItemIds.DragonFruit": 500065,
    "BossRushItemIds.EmberChili": 500066,
    "BossRushItemIds.PhantomMushroom": 500067,
    # 天空岛物品（纪念品、风标罗盘、岛上特产）：只从剧情节点与岛上箱子里出，不进任何随机奖池。
    "BossRushItemIds.SkyIslandHomecomingBadge": 500068,
    "BossRushItemIds.SkyIslandWindeaterCore": 500069,
    "BossRushItemIds.SkyIslandWindVaneCompass": 500070,
    "BossRushItemIds.SkyIslandHomecomingBento": 500071,
    "BossRushItemIds.SkyIslandStarmossSalve": 500072,
    # 天空岛批次三（采集材料、晴岚风晶、局内耗材）：只从岛上的采集点与合成台出。
    "BossRushItemIds.SkyIslandCloudmossFiber": 500073,
    "BossRushItemIds.SkyIslandGreenearSheaf": 500074,
    "BossRushItemIds.SkyIslandDriftwood": 500075,
    "BossRushItemIds.SkyIslandBrassScrap": 500076,
    "BossRushItemIds.SkyIslandWindcrystalShard": 500077,
    "BossRushItemIds.SkyIslandStardust": 500078,
    "BossRushItemIds.SkyIslandQinglanWindcrystal": 500079,
    "BossRushItemIds.SkyIslandWindLantern": 500080,
    "BossRushItemIds.SkyIslandWindwardIncense": 500081,
    "BossRushItemIds.SkyIslandQinglanCharm": 500082,
    "DragonDescendantConfig.DRAGON_HELM_TYPE_ID": 500003,
    "DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID": 500004,
    "DragonBreathConfig.WEAPON_TYPE_ID": 500005,
    "FlightConfig.TotemTypeIdBase": 500010,
    "DragonKingConfig.DRAGON_KING_HELM_TYPE_ID": 500011,
    "DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID": 500012,
    "ReverseScaleConfig.TotemTypeId": 500013,
    "DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID": 500034,
    "DragonKingBossGunConfig.WeaponTypeId": 500035,
    "FrostmourneIds.WeaponTypeId": 500041,
    "PhantomWitchScytheIds.WeaponTypeId": 500044,
    "NewWeaponIds.ViperDaggerTypeId": 500048,
    "NewWeaponIds.SummonStaffTypeId": 500049,
    "NewWeaponIds.EnergyShieldTypeId": 500050,
    "NewWeaponIds.FrostSpearTypeId": 500051,
    "NewWeaponIds.ThunderRingTypeId": 500052,
    "FactionFlagConfig.RANDOM_FLAG_TYPE_ID": 500020,
    "FactionFlagConfig.SCAV_FLAG_TYPE_ID": 500021,
    "FactionFlagConfig.USEC_FLAG_TYPE_ID": 500022,
    "FactionFlagConfig.BEAR_FLAG_TYPE_ID": 500023,
    "FactionFlagConfig.LAB_FLAG_TYPE_ID": 500024,
    "FactionFlagConfig.WOLF_FLAG_TYPE_ID": 500025,
    "FactionFlagConfig.PLAYER_FLAG_TYPE_ID": 500026,
    "RespawnItemConfig.TAUNT_SMOKE_TYPE_ID": 500027,
    "RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID": 500028,
    "RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID": 500032,
    "RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID": 500033,
    "BloodhuntTransponderConfig.TYPE_ID": 500036,
    "FoldableCoverPackConfig.TYPE_ID": 500037,
    "ReinforcedRoadblockPackConfig.TYPE_ID": 500038,
    "BarbedWirePackConfig.TYPE_ID": 500039,
    "EmergencyRepairSprayConfig.TYPE_ID": 500040,
    "DingdangDrawingConfig.TYPE_ID": 500016,
    "ZombieTideInvitationConfig.TYPE_ID": 500045,
    "ZombieTideBeaconConfig.TYPE_ID": 500046,
    "PortableSafeZoneDeviceConfig.TYPE_ID": 500058,
}


def fail(message: str) -> int:
    print("LootBlacklistDataRegistryGuard: FAIL - " + message)
    return 1


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//.*", "", text)


def parse_fallback_ids(source: str):
    match = re.search(r"return\s+new\s+int\[\]\s*\{(?P<body>.*?)\};", source, re.S)
    if not match:
        raise ValueError("fallback int array not found")

    body = strip_comments(match.group("body"))
    ids = []
    for raw_token in body.split(","):
        token = raw_token.strip()
        if not token:
            continue
        if re.fullmatch(r"-?\d+", token):
            ids.append(int(token))
            continue
        if token not in CONSTANT_VALUES:
            raise ValueError(f"unknown fallback constant: {token}")
        ids.append(CONSTANT_VALUES[token])
    return ids


def main() -> int:
    if not DATA_FILE.exists():
        return fail("Assets/Data/LootBlacklist.json is missing")

    source = REGISTRY.read_text(encoding="utf-8")
    required_snippets = [
        'private const string DataFileName = "LootBlacklist.json";',
        "JsonDataRegistry.TryReadDataFile(DataFileName, out json)",
        "LoadJsonBlacklistIds()",
        "CreateFallbackBlacklistIds()",
        "ParseItemIds(string json)",
    ]
    for snippet in required_snippets:
        if snippet not in source:
            return fail("registry missing JSON-first snippet: " + snippet)

    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    if "Common\\Data\\JsonDataRegistry.cs" not in compile_text:
        return fail("compile_official.bat does not compile JsonDataRegistry.cs")

    try:
        data = json.loads(DATA_FILE.read_text(encoding="utf-8"))
    except Exception as exc:
        return fail("LootBlacklist.json is not valid JSON: " + str(exc))

    json_ids = data.get("itemIds")
    if not isinstance(json_ids, list) or not all(isinstance(value, int) for value in json_ids):
        return fail("LootBlacklist.json itemIds must be an integer array")

    try:
        fallback_ids = parse_fallback_ids(source)
    except ValueError as exc:
        return fail(str(exc))

    if json_ids != fallback_ids:
        return fail("LootBlacklist.json itemIds do not match hardcoded fallback ids")

    if len(json_ids) != len(set(json_ids)):
        return fail("LootBlacklist.json contains duplicate itemIds")

    print("LootBlacklistDataRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
