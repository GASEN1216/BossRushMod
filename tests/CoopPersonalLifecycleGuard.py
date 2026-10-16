"""Personal scene/session effects retain native solo behavior and original ownership."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def source(path):
    return clean_source((ROOT/path).read_text(encoding='utf-8-sig'))

def body(text, signature):
    assert text.count(signature) == 1, signature
    start = text.index('{', text.index(signature)); end = start + 1; depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}'); end += 1
    return re.sub(r'\s+', ' ', text[start+1:end-1])

def main():
    content = source('Integration/Coop/BossRushCoopContentRuntimeModule.cs')
    scene = body(content, 'public override void OnSceneLoaded(')
    assert scene.index('if (!BossRushCoop.IsConnected) return;') < scene.index('RefreshSummonScene();'), 'solo scene load preserves native summons'
    refresh = body(content, 'private void RefreshSummonScene(')
    assert 'ReferenceEquals(core, summonCore) && summonRun == BossRushCoop.RunId && summonToken == BossRushCoop.SceneToken' in refresh, 'same actual Core room token retains summons and casts'
    clear = body(content, 'private void ClearCoopContentState(')
    assert 'ResetStaticCaches' not in clear, 'network cleanup never clears weapon-global solo summons'
    assert 'foreach (CharacterMainControl entity in summonEntities.Keys)' in clear, 'network cleanup retires only its registered entities'
    wraith = source('Integration/DeathWraith/DeathWraithCoop.cs')
    killed = body(wraith, 'private bool HandleCoopWraithDeath(')
    assert 'entry.Dead = true;' in killed and '.Deliver(' not in killed and 'DeliverCoopWraithClear' not in killed, 'kill leaves current-world tombstone without persistent deletion receipt'
    legacy = body(wraith, 'private bool ApplyCoopWraithClear(')
    assert 'RemoveStoredDeathWraithInfo' not in legacy and 'LoadStoredDeathWraith' not in legacy, 'legacy kill acknowledgement has no personal store IO'
    incoming = body(wraith, 'private void ReceiveCoopWraithPart(')
    assert 'if (coopWraithSpawns.ContainsKey(key)) { coopWraithPublishAt = 0f; return; }' in incoming, 'dead same-generation fingerprint remains deduplicated'
    spouse = source('Integration/NPCs/Common/CoopNpcWorld_Spouses.cs')
    assert 'previous.Epoch != BossRushCoop.LocalReadyEpoch' in body(spouse, 'private bool SetLocalSpouseIntent('), 'reconnect epoch reissues real local spouse intent'
    assert 'intent.Epoch != player.ReadyEpoch' in body(spouse, 'private void OnSpouseRequest('), 'spouse host binds intent to current ready epoch'
    reply = body(spouse, 'private void OnSpouseReply(')
    assert 'reply.Epoch == BossRushCoop.LocalReadyEpoch' in reply, 'old handshake reply cannot silence new spouse request'
    affix = body(source('Integration/AffixForge/AffixItemData.cs'), 'internal static bool IsValidNetworkVariable(')
    assert 'if (key == NameKey(slot)) return type == CustomDataType.String;' in affix, 'production hidden empty and unknown affix names remain valid typed snapshots'
    ack = body(content, 'private void OnSummonAck(')
    assert 'callback(packet.accepted);' in ack and 'if (!packet.accepted) NotifySummonRejected();' in ack, 'negative summon result refunds and informs the actual caller'
    waylayer = source('SkyIsland/SkyIslandWaylayerChief.cs')
    decision = body(waylayer, 'private void QueueDecision(')
    assert 'entry.DecisionQueued' in decision and 'entry.DecisionQueued = decisions.Deliver(' in decision, 'one stable personal receipt registration owns all generation-independent delivery retries'
    for path in ('Integration/Frostmourne/FrostmourneAction.cs', 'Integration/NewWeapons/SummonStaff/SummonStaffAction.cs'):
        action = source(path)
        if path.endswith('/FrostmourneAction.cs'):
            action += source('Integration/Frostmourne/FrostmourneAction_Coop.cs')
        assert 'StopAction(); return;' in body(action, 'protected override void OnAbilityUpdate('), path+' unsent request exits without completion'
        assert 'attempt != coopSummonAttempt' in body(action, 'private void OnCoopSummonResult('), path+' stale response cannot refund new cast'
        assert 'base.OnStop(); if (coopSummonRejected) { lastEndTime = -999f; isCoolingDown = false; }' in body(action, 'protected override void OnStop('), path+' rejection refunds after real base cooldown assignment'
    print('CoopPersonalLifecycleGuard: PASS')

if __name__ == '__main__':
    try: main()
    except AssertionError as error:
        print('CoopPersonalLifecycleGuard: FAIL:', error)
        raise SystemExit(1)
