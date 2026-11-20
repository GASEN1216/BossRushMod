"""Compile the production registration and item-load hooks with an isolated game host."""
from collections import defaultdict
from pathlib import Path
import hashlib
import json
import re
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/dynamic-item-initialization"
REGISTRY = "Integration/BossRushDynamicItemRegistry.cs"
PATCH = "Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs"
sys.path[:0] = [str(ROOT / "tests"), str(ROOT / "tools")]
from cs_source_util import clean_source
from TypeIdLedgerGuard import cross_check_agents, parse_ledger
from item_bundle_inventory import bundle_sources, const_string, one
from run_runtime_regressions import run_project_fixture

ITEM_CONFIGS = (
    "AwenLootSweepTokenConfig", "BarbedWirePackConfig", "BloodhuntTransponderConfig",
    "BrickStoneConfig", "CalmingDropsConfig", "DiamondConfig", "DiamondRingConfig",
    "DingdangDrawingConfig", "EmergencyRepairSprayConfig", "FateEchoRelicConfig",
    "FoldableCoverPackConfig", "PeaceCharmConfig", "PortableSafeZoneDeviceConfig",
    "ReinforcedRoadblockPackConfig", "WildHornConfig", "ZombieTideBeaconConfig",
    "ZombieTideInvitationConfig",
)
DECLARATIONS = {
    **{name: [f"Integration/Items/{name}.cs"] for name in ITEM_CONFIGS},
    "AchievementMedalConfig": ["Achievement/AchievementMedalConfig.cs"],
    "AwenCourierTokenConfig": ["Integration/Items/AwenDepositTokenConfig.cs"],
    "ColdQuenchFluidConfig": ["Integration/Reforge/ColdQuenchFluidConfig.cs"],
    "DragonBreathConfig": ["Integration/DragonDescendant/DragonBreathConfig.cs"],
    "DragonDescendantConfig": ["Integration/DragonDescendant/DragonDescendantConfig.cs"],
    "DragonKingConfig": ["Integration/DragonKing/DragonKingConfig.cs"],
    "DragonKingBossGunConfig": ["Integration/DragonKing/Weapons/DragonKingBossGunConfig.cs"],
    "FactionFlagConfig": ["ModeE/FactionFlagConfig.cs"],
    "FenHuangHalberdIds": ["Integration/DragonKing/Weapons/FenHuangHalberdIds.cs"],
    "FlightConfig": ["Integration/FlightTotem/FlightConfig.cs"],
    "FrostmourneIds": ["Integration/Frostmourne/FrostmourneIds.cs"],
    "NewWeaponIds": ["Integration/NewWeapons/Common/NewWeaponIds.cs"],
    "PhantomWitchConfig": ["Integration/PhantomWitch/PhantomWitchConfig.cs"],
    "PhantomWitchScytheIds": ["Integration/PhantomWitch/PhantomWitchScytheIds.cs"],
    "RespawnItemConfig": ["ModeE/RespawnItemConfig.cs"],
    "SkyIslandBossRules": ["SkyIsland/SkyIslandBossRules.cs"],
    "BossRushItemIds": [
        "Config/ConfigItemIds.cs", "Config/ConfigAffixForge.cs", "Config/ConfigCodex.cs",
        "Config/ConfigPetNest.cs", "Integration/BackMountain/BackMountainItems.cs",
    ],
}


def declarations(registry, read):
    """Keep the fixture's TypeID and bundle values on the same declarations as production."""
    reference = re.compile(r"\b(" + "|".join(DECLARATIONS) + r")\.(\w+)")
    fields = defaultdict(dict)

    def copy(owner, name):
        if name in fields[owner]:
            return
        field = re.compile(
            rf"\b(?:const\s+(?:int|string)|static\s+readonly\s+int\[\])\s+{name}\s*=\s*([^;]+);"
        )
        matches = [
            match
            for path in DECLARATIONS[owner]
            for match in field.finditer(clean_source(read(path)))
        ]
        # Plan delegates reference runtime methods on some of these same provider classes.
        if not matches:
            return
        if len(matches) != 1:
            raise ValueError(f"Ambiguous production field {owner}.{name}")
        definition = matches[0]
        fields[owner][name] = "internal " + definition.group()
        for dependency, member in reference.findall(definition.group(1)):
            copy(dependency, member)
        if owner == "FactionFlagConfig" and name == "ALL_FLAG_TYPE_IDS":
            for member in re.findall(r"\b\w+_FLAG_TYPE_ID\b", definition.group(1)):
                copy(owner, member)

    for owner, field_name in reference.findall(clean_source(registry)):
        copy(owner, field_name)
    # Historical Buff IDs share the ledger's number range without being item registrations.
    copy("DragonBreathConfig", "BUFF_ID")
    copy("PhantomWitchConfig", "CurseBuffID")
    return "\n".join(
        "internal static partial class " + owner + " {\n" + "\n".join(members.values()) + "\n}"
        for owner, members in fields.items() if members
    )


