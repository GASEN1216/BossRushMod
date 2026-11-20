"""Rebuild the published item bundle inventory from exported Unity prefab roots."""

import argparse
import hashlib
import json
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]
SNAPSHOT = ROOT / "tools/item_bundle_inventory.json"
MANIFEST = ROOT / "tools/resource_release_manifest.json"

SPECIAL_LOADERS = {
    "EnsureBossRushTicket": "Integration/BossRushDynamicItemRegistry.cs",
    "EnsureBirthdayCake": "Integration/BossRushIntegrationRuntimeModule_BirthdayCake.cs",
    "EnsureAdventureJournal": "Integration/BossRushIntegrationRuntimeModule_WikiBook.cs",
}


def one(pattern, source, context):
    matches = re.findall(pattern, source)
    if len(matches) != 1:
        raise ValueError(f"{context}: expected one match for {pattern!r}, found {len(matches)}")
    return matches[0]


def const_string(source, name):
    return one(rf'\bconst string {re.escape(name)}\s*=\s*"([^"\n]+)"\s*;', source, name)


def bundle_sources(read):
    """Derive the factory directories and special paths from the actual loaders."""
    equipment = const_string(read("Integration/EquipmentFactory.cs"), "EQUIPMENT_PATH")
    items = const_string(read("Integration/ItemFactory.cs"), "ITEMS_PATH")
    special = {}
    for method, path in SPECIAL_LOADERS.items():
        source = read(path)
        arguments = one(r"\bstring bundlePath\s*=\s*Path\.Combine\(modDir, ([^;\n]+)\);", source, path)
        parts = []
        for argument in arguments.split(","):
            token = argument.strip()
            if re.fullmatch(r'"[a-zA-Z_/-]+"', token):
                parts.append(token[1:-1])
            elif token.isidentifier():
                parts.append(const_string(source, token))
            else:
                raise ValueError(f"{path}: unsupported bundle path expression {token!r}")
        special[method] = "/".join(parts)
    return equipment, items, special


def published_item_bundle_paths(manifest):
    equipment, items, special = bundle_sources(lambda path: (ROOT / path).read_text(encoding="utf-8-sig"))
    listed = set(manifest["bundles"])
    missing = set(special.values()) - listed
    if missing:
        raise ValueError("Special item bundles missing from release manifest: " + ", ".join(sorted(missing)))
    return {path for path in listed if path.startswith((equipment + "/", items + "/"))} | set(special.values())


def inspect_prefab_roots(environment):
    """Use only exported roots that the factories see, excluding nested Item and Model objects."""
    items = set()
    models = set()
    for pointer in environment.container.values():
        reader = pointer.deref()
        if reader.type.name != "GameObject":
            continue
        root = reader.read()
        has_item = False
        has_agent = False
        for pair in root.m_Component:
            component = pair.component.deref()
            if component.type.name != "MonoBehaviour":
                continue
            script = component.read().m_Script.deref().read()
            if script.m_Namespace == "ItemStatsSystem" and script.m_ClassName == "Item":
                items.add((component.read_typetree()["typeID"], root.m_Name))
                has_item = True
            if script.m_ClassName in ("ItemAgent", "DuckovItemAgent"):
                has_agent = True
        if not has_item and (has_agent or root.m_Name.lower().endswith("_model")):
            models.add(root.m_Name)
    return {"items": [list(item) for item in sorted(items)], "models": sorted(models)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Root containing the final release bundles")
    parser.add_argument("--output", type=Path, default=SNAPSHOT, help="Path for the generated inventory JSON")
    args = parser.parse_args()
    import UnityPy

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    bundles = {}
    for relative in sorted(published_item_bundle_paths(manifest)):
        path = args.root / relative
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        bundles[relative] = {"sha256": digest, **inspect_prefab_roots(UnityPy.load(str(path)))}
    args.output.write_text(json.dumps({"schema": 1, "bundles": bundles}, indent=2, ensure_ascii=False) + "\n",
                           encoding="utf-8", newline="\n")
    print(f"Item bundle inventory: {len(bundles)} release paths")


if __name__ == "__main__":
    main()
