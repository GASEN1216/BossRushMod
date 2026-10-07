"""终章占场必须在扫描和清怪前生效；行为见 ArenaHostRemainder 执行回归。"""
from pathlib import Path
import re
from ArchitectureStructureGuard import extract_method_body
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs"


def check():
    source = clean_source(SOURCE.read_text(encoding="utf-8-sig"))
    clear = re.sub(r"\s+", " ", extract_method_body(source, "internal void ClearEnemiesForBossRush()"))
    continuous = re.sub(r"\s+", " ", extract_method_body(source, "internal IEnumerator ContinuousClearEnemiesUntilWaveStart()"))
    failures = []
    if not clear.startswith("{ if (owner.IsCampaignFinalBossActive) return;"):
        failures.append("直接清场必须先检查终章占场，覆盖工厂在途窗口")
    if "if (!owner.IsCampaignFinalBossActive) RefreshCharacterCache();" not in continuous:
        failures.append("协程初始扫描必须避开终章占场")
    pause = (r"while \([^{}]+\) \{ if \(owner\.IsCampaignFinalBossActive\) \{ "
             r"yield return owner\.ArenaSharedWait05s; continue; \} loopCount\+\+;")
    if not re.search(pause, continuous):
        failures.append("持续清场必须先等待并重试，终章期间不得扫描或消耗循环预算")
    return failures


if __name__ == "__main__":
    failures = check()
    for failure in failures:
        print("CampaignArenaClearIsolationGuard: " + failure)
    print("CampaignArenaClearIsolationGuard: " + ("FAIL" if failures else "PASS"))
    raise SystemExit(bool(failures))