def bundle_source_contract(read):
    equipment, items, special = bundle_sources(read)
    model = read("Integration/EclipseBloom/ResidualSunShieldModel.cs")
    normalize = one(r"(?m)private static string ExtractBaseName\(string prefabName\)\s*\{[\s\S]+?^        \}",
                    clean_source(read("Integration/EquipmentFactory.cs")), "equipment model keys")
    read("tools/resource_release_manifest.json")
    inventory = json.loads(read("tools/item_bundle_inventory.json"))["bundles"]
    for relative, bundle in inventory.items():
        path = (ROOT / relative).resolve()
        if not path.is_relative_to(ROOT / "Assets"):
            raise ValueError(f"Invalid item bundle inventory path: {relative}")
        if path.is_file():
            with path.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            if digest != bundle["sha256"]:
                raise ValueError(f"{relative}: local item bundle SHA-256 differs from the prefab inventory")
    special_entries = ",\n".join("{ " + json.dumps(key) + ", " + json.dumps(path) + " }" for key, path in special.items())
    return (
        "using System;\nusing System.Collections.Generic;\ninternal static partial class BundleSourceAudit {\n"
        f"private const string EquipmentRoot = {json.dumps(equipment)};\n"
        f"private const string ItemRoot = {json.dumps(items)};\n"
        f"private const string ShieldModelBundle = {json.dumps(const_string(model, 'BundleName'))};\n"
        f"private const string ShieldModelKey = {json.dumps(const_string(model, 'ModelBaseName'))};\n"
        "private static readonly Dictionary<string, string> SpecialPaths = new Dictionary<string, string> {\n"
        + special_entries + "\n};\n" + normalize + "\n}\n"
    )


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    inputs = {}

    def read(path):
        data = (ROOT / path).read_bytes()
        inputs[path] = hashlib.sha256(data).hexdigest()
        return data.decode("utf-8-sig")

    source = read(REGISTRY)
    registry, separator, _ = source.partition("    internal sealed partial class IntegrationRuntimeModule")
    if not separator:
        raise ValueError("Cannot find the registry's runtime host boundary")
    read(PATCH)
    bounds, holes, error = parse_ledger()
    if error:
        raise ValueError(error)
    low, high = bounds
    error = cross_check_agents(low, high, holes)
    if error:
        raise ValueError(error)
    for path in ("docs/contracts.md", "AGENTS.md"):
        read(path)
    expected = ", ".join(str(type_id) for type_id in range(low, high + 1) if type_id not in holes)
    body = declarations(registry, read)
    (OUT / "Production.cs").write_text(
        registry + body + f"\ninternal static class Ledger {{ internal static readonly int[] Ids = {{ {expected} }}; }}\n}}",
        encoding="utf-8",
    )
    (OUT / "BundleSourceContract.cs").write_text(bundle_source_contract(read), encoding="utf-8")
    files = [OUT / "Production.cs", OUT / "BundleSourceContract.cs", ROOT / PATCH, HERE / "Program.cs", HERE / "Host.cs",
             HERE / "BundleSourceAudit.cs"]
    project = OUT / "Probe.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in files)
        + '</ItemGroup></Project>',
        encoding="utf-8",
    )
    (OUT / "source-sha256.json").write_text(json.dumps(inputs, indent=2), encoding="utf-8")
    code, output = run_project_fixture(project, OUT / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
