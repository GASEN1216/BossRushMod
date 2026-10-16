"""Freeze Sky Island encounter ownership and formal content wiring."""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def main():
    source = clean_source((ROOT / 'SkyIsland/SkyIslandEncounters.cs').read_text(encoding='utf-8'))
    content = clean_source((ROOT / 'SkyIsland/SkyIslandContent.cs').read_text(encoding='utf-8'))
    rules = clean_source((ROOT / 'SkyIsland/SkyIslandStoryRules.cs').read_text(encoding='utf-8'))
    # 内容表只由会话加载一次并传入，遭遇 owner 不再自行解析一遍 World.json。
    assert 'SkyIslandContent.Load()' not in source, 'Encounters must consume the session-loaded content table'
    assert 'SkyIslandContentData content' in source, 'Encounters must take the content table as a constructor input'
    session = clean_source((ROOT / 'SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8'))
    assert session.count('SkyIslandContent.Load()') == 1, 'Session owns the single content load'
    assert 'groundMask, content, IsWorldSessionValid' in session, 'Session must pass its content table and world lifetime to the encounter owner'
    # 房主阵亡时 TickCoopWorldWhileDead 仍推进搜刮；巡守的钟庭门控必须与门同用全员并集。
    assert 'new SkyIslandScavenging(root, player, groundMask, raidSeed, IsWorldSessionValid,' in session, 'Scavenging must outlive a dead host while guests are alive'
    assert 'content, IsWorldSessionValid, CoopGateStory);' in session, 'Patrol region gates must use the same co-op gate story as the gates'
    coop = clean_source((ROOT / 'SkyIsland/SkyIslandSessionCoop.cs').read_text(encoding='utf-8'))
    assert 'BossRushCoop.AnyPlayerAlive' in coop and ': IsSessionValid()' in coop, 'Coop world lifetime must include living guests and preserve offline validity'
    for token in ('content.Encounters', 'clone.dropBoxOnDead = true',
                  'if (completed(encounter.Id) || BossRushCoop.IsConnected)', 'SkyIslandEnemyRecord[]', 'actor.Died',
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
    assert '!BossRushCoop.IsConnected && !encounter.Started && encounter.Manual && completed(encounter.Id)' in source, 'Host old manual endings cannot preclear another participant challenge'
    assert 'ChallengePlayer = challenger' in coop and 'ChallengeAsset = asset' in coop and 'ChallengeRequest = request' in coop, 'Challenge must freeze exact original player/asset/request'
    assert 'target.Started && !target.Cleared' in coop, 'Full group must reopen for a different eligible challenger after clearing'
    assert 'CompleteCoopChallenge(id, true)' in session and 'CompleteCoopChallenge(id, false)' in session, 'All named kill paths must route original initiator completion'
    assert 'TryChallengeOutcome(entry.Id, out namedOutcome)) continue;' in coop, 'World snapshots cannot grant personal named endings to bystanders'
    assert 'coopChallengeReplies[key] = reply;' in coop and 'coopChallengeDelivery.Deliver(requester.Id, key, reply, data.AssetId)' in coop, 'Both accept and reject require reliable original-asset receipt'
    assert 'Challenge not started:' in coop, 'Named refusal cannot use echo wording'
    runtime=clean_source((ROOT / 'SkyIsland/SkyIslandCoopRuntime.cs').read_text(encoding='utf-8'))
    assert 'completions.Deliver(value.Player, id, value, value.Asset)' in runtime and 'value.Asset != BossRushCoop.LocalAssetId' in runtime, 'Persistent completion must route frozen owner and original save'
    assert 'pendingCompletions' in runtime and 'FlushChallengeCompletions();' in runtime, 'Disconnected original owner must retain earned outcome'
    tick=clean_source((ROOT / 'SkyIsland/SkyIslandSessionTick.cs').read_text(encoding='utf-8'))
    assert 'BossRushCoop.IsConnected ? !IsStoryChallengeActive("Zheling")' in tick, 'Zheling resident must return so next participant can actually initiate'
    story=clean_source((ROOT / 'SkyIsland/SkyIslandWorldStory.cs').read_text(encoding='utf-8'))
    assert story.count('Challenge requested; waiting for the host') == 2, 'Client challenge UI must wait for host acceptance'
    print('PASS SkyIslandEncounterOwnershipGuard')


if __name__ == '__main__':
    main()
