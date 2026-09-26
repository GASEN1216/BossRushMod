"""Random-event effects must belong to the registered owner and retain lifecycle gates."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT=Path(__file__).resolve().parents[1]
FILES=("RandomEventEffectsBridge.cs", "RandomEventEffectsBridge_Spawn.cs", "RandomEventEffectsBridge_Loot.cs")
ENTRIES=("IsRandomEventInfiniteHellRun", "GetRandomEventSpawnPointsSafe", "GetRandomEventSafePointAwayFromPlayer", "CollectEventBuffTargets", "ShowRandomEventBanner", "ShowRandomEventDirectionalBanner", "TryApplyRandomEventForcedWeather", "RestoreRandomEventForcedWeather", "MakeRandomEventAiSound", "PopRandomEventText", "PlayRandomEventModSound", "PlayRandomEventStinger", "CreateRandomEventAirdropLootbox", "RandomEventAirdropDropRoutine", "SpawnRandomEventCashPiles", "SpawnRandomEventIntruderBoss", "DespawnRandomEventIntruderBoss", "SpawnRandomEventParadeDucks", "HasRandomEventMerchantPreset", "SpawnRandomEventMerchant", "DespawnRandomEventMerchant")

def body(source,name):
    matches=list(re.finditer(r'\b(?:internal|private|public)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]]+\s+'+name+r'\(',source))
    assert len(matches)==1,"missing/duplicate method: "+name
    start=source.index('{',matches[0].start());end,depth=start+1,1
    while depth:depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start+1:end-1]

def ordered(source,tokens,label):
    pos=0
    for token in tokens:
        found=source.find(token,pos)
        assert found>=0,label+" missing/order changed: "+token
        pos=found+len(token)

def main():
    parts=[clean_source((ROOT/'RandomEvents'/p).read_text(encoding='utf-8-sig')) for p in FILES]
    for path,source in zip(FILES,parts):
        assert 'internal sealed partial class RandomEventsRuntimeModule' in source,path+' must be module-owned'
        assert 'partial class ModBehaviour' not in source,path+' returned to host'
    source='\n'.join(parts)
    host=clean_source((ROOT/'RandomEvents/RandomEventsRuntimeModuleHostBridge.cs').read_text(encoding='utf-8-sig'))
    for name in ENTRIES:
        body(source,name)
        compact=re.sub(r'\s+','',body(host,name))
        assert re.fullmatch(r'(?:return)?randomEventsRuntime\.'+name+r'\([^;{}]*\);',compact),'host bridge disconnected: '+name
    collect=body(source,'CollectEventBuffTargets')
    ordered(collect,['_owner.RefreshRandomEventCharacterCacheForRuntime()', '_owner.RandomEventCachedCharactersForRuntime.Count', 'c.Health.IsDead', 'Team.IsEnemy', 'PetNestCompanionAgent.IsCompanionCharacter', 'buffer.Add(c)'],'buff target cache')
    assert 'get { return WavesArenaRuntimeModule.CharacterCache; }' in host and 'WavesArenaRuntimeModule.RefreshCharacterCache();' in host,'shared character cache disconnected'
    for statement in ('get { return BossRushAudioRuntimeService.CachedSpawnEggBehavior; }',
                      'set { BossRushAudioRuntimeService.CachedSpawnEggBehavior = value; }',
                      'set { BossRushAudioRuntimeService.EggSpawnPreset = value; }'):
        assert statement in host,'parade must share original Audio egg caches: '+statement
    ordered(body(source,'TryApplyRandomEventForcedWeather'),['prevForce = inst.ForceWeather;', 'prevValue = inst.ForceWeatherValue;', 'WeatherManager.SetForceWeather(true, weather);'],'weather capture')
    assert 'WeatherManager.SetForceWeather(prevForce, prevValue);' in body(source,'RestoreRandomEventForcedWeather'),'weather restore disconnected'
    intruder=body(source,'SpawnRandomEventIntruderBossAsync')
    ordered(intruder,['_owner.EnsureCharacterPresetsCacheReady()', 'await _owner.SpawnEnemyCoreInternalAsync(', 'SuppressWaveBossRegistration = true', 'isStillValid != null && !isStillValid()', 'DespawnRandomEventIntruderBoss(boss)', 'onSpawned(boss)'],'intruder async')
    ordered(body(source,'DespawnRandomEventIntruderBoss'),['_owner.UnregisterRandomEventRecoveryForRuntime', 'BossBgmKeys.DragonDescendant', 'BossBgmKeys.PhantomWitch', 'BossBgmKeys.DragonKing', 'UnityEngine.Object.Destroy(boss.gameObject)'],'intruder cleanup')
    parade=body(source,'SpawnRandomEventParadeDucksAsync')
    ordered(parade,['_owner.RandomEventSpawnEggBehaviorForRuntime', '_owner.RandomEventEggSpawnPresetForRuntime = behavior.spawnCharacter', 'await behavior.spawnCharacter.CreateCharacterAsync', 'SceneManager.GetActiveScene().buildIndex != sceneBuildIndex', 'UnityEngine.Object.Destroy(duck.gameObject)', 'SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep', 'onSpawned(duck)', 'spawned++', 'await UniTask.Yield()', 'finally', 'InvokeRandomEventSpawnCompletion'],'parade lifecycle')
    merchant=body(source,'SpawnRandomEventMerchantAsync')
    ordered(merchant,['await preset.CreateCharacterAsync', 'SceneManager.GetActiveScene().buildIndex != sceneBuildIndex', 'DespawnRandomEventMerchant(character, null)', '_owner.SetRandomEventMerchantHealthForRuntime', 'SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep', 'BuildRandomEventMerchantShop', 'MultiSceneCore.MoveToActiveWithScene', 'onSpawned(character, shop)'],'merchant lifecycle')
    cash=body(source,'SpawnRandomEventCashPilesAsync')
    assert cash.count('_owner == null || (isStillValid != null && !isStillValid())')==2,'cash must check Unity owner before and after await'
    ordered(cash,['await ItemAssetsCollection.InstantiateAsync', 'cash.DestroyTree()', 'UnityEngine.Random.insideUnitSphere', 'UnityEngine.Random.Range', 'cash.Drop', 'spawned++', 'await UniTask.Yield()', 'finally', '_owner != null && (isStillValid == null || isStillValid())', 'InvokeRandomEventCashCompletion'],'cash lifecycle')
    assert 'DecorateLootbox(lootbox, _owner, false, true);' in body(source,'CreateRandomEventAirdropLootbox'),'airdrop lost host decoration owner'
    print('RandomEventEffectsOwnershipGuard: PASS')

if __name__=='__main__':
    try:main()
    except AssertionError as error:
        print('RandomEventEffectsOwnershipGuard: FAIL - '+str(error));raise SystemExit(1)
