"""Native guest scene entry must reach local facilities without F3 pending intent."""
from pathlib import Path
from cs_source_util import clean_source
from CoopGameplaySceneGuard import body

ROOT = Path(__file__).resolve().parents[1]


def main():
    coop = clean_source((ROOT / 'WavesArena/WavesArenaRuntimeModule_Coop.cs').read_text(encoding='utf-8-sig'))
    apply = body(coop, 'private void ApplyCoopArena(')
    assert 'coopArenaEntryToken = BossRushCoop.SceneToken;' in apply, 'accepted snapshot records current native scene token'
    assert 'ScheduleCoopArenaEntry();' in apply, 'accepted snapshot reaches real guest entry scheduler'
    schedule = body(coop, 'private void ScheduleCoopArenaEntry()')
    for required in ('!BossRushCoop.IsClient', '!BossRushCoop.HasBoundScene', '!ArenaActive', '(CoopEntryMode != 1 && CoopEntryMode != 2)',
                     'coopArenaEntryScheduledToken == BossRushCoop.SceneToken && coopArenaEntryHandle == scene.handle',
                     'owner.StartCoopArenaEntryForScene(scene);'):
        assert required in schedule, 'guest scene entry scope: ' + required
    wait = body(coop, 'internal IEnumerator SetupCoopClientEntry(')
    assert 'coopArenaEntryToken != BossRushCoop.SceneToken' in wait, 'old mode cannot configure a new scene'
    placement = body(wait, 'if (BossRushCoop.IsClient && BossRushCoop.RunId == room && scene.isLoaded && SceneManager.GetActiveScene().handle == scene.handle && (CoopEntryMode == 1 || CoopEntryMode == 2))')
    for required in ('CharacterMainControl local = CharacterMainControl.Main;', 'string token = BossRushCoop.SceneToken;',
                     'int generation = BossRushCoop.SceneGeneration;', 'string asset = BossRushCoop.LocalAssetId;',
                     'local.Health == null || local.Health.IsDead || !BossRushCoop.HasBoundScene',
                     'string.IsNullOrEmpty(asset) || coopArenaEntryToken != token',
                     'Vector3 target = ModBehaviour.GetDefaultPositionForScene(scene.name);',
                     'if (local.gameObject.scene != scene) SceneManager.MoveGameObjectToScene(local.gameObject, scene);',
                     'if (!BossRushInitialSpawn.HasArrived(target) && (local.transform.position - target).sqrMagnitude > 0.25f) local.SetPosition(target);'):
        assert required in placement, 'exact local Normal/D first placement: ' + required
    assert placement.count('local.SetPosition(target);') == 1, 'first entry has one position operation'
    validation = placement[placement.index('if (!aligned'):placement.index('createEntry();')]
    for required in ('!BossRushCoop.IsClient', 'BossRushCoop.RunId != room', '!BossRushCoop.HasBoundScene',
                     'BossRushCoop.SceneToken != token', 'coopArenaEntryToken != token',
                     'BossRushCoop.SceneGeneration != generation', 'BossRushCoop.LocalAssetId != asset',
                     '!scene.isLoaded', 'SceneManager.GetActiveScene().handle != scene.handle',
                     'local == null', 'CharacterMainControl.Main != local', 'local.Health == null', 'local.Health.IsDead', 'yield break;'):
        assert required in validation, 'position callback must retain the actual local entry lease: ' + required
    assert placement.index('local.SetPosition(target);') < placement.index('if (!aligned') < placement.index('createEntry();') < placement.index('continueEntry();') < placement.index('createRescue();'), 'revalidate before dispatching existing local facilities'
    entry = clean_source((ROOT / 'WavesArena/BossRushEntryFlow.cs').read_text(encoding='utf-8-sig'))
    bridge = body(entry, 'internal void StartCoopArenaEntryForScene(')
    for required in ('SetCurrentMapSpawnPoints(scene.name);', 'SetArenaCenterFromMapConfig(scene.name);',
                     'StartCoroutine(WaitForLevelInitializedThenSetup(scene));'):
        assert required in bridge, 'real local facilities readiness bridge: ' + required
    custom = clean_source((ROOT / 'Integration/BossRushIntegration_TravelAndSetup.cs').read_text(encoding='utf-8-sig'))
    assert 'wavesArenaRuntime.SetCoopEntryMode((int)entryMode + 1);' in body(custom, 'private System.Collections.IEnumerator SetupBossRushInGroundZero('), 'custom maps publish their actual entry mode'
    print('CoopArenaGuestEntryGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('CoopArenaGuestEntryGuard: FAIL - ' + str(error))
        raise SystemExit(1)
