"""Validate production authored-asset leases by breaking only isolated copies."""
from pathlib import Path
import hashlib
import importlib.util
import json
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/EclipseBloomAssets-negative"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("eclipse_asset_fixture", HERE / "run.py")
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    hashes = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in fixture.ASSET_PRODUCTION}
    code, output = fixture.run_asset_fixture(output_root=OUT / "baseline")
    (OUT / "baseline.log").write_text(output, encoding="utf-8")
    if code:
        print("FAIL authored lease baseline")
        print(output[-5000:])
        return 1
    assets = "Integration/EclipseBloom/EclipseBloomAuthoredAssets.cs"
    manager = "Integration/EclipseBloom/EclipseBloomAssetManager.cs"
    visuals = "Integration/EclipseBloom/EclipseBloomVisuals.cs"
    shield = "Integration/EclipseBloom/ResidualSunShieldModel.cs"
    icon = "Integration/EclipseBloom/ResidualSunShieldIcon.cs"
    factory = "Integration/EquipmentFactory_RuntimeModels.cs"
    probes = (
        ("lease_does_not_accumulate", assets, "references++;", "references = 1;",
         "concurrent flower instances borrow one loaded authored bundle with two leases"),
        ("first_owner_unloads_everyone", assets, "if (references == 0) ResetStaticCaches();", "ResetStaticCaches();",
         "disposing one flower only destroys its dynamic mesh and keeps other borrowed assets alive"),
        ("last_lease_leaks_loaded_objects", assets, "bundle.Unload(true);", "bundle.Unload(false);",
         "last flower lease unloads all authored assets exactly once after dynamic resources are disposed"),
        ("failed_load_retains_partial_assets", assets, "ResetStaticCaches();\n                    throw;", "throw;",
         "partial authored load must unload the incomplete bundle"),
        ("unsupported_shader_accepted", assets, "|| !material.shader.isSupported", "",
         "invalid authored resource must fail the real Acquire path: eclipse_authored_material_missing_obsidian"),
        ("degenerate_mesh_accepted", assets, "|| mesh.vertexCount < 4", "",
         "invalid authored resource must fail the real Acquire path: eclipse_authored_mesh_missing_petal"),
        ("manager_forgets_authored_release", manager, "if (_authoredLease) EclipseBloomAuthoredAssets.Release();", "",
         "disposing one flower only destroys its dynamic mesh and keeps other borrowed assets alive"),
        ("manager_destroys_borrowed_petal", manager, "Petal = EclipseBloomAuthoredAssets.Petal;",
         "Petal = EclipseBloomAuthoredAssets.Petal; _owned.Add(Petal);",
         "disposing one flower only destroys its dynamic mesh and keeps other borrowed assets alive"),
        ("disabled_warning_allows_damage", visuals, "|| !_fills[sector].enabled", "",
         "disabled ground fill renderer cannot permit invisible damage"),
        ("fog_hidden_warning_allows_damage", visuals, "|| _fills[sector].forceRenderingOff", "",
         "fog forced off ground fill renderer cannot permit invisible damage"),
        ("hidden_warning_parent_allows_damage", visuals, "!_sectors[sector].activeInHierarchy", "!_sectors[sector].activeSelf",
         "inactive warning ancestor cannot permit invisible damage"),
        ("shield_pickup_reverts_to_sprite", shield, "item.useSpriteForPickup = false;", "item.useSpriteForPickup = true;",
         "real shield bind and factory registration connect the native pickup graphic and equipment model before disabling sprite pickup"),
        ("shield_template_missing_ground_accepted", shield, '|| model.transform.Find("GroundPoint") == null', "",
         "shield template without the authored GroundPoint is rejected before caching or icon attachment"),
        ("shield_template_missing_part_accepted", shield, "|| model.GetComponentsInChildren<MeshFilter>(true).Length != 6", "",
         "shield template missing an authored mesh part is rejected before caching or icon attachment"),
        ("shield_reset_destroys_factory_wrapper", shield, "template = null;", "if (template != null) UnityEngine.Object.Destroy(template.gameObject); template = null;",
         "shield cache reset cannot destroy the factory wrapper still borrowed by existing item agents"),
        ("owned_asset_ignores_bundle_identity", factory,
         "bundle != null && string.Equals(bundle.name, bundleName, System.StringComparison.OrdinalIgnoreCase)", "bundle != null",
         "factory lookup borrows the exact asset from its named owned bundle without acquiring a new bundle"),
        ("owned_asset_query_unloads_factory_bundle", factory,
         "return bundle.LoadAsset<T>(assetPath);", "{ T result = bundle.LoadAsset<T>(assetPath); bundle.Unload(true); return result; }",
         "repeated factory asset queries share the existing resource without unloading its owner"),
        ("shield_icon_ignores_dimensions", icon,
         "|| loaded.texture.width != Size || loaded.texture.height != Size", "",
         "real shield icon rejects incorrect production dimensions without destroying the borrowed resource"),
        ("shield_icon_destroys_borrowed_sprite", icon,
         "private void OnDestroy() { sprite = null; }", "private void OnDestroy() { UnityEngine.Object.Destroy(sprite); sprite = null; }",
         "shield icon component destruction cannot destroy the Sprite texture or equipment factory bundle it borrowed"),
    )
    results = {}
    for name, target, original, replacement, expected in probes:
        copy_root = Path(tempfile.mkdtemp(prefix=name + "-", dir=OUT))
        for source in fixture.ASSET_PRODUCTION:
            path = copy_root / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((ROOT / source).read_bytes())
        path = copy_root / target
        value = path.read_text(encoding="utf-8-sig")
        if value.count(original) != 1:
            raise AssertionError(name + ": mutation anchor must occur exactly once")
        path.write_text(value.replace(original, replacement), encoding="utf-8")
        code, output = fixture.run_asset_fixture(source_root=copy_root, output_root=copy_root / "build")
        (copy_root / "probe.log").write_text(output, encoding="utf-8")
        passed = code != 0 and "InvalidOperationException" in output and expected in output
        results[name] = {"passed": passed, "exit_code": code, "expected_assertion": expected, "copy_root": str(copy_root)}
        print(("PASS " if passed else "FAIL ") + name + ": " + expected, flush=True)
        if not passed:
            print(output[-5000:])
    unchanged = all(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest for name, digest in hashes.items())
    results["workspace_production_unchanged"] = unchanged
    results["production_sha256"] = hashes
    (OUT / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(("PASS" if unchanged else "FAIL") + ": workspace production SHA-256 unchanged")
    return 0 if unchanged and all(value["passed"] for value in results.values() if isinstance(value, dict) and "passed" in value) else 1


if __name__ == "__main__":
    raise SystemExit(main())
