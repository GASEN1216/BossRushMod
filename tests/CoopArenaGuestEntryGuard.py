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
