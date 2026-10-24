"""Mutate independent copies of production rules, never the shared workspace."""
from pathlib import Path
import hashlib
import importlib.util
import json
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/FieldAdventureRules-negative"
SOURCES = {
    "mimic": ROOT / "Integration/FieldAdventures/Mimic/GreedyMimicRules.cs",
    "parcel": ROOT / "Integration/FieldAdventures/Delivery/OddParcelRules.cs",
    "prototype": ROOT / "Integration/FieldAdventures/Prototypes/DangerousPrototypeRules.cs",
    "inventory": ROOT / "Integration/FieldAdventures/Mimic/GreedyMimicInventory.cs",
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("field_adventure_regression_runner", ROOT / "tools/run_runtime_regressions.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    hashes = {name: digest(path) for name, path in SOURCES.items()}
    code, output = runner.run_project_fixture(HERE / "FieldAdventureRules.csproj", OUT / "baseline", ROOT)
    (OUT / "baseline.log").write_text(output, encoding="utf-8")
    if code:
        print("FAIL: baseline must pass before behavioral mutations")
        print(output[-6000:])
        return 1

    probes = (
        ("fourth_meal_allowed", "mimic", "Meals < MaximumMeals", "Meals <= MaximumMeals",
         "fourth meal is rejected for every food type"),
        ("unlearned_attack", "mimic", "if (meals[i] > 0 && chosen-- == 0)", "if (chosen-- == 0)",
         "mimic only attacks with the foods the player actually fed"),
        ("overheal", "mimic", "Math.Min(MaximumHealth - currentHealth,", "Math.Min(MaximumHealth,",
         "full health mimic cannot heal further"),
        ("ground_increases_pressure", "parcel", "!carried ? -36f", "!carried ? 36f",
         "putting a parcel down relieves pressure without damaging it"),
        ("failed_payment_consumes_claim", "parcel", "if (!addMoney(Reward)) return false;", "addMoney(Reward);",
         "payment failure does not mark a parcel paid"),
        ("holstered_charging", "prototype", "if (held) Charge =", "Charge =",
         "holstered prototype does not recharge"),
        ("direct_hits_pass_ricochet", "prototype",
         "if (!Active || (Kind == DangerousPrototypeKind.RicochetSun && !reflected)) return false;",
         "if (!Active) return false;", "direct beam damage never advances the ricochet challenge"),
        ("cancel_resets_reward_ledger", "prototype", "internal void Cancel() { Active = false;",
         "internal void Cancel() { claimedMask = 0; Active = false;",
         "cancelling another lease preserves already claimed quota"),
        ("quest_items_eligible", "inventory", '|| item.Tags.Contains("Quest")', '',
         "quest tags are protected from mimic feeding"),
        ("rollback_abandons_original", "inventory", "item.Drop(player, true);", "return;",
         "failed AddAt rollback must drop the same original instead of leaving it ownerless"),
        ("destroy_delivered_reward", "inventory", "finally { SkyIslandInventoryTransaction.DestroyUnowned(reward); }",
         "finally { if (reward != null) reward.DestroyTree(); }",
         "successfully dropped rewards retain their ground owner and are never destroyed"),
    )
    source_texts = {name: path.read_text(encoding="utf-8") for name, path in SOURCES.items()}
    results = {}
    for name, target, original, replacement, expected in probes:
        if source_texts[target].count(original) != 1:
            raise AssertionError(name + ": mutation anchor is not unique: " + original)
        copy_root = Path(tempfile.mkdtemp(prefix=name + "-", dir=OUT))
        for source_name, source_path in SOURCES.items():
            copied = copy_root / source_path.relative_to(ROOT)
            copied.parent.mkdir(parents=True, exist_ok=True)
            text = source_texts[source_name]
            if source_name == target:
                text = text.replace(original, replacement)
            copied.write_text(text, encoding="utf-8")
        copied_fixture = copy_root / HERE.relative_to(ROOT)
        copied_fixture.mkdir(parents=True)
        for filename in ("Program.cs", "InventoryCases.cs", "InventoryStubs.cs", "FieldAdventureRules.csproj"):
            (copied_fixture / filename).write_bytes((HERE / filename).read_bytes())
        code, output = runner.run_project_fixture(
            copied_fixture / "FieldAdventureRules.csproj", copy_root / "execution", copy_root)
        (copy_root / "probe.log").write_text(output, encoding="utf-8")
        passed = code != 0 and "Unhandled exception." in output and expected in output
        results[name] = {"passed": passed, "exit_code": code, "expected_assertion": expected,
                         "copy_root": str(copy_root)}
        print(("PASS " if passed else "FAIL ") + name + ": " + expected, flush=True)
        if not passed:
            print(output[-6000:])

    unchanged = all(digest(path) == hashes[name] for name, path in SOURCES.items())
    results["workspace_production_unchanged"] = unchanged
    results["production_sha256"] = hashes
    (OUT / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(("PASS" if unchanged else "FAIL") + ": all workspace production SHA-256 values unchanged")
    return 0 if unchanged and all(results[name]["passed"] for name, *_ in probes) else 1


if __name__ == "__main__":
    sys.exit(main())
