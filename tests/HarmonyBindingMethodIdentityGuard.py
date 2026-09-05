"""CR-2026-09-05-020: each patch class must prove its own installed methods."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "Common/Infrastructure/HarmonyBindingSelfCheck.cs").read_text(encoding="utf-8-sig")
assert "IsPatchClassApplied(type, original, harmony.Id)" in source, "Self-check must retain the expected patch class"
assert "patches[i].owner == owner" in source and "patches[i].PatchMethod == expectedMethod" in source, "Verify both owner and exact patch method"
for category in ("Prefixes", "Postfixes", "Transpilers", "Finalizers"):
    assert "ContainsPatch(patchInfo." + category in source, "Missing per-method verification for " + category
assert "return expected > 0;" in source, "Empty class must not pass vacuously"
assert "HasDynamicTargetSelector(type)" in source, "Retain explicit dynamic-target boundary"
assert "ContainsOwner(" not in source, "Owner-only checks falsely accept shared-target partial failures"
assert (ROOT / "tests/fixtures/HarmonyBindingSecondReview/run.py").is_file(), "Keep real Harmony partial-install execution regression"
print("PASS: per-class Harmony patch method identity and execution fixture")
