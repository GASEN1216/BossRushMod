"""Legacy wave timers and countdown state belong to the registered arena module."""

from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
HOST = clean_source((ROOT / "ModBehaviour.cs").read_text(encoding="utf-8-sig"))
BRIDGE = clean_source((ROOT / "WavesArena/WavesArenaRuntimeHooks.cs").read_text(encoding="utf-8-sig"))
LOOT_BRIDGE = clean_source((ROOT / "LootAndRewards/LootAndRewards.cs").read_text(encoding="utf-8-sig"))
MODULE = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule.cs").read_text(encoding="utf-8-sig"))
REGISTRATION = clean_source((ROOT / "ModBehaviourRuntimeModules.cs").read_text(encoding="utf-8-sig"))


def main():
    if "wavesArenaRuntime = new WavesArenaRuntimeModule();" not in REGISTRATION:
        raise AssertionError("arena module instance must be created once")
    if "runtimeModuleHost.Register(wavesArenaRuntime);" not in REGISTRATION:
        raise AssertionError("registered arena module must be the stored instance")
    for old_name, new_name in (
        ("waitingForNextWave", "WaitingForNextWave"),
        ("waveCountdown", "WaveCountdown"),
        ("lastWaveCountdownSeconds", "LastWaveCountdownSeconds"),
        ("waveIntegrityCheckTimer", "WaveIntegrityCheckTimer"),
        ("daXingXingCleanTimer", "DaXingXingCleanTimer"),
        ("totalEnemies", "TotalEnemies"),
        ("defeatedEnemies", "DefeatedEnemies"),
        ("nextWaveBossName", "NextWaveBossName"),
        ("bossesPerWave", "BossesPerWave"),
        ("bossesInCurrentWaveTotal", "BossesInCurrentWaveTotal"),
        ("bossesInCurrentWaveRemaining", "BossesInCurrentWaveRemaining"),
    ):
        if new_name + " { get; set; }" not in MODULE:
            raise AssertionError(new_name + " state must be owned by WavesArenaRuntimeModule")
        if ("get { return wavesArenaRuntime." + new_name + "; }") not in BRIDGE:
            raise AssertionError(old_name + " getter must forward to the arena owner")
        if ("set { wavesArenaRuntime." + new_name + " = value; }") not in BRIDGE:
            raise AssertionError(old_name + " setter must forward to the arena owner")
        if re.search(r"^\s*private\s+(?:readonly\s+)?[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*(?:=|;)", HOST, re.M):
            raise AssertionError(old_name + " must not remain a host field")
    if "LastWaveCountdownSeconds { get; set; } = -1;" not in MODULE:
        raise AssertionError("initial countdown marker must stay negative")
    if "BossesPerWave { get; set; } = 1;" not in MODULE:
        raise AssertionError("default wave size must remain one")
    if "CurrentWaveBosses =" not in MODULE or "new System.Collections.Generic.List<UnityEngine.MonoBehaviour>()" not in MODULE:
        raise AssertionError("the module must own the mutable current wave boss list")
    if "get { return wavesArenaRuntime.CurrentWaveBosses; }" not in BRIDGE:
        raise AssertionError("legacy boss list reads must forward to the module")
    if "private readonly List<MonoBehaviour> currentWaveBosses" in HOST:
        raise AssertionError("the current wave boss list must not remain a host field")
    for old_name, new_name in (
        ("infiniteHellMode", "InfiniteHellMode"),
        ("infiniteHellWaveIndex", "InfiniteHellWaveIndex"),
        ("infiniteHellCashPool", "InfiniteHellCashPool"),
        ("infiniteHellMilestoneRewardTier", "InfiniteHellMilestoneRewardTier"),
        ("infiniteHellWaveCashThisWave", "InfiniteHellWaveCashThisWave"),
        ("infiniteHellHighQualityItemPoolInitialized", "InfiniteHellHighQualityItemPoolInitialized"),
    ):
        if new_name + " { get; set; }" not in MODULE:
            raise AssertionError(new_name + " must be stored in the arena module")
        if "get { return wavesArenaRuntime." + new_name + "; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " loot bridge getter is missing")
        if "set { wavesArenaRuntime." + new_name + " = value; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " loot bridge setter is missing")
        if re.search(r"^\s*private\s+[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*(?:=|;)", LOOT_BRIDGE, re.M):
            raise AssertionError(old_name + " must not remain a host field")
    for old_name, new_name in (
        ("infiniteHellHighQualityItemPool", "InfiniteHellHighQualityItemPool"),
        ("infiniteHellHighQualityCandidateIdScratch", "InfiniteHellHighQualityCandidateIdScratch"),
        ("infiniteHellHighQualityPreferredScratch", "InfiniteHellHighQualityPreferredScratch"),
        ("infiniteHellHighQualityFallbackScratch", "InfiniteHellHighQualityFallbackScratch"),
    ):
        if new_name + " =" not in MODULE:
            raise AssertionError(new_name + " collection must be owned by the arena module")
        if "get { return wavesArenaRuntime." + new_name + "; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " collection bridge is missing")
        if re.search(r"^\s*private\s+readonly\s+[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*=", LOOT_BRIDGE, re.M):
            raise AssertionError(old_name + " must not remain a host collection")
    print("WavesArenaStateOwnershipGuard: PASS")


if __name__ == "__main__":
    main()
