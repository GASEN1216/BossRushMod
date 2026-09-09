"""Freeze Sky Island's additive save contract and shared persistence ownership."""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def source(name):
    return clean_source((ROOT / 'DebugAndTools/SkyIsland' / name).read_text(encoding='utf-8'))


def main():
    rules = source('SkyIslandStoryRules.cs')
    service = source('SkyIslandStoryService.cs')
    codec = source('SkyIslandStoryCodec.cs')
    recovery = source('SkyIslandStorySaveRecovery.cs')
    assert '"BossRush_SkyIsland_Story_v1"' in rules, 'SkyIslandStoryRules: frozen storage key changed'
    assert 'SchemaVersion = 1' in rules, 'SkyIslandStoryRules: schema changed without migration review'
    for name in ('schemaVersion', 'flags', 'visitedRegions', 'clearedEncounters', 'discoveredNotes'):
        assert '"' + name + '"' in codec, 'SkyIslandStoryCodec: missing persisted field ' + name
    assert 'BossRushJsonParser.ParseOrNull' in codec and 'new BossRushJsonWriter' in codec, 'Use the shared JSON implementation'
    assert 'new BossRushSlotJsonStore<SkyIslandStoryData>' in service, 'Use shared slot store'
    assert 'new BossRushSaveCoordinatorEngine' in service, 'Use shared save coordinator'
    assert 'candidate = source.Copy()' in rules, 'Rules must construct candidate before committing'
    assert 'SavesSystem.CurrentSlot == entrySlot' in service, 'Old sessions must not mutate a new slot'
    assert '!safeToFlush' in service, 'Independent raid must not permit combat frame physical saves'
    assert 'store.ShutdownSubscription()' in service, 'Story owner must release save event subscriptions'
    assert 'value.TryClose()' in recovery and 'story.TryClose()' in recovery, 'Retain and retry pending progress after scene exit'
    assert 'private void OnDestroy()' in recovery and 'story.Close()' in recovery, 'Recovery needs final lifecycle cleanup'
    # CR-2026-09-08-004：待保存 owner 从第一次移交起就必须独立于 Mod 宿主，
    # 否则宿主销毁时 OnDestroy -> Close 会在保存失败的情况下照样退订，丢掉已接受事实。
    assert 'internal static void CloseOrRetain(SkyIslandStoryService value)' in recovery, 'Recovery owner must not take a host'
    assert 'DontDestroyOnLoad' in recovery, 'Pending save owner must survive host destruction'
    assert 'AddComponent' not in recovery.split('CloseOrRetain', 1)[1].split('recoveryRoot.AddComponent', 1)[0], \
        'Recovery must never attach to a caller-supplied host'
    assert 'ReferenceEquals(existing.story, value)' in recovery, 'Repeat handover must not create a second owner'
    assert 'SkyIslandStorySaveRecovery.CloseOrRetain(story)' in source('SkyIslandSession.cs'), 'Session must hand over without a host'
    # CR-2026-09-08-003：单向 StoreFaulted 之后必须有可到达的恢复终点，
    # 否则恢复 owner 永远占着同一个槽，入口一直显示「仍在保存」。
    assert 'private bool TryRecoverFaultedStore()' in service, 'Faulted store needs an explicit recovery path'
    for caller in ('internal void Tick(bool safeToFlush)', 'internal bool TryClose()'):
        body = service.split(caller, 1)[1].split('\n        }', 1)[0]
        assert 'TryRecoverFaultedStore()' in body, 'Recovery must be attempted from ' + caller
    assert 'store.IsStoreFaulted = ' not in service and 'IsStoreFaulted = false' not in service, \
        'Never clear the shared one-way fault flag; build a new store instead'
    assert 'replacement = CreateStore()' in service and 'replacement.Store(accepted)' in service, \
        'Recovery must re-adopt the last accepted snapshot through a fresh store'
    assert 'SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(accepted)) == null' in service, \
        'Recovery must round-trip validate before adopting'
    assert 'if (!adopted && replacement != null) replacement.ShutdownSubscription()' in service, \
        'Failed recovery must not leak the replacement subscription'
    assert '"SkyIslandStory"' in (ROOT / 'tools/run_runtime_regressions.py').read_text(encoding='utf-8'), 'Register production execution fixture'
    for body in (service, rules, codec, recovery):
        assert 'SavesSystem.SaveFile' not in body, 'Physical saves belong exclusively to the shared coordinator'
    print('PASS SkyIslandStoryContractGuard')


if __name__ == '__main__':
    main()
