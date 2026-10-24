"""Champion pool wiring and unsupported consumer boundaries; runtime fixture proves owner behavior."""
from pathlib import Path
import os
import re

from cs_source_util import clean_source

ROOT = Path(os.environ.get("BOSSRUSH_CHAMPION_SOURCE_ROOT", Path(__file__).resolve().parents[1]))
ADAPTER = "Integration/SandstormChampion/SandstormChampionPoolAdapter.cs"
FACTORY = "Integration/SandstormChampion/SandstormChampionBoss.cs"


def compact(text):
    return re.sub(r"\s+", " ", text).strip()


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(path, signature):
    text = source(path)
    assert text.count(signature) == 1, f"{path}: ambiguous member {signature}"
    opening = text.index("{", text.index(signature))
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == "{") - (text[end] == "}")
        if depth == 0:
            return compact(text[opening + 1:end])
    raise AssertionError(f"{path}: unclosed {signature}")


def main():
    checks = 0

    def require(text, statement, label):
        nonlocal checks
        assert compact(statement) in text, f"{label}: missing {statement}"
        checks += 1

    require(body(ADAPTER, "internal static void Register("),
            "owner.FindArenaEnemyPreset(SandstormChampionBoss.NameKey) != null", "catalogue idempotence")
    require(body(ADAPTER, "internal static void Register("), "baseHealth = SandstormChampionConfig.BaseHealth", "existing tuning")
    factory = body(FACTORY, "internal static async UniTask<CharacterMainControl> SpawnAsync(")
    require(factory, "if (deferActivationUntilNextFrame) boss.gameObject.SetActive(false);", "official active return")
    require(factory, "if (deferActivationUntilNextFrame) { await UniTask.Yield(); if (!IsCurrent(owner, sceneHandle, isCurrent)) throw new OperationCanceledException(); }", "true deferred activation")
    assert factory.index("if (deferActivationUntilNextFrame) {") < factory.index("controller.Initialize(") < factory.index("boss.gameObject.SetActive(true);")
    checks += 1
    adapter = body(ADAPTER, "internal static async UniTask<CharacterMainControl> SpawnAsync(")
    require(adapter, "SandstormChampionBoss.SpawnAsync(owner, position, current, deferActivationUntilNextFrame)", "defer forwarding")
    require(adapter, "if (!isNonWaveSpawn)", "nonwave isolation")
    require(adapter, "AddComponent<SandstormChampionPoolWaveLease>().Bind(owner, character)", "per instance wave owner")
    support = body(ADAPTER, "private static bool SupportsPlayerCombat(")
    for statement in ("!owner.IsModeEActive", "!owner.IsModeFActive", "!owner.IsZombieModeActive",
                      "!ModBehaviour.IsModeGRunInProgressSafe()", "!ModBehaviour.IsModeHRunInProgressSafe()"):
        require(support, statement, "unsupported combat mode")
    require(body("WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs", "internal void InitializeEnemyPresets()"),
            "SandstormChampionPoolAdapter.Register(owner);", "production pool registration")
    require(body("Integration/IntegrationHostCompatibility.cs", "private bool IsManagedBossPreset("),
            "SandstormChampionPoolAdapter.IsPreset(preset)", "managed identity prevents ordinary preset fallback")
    require(compact(source("WavesArena/WavesArenaRuntimeModule.cs")), "SandstormChampionBoss.NameKey,", "early strong boss protection")
    require(body("Utilities/EnemySpawnHostBridge.cs", "private void BindSpawnPostprocessServices()"),
            "BossPoolSpawnBindings.Bind(enemySpawnRuntime, this);", "host forwards pool spawn bindings")
    require(body("Utilities/BossPoolSpawnBindings.cs", "internal static void Bind("),
            "runtime.BindSandstormChampionService((position, notify, defer, nonWave, active) => SandstormChampionPoolAdapter.SpawnAsync(host, position, active, nonWave, defer));",
            "production spawn delegate binding")
    core = body("Utilities/EnemySpawnCore.cs", "internal async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(")
    require(core, "else if (SandstormChampionPoolAdapter.IsPreset(currentPreset))", "dedicated core factory dispatch")
    require(core, "SandstormChampionPoolAdapter.CleanupCancelled(character);", "core cancellation owner")
    require(core, "normalizeDamageMultiplier && !SandstormChampionPoolAdapter.IsPreset(currentPreset)", "preserve scaled champion skill baseline")
    require(body("WavesArena/WavesArenaRuntimeModule_LegacySpawn.cs", "internal async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync("),
            "SandstormChampionPoolAdapter.SpawnAsync(owner, position, isActiveCheck, false)", "arena and infinite hell dispatch")
    for path, signature, statement in (
        ("Utilities/ModeEFEnemySpawnRuntime.cs", "internal void BuildModeEFactionPresetCaches()", "if (SandstormChampionPoolAdapter.IsPreset(boss)) continue;"),
        ("ModeD/ModeDRuntimeModule_Selection.cs", "internal EnemyPresetInfo GetRandomBossPreset()", "if (SandstormChampionPoolAdapter.IsPreset(candidate)) continue;"),
        ("ModeF/ModeFRespawn.cs", "private EnemyPresetInfo GetRandomModeFRespawnBossPreset()", "if (SandstormChampionPoolAdapter.IsPreset(preset)) continue;"),
        ("ModeG/ModeGRuntimeBridge.cs", "internal ModeGBossSnapshot CreateModeGBossSnapshot()", "if (SandstormChampionPoolAdapter.IsPreset(info)) continue;"),
        ("ModeH/ModeHGroupRoster.cs", "internal static List<ModeHGroupEntry> Build(", "if (SandstormChampionPoolAdapter.IsPreset(info)) continue;"),
        ("PetNest/PetNestCompanionSpawner.cs", "internal static CharacterRandomPreset ResolveSourcePreset(", "if (SandstormChampionPoolAdapter.IsPreset(lineageKey)) return null;"),
    ):
        require(body(path, signature), statement, path + " consumer boundary")
    print(f"SandstormChampionPoolGuard: PASS ({checks} assertions; L1 wiring and structure only)")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, FileNotFoundError) as error:
        print("SandstormChampionPoolGuard: FAIL " + str(error))
        raise SystemExit(1)
