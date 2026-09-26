"""Arena algorithms and regeneration scratch belong to their actual runtime owners."""
from pathlib import Path
import re
from cs_source_util import clean_source
from ArchitectureStructureGuard import extract_method_body

ROOT=Path(__file__).resolve().parents[1]
def read(p): return clean_source((ROOT/p).read_text(encoding="utf-8-sig"))
def compact(s): return re.sub(r"\s+", "", s)

def main():
    host=read("ModBehaviour.cs")
    registry=read("WavesArena/WavesArenaRuntimeModule_CharacterRegistry.cs")
    spawn=read("WavesArena/WavesArenaRuntimeModule_LegacySpawn.cs")
    loot=read("WavesArena/WavesArenaRuntimeModule_LootTemplates.cs")
    regen=read("Integration/Mutators/MutatorBossRegenRuntime.cs")
    registration=read("ModBehaviourRuntimeModules.cs")
    for src in (registry,spawn,loot,regen):
        assert "partial class ModBehaviour" not in src,"runtime algorithms must not be host partials"
    for declaration in ("private static List<CharacterMainControl> _cachedCharacters", "private readonly HashSet<CharacterMainControl> bossRushOwnedDaXingXing",
                        "private static bool _characterCacheNeedsRefresh", "private static float _characterCacheRefreshTimer", "private static Vector3 _arenaCenter"):
        assert declaration in registry and declaration not in host,"registry state owner changed: "+declaration
    assert "private readonly List<MonoBehaviour> _singleBossRegenList" in regen and "_singleBossRegenList" not in host,"regeneration scratch must belong to the runtime"
    for name,args,returns,static in (
        ("SpawnEnemyAtPositionAsync","preset, position, isActiveCheck",True,False),
        ("ApplyInfiniteHellScaling","character, preset",False,False),
        ("GetPresetTeam","preset",True,False),
        ("GetLootBoxTemplateWithLoader","",True,True),
        ("GetDifficultyRewardLootBoxTemplate","",True,True),
        ("ApplyLootBoxCoverSetting","lootbox, ignoreConfig",False,False),
        ("SetArenaCenterFromMapConfig","sceneName",False,False),
        ("IsDaXingXingPreset","preset",True,False),
        ("TryCleanNonBossRushDaXingXing","",False,False),
        ("RefreshCharacterCache","",False,True),
    ):
        match=re.search(r"(?:private|internal) (?:static )?[\w<>]+ "+name+r"\(",host)
        assert match,"missing original bridge: "+name
        body=extract_method_body(host,match.group())
        expected="{"+("return " if returns else "")+("WavesArenaRuntimeModule" if static else "wavesArenaRuntime")+"."+name+"("+args+");}"
        assert compact(body)==compact(expected),"legacy bridge must be one exact forward: "+name
    body=extract_method_body(spawn,"internal async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(")
    assert body.index("ModBehaviour coroutineHost = owner;")<body.index("await "),"spawn must capture original coroutine host before await"
    assert "coroutineHost.StartCoroutine(enemyRecoveryMonitor.DelayedBossPositionValidation(character, 0.5f));" in body,"original coroutine host and delay must survive module teardown"
    assert "enemyRecoveryMonitor.RegisterEnemyRecoveryAnchor(character, position);" in body,"spawn must register anchor with shared recovery service"
    assert "BindSpawnPostprocessServices();\n            BindArenaSpawnServices();" in registration,"legacy spawn services bind at the original module assembly phase"
    bindings=read("WavesArena/WavesArena.cs")
    for statement in ("wavesArenaRuntime.BindLegacySpawnServices(", "wavesArenaRuntime.BindLootBoxPolicies(() => config != null, () => config.lootBoxBlocksBullets);"):
        assert statement in bindings,"Arena dependencies must bind explicitly: "+statement
    for statement in (
        "mutatorBossRegenRuntime.BindArenaQueries(() => IsActive, () => wavesArenaRuntime.BossesPerWave,",
        "() => wavesArenaRuntime.CurrentBoss, () => wavesArenaRuntime.CurrentWaveBosses);",
        "mutatorBossRegenRuntime.BindModeDQueries(() => modeDRuntime.IsActive, () => modeDRuntime.modeDCurrentWaveEnemies);",
        "mutatorBossRegenRuntime.BindModeEQueries(() => modeERuntime.IsModeEActive, () => modeERuntime.ModeEAliveEnemies, modeERuntime.GetModeEBossRegenCache);",
        "mutatorBossRegenRuntime.BindModeFQueries(() => modeFRuntime.IsModeFActive, () => modeFActiveBossSet, modeFRuntime.GetModeFBossRegenCache);",
    ):
        assert statement in registration,"regeneration must bind original live mode queries: "+statement
    update=extract_method_body(host,"void Update()")
    positions=[update.find(token) for token in ("TickModeRuntimeGroup(", "mutatorBossRegenRuntime.Tick();", "if (f3DebugCheatMenuVisible)")]
    assert min(positions)>=0 and positions[0]<positions[1]<positions[2],"regeneration must remain between mode dispatch and debug modal gate"
    print("ArenaHostRemainderOwnershipGuard: PASS")
    return 0

if __name__=="__main__":
    try: raise SystemExit(main())
    except AssertionError as error:
        print("ArenaHostRemainderOwnershipGuard: FAIL - "+str(error));raise SystemExit(1)
