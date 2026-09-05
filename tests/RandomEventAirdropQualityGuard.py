"""CR-2026-09-05-017: constrain the pool actually consumed by LootBoxLoader."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "RandomEvents/RandomEventEffectsBridge_Loot.cs").read_text(encoding="utf-8-sig")
pool = source.split("private bool FillRandomEventAirdropPool(", 1)[1].split("internal IEnumerator RandomEventAirdropDropRoutine", 1)[0]
assert "FillRandomEventAirdropPool(loader, qMin, qMax)" in source, "Pass normalized quality bounds to the consumed pool"
assert "ItemAssetsCollection.GetMetaData(id)" in pool, "Filter actual item metadata, not unused loader.qualities"
assert "metadata.quality < qMin || metadata.quality > qMax" in pool, "Both quality bounds must filter candidates"
assert "IsItemBlacklisted(id)" in pool, "Keep the common item blacklist"
assert "1f / bucket.Count" in pool, "Each nonempty quality must carry equal total weight"
assert pool.index("entriesList.Clear();") < pool.index("BuildGeneralBossLootCandidateIdSet();"), "Empty candidates must not retain template rewards"
assert source.index("if (!hasRandomPool)") < source.index("loader.StartSetup();"), "Do not start setup with an invalid pool"
assert (ROOT / "tests/fixtures/AirdropSecondReview/run.py").is_file(), "Keep executable quality/blacklist/weight/failure regressions"
print("PASS: actual airdrop pool quality contract and execution fixture")
