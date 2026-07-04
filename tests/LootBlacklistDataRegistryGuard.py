"""Guard: LootBlacklist runtime data must be JSON-first with a hardcoded fallback."""

from pathlib import Path
import json
import re
import sys


REGISTRY = Path("Config/LootBlacklistRegistry.cs")
DATA_FILE = Path("Assets/Data/LootBlacklist.json")
COMPILE = Path("compile_official.bat")

CONSTANT_VALUES = {
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
