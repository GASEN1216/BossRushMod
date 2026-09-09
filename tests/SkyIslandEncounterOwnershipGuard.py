"""Freeze Sky Island encounter ownership and formal content wiring."""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def main():
    source = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandEncounters.cs').read_text(encoding='utf-8'))
    content = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandContent.cs').read_text(encoding='utf-8'))
    rules = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandStoryRules.cs').read_text(encoding='utf-8'))
    # 内容表只由会话加载一次并传入，遭遇 owner 不再自行解析一遍 World.json。
    assert 'SkyIslandContent.Load()' not in source, 'Encounters must consume the session-loaded content table'
    assert 'SkyIslandContentData content' in source, 'Encounters must take the content table as a constructor input'
    session = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8'))
    assert session.count('SkyIslandContent.Load()') == 1, 'Session owns the single content load'
    assert 'groundMask, content, IsSessionValid' in session, 'Session must pass its content table to the encounter owner'
    for token in ('content.Encounters', 'clone.dropBoxOnDead = true',
                  'if (completed(encounter.Id))', 'SkyIslandEnemyRecord[]', 'actor.Died',
                  'life.OwnPreset(clone)', 'life.Bind(created, actor)', 'if (!retained && created != null)',
                  'Destroy(preset, 0.1f)', 'SkyIslandResidents.ApplyBattleFace', 'created.SetTeam(Teams.wolf)'):
        assert token in source, 'SkyIslandEncounters missing required owner binding: ' + token
    assert 'TransferLoot' not in source and 'CreateFromItem' not in source, 'Official death owns drops; do not add duplicate transfer'
    assert 'SkyIslandPresetCleanup' not in source, 'Scene cleanup must not destroy in-flight presets'
    assert 'JsonDataRegistry.TryReadDataFile("SkyIsland", "World.json", out raw)' in content, 'Use shared content IO'
    for region in ('D', 'G'):
        assert 'source.EncounterCleared("' + region + '_02")' in rules, 'Beacon must require second encounter in ' + region
    runner = (ROOT / 'tools/run_runtime_regressions.py').read_text(encoding='utf-8')
    assert '"SkyIslandEncounters"' in runner, 'Register production owner execution regression'
    print('PASS SkyIslandEncounterOwnershipGuard')


if __name__ == '__main__':
    main()
