"""明确 Mod AI 的预测保护：原 Hurt 准入不变、精确上下文、真实死亡放行、归属现态与清理。"""
from pathlib import Path
import re
from cs_source_util import clean_source
ROOT = Path(__file__).resolve().parents[1]

def read(path): return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))
def body(source, signature):
    assert source.count(signature) == 1, 'unique member: ' + signature
    start = source.index('{', source.index(signature)); depth = 0
    for at in range(start, len(source)):
        depth += (source[at] == '{') - (source[at] == '}')
        if depth == 0: return source[start+1:at]
    raise AssertionError('unclosed member: ' + signature)
def ordered(source, tokens, label):
    cursor = 0
    for token in tokens:
        found = source.find(token, cursor)
        assert found >= 0, label + ': ' + token
        cursor = found + len(token)

def main():
    source = read('Utilities/CoopAiPredictionHealthProtection.cs')
    authority = body(source, 'internal static bool HasEntityAuthority(CharacterMainControl character)')
    ordered(authority, ['if (character == null) return false;',
                        'if (marker != null && marker.Replica) return false;',
                        'if (!BossRushCoop.HasWorldAuthority) return false;',
                        'marker == null || !marker.AuthorityBound',
                        'BossRushCoop.IsHost && !string.IsNullOrEmpty(marker.AuthorityRun) && marker.AuthorityRun == BossRushCoop.RunId'], 'frozen entity authority')
    birth = body(source, 'internal static void TrackAuthority(CharacterMainControl character)')
    ordered(birth, ['!BossRushCoop.IsHost', 'if (marker.Replica) return;',
                    'if (!marker.AuthorityBound)',
                    'marker.AuthorityBound = true; marker.AuthorityRun = BossRushCoop.RunId;',
                    'Owner != null && HasEntityAuthority(character)', 'Owner.Track(character);'], 'host birth cannot relabel old entities')
    replica = body(source, 'internal static void MarkReplica(CharacterMainControl character)')
    ordered(replica, ['!BossRushCoop.IsClient', 'marker.Replica = true;',
                      'Owner == null || !BossRushCoop.HasBoundScene', 'marker.PredictionRun = BossRushCoop.RunId;',
                      'marker.PredictionToken = BossRushCoop.SceneToken;', 'marker.Armed = true;'], 'replica birth precedes optional prediction owner')
    begin = body(source, 'internal static Health Begin(Health health, CharacterMainControl attacker)')
    ordered(begin, ['Health previous = predicting; predicting = null;', '!BossRushCoop.IsClient',
                    'BossRushCoop.GetPlayerId(attacker) != BossRushCoop.LocalPlayerId',
                    'marker.Armed && marker.Owner == Owner', 'predicting = health;'], 'local/exact owner context')
    clamp = body(source, 'internal static bool TryClamp(Health health, ref float value)')
    ordered(clamp, ['!BossRushCoop.IsClient', 'health != predicting', 'value > 0f', 'return false;', 'value = 1f; return true;'], 'exact client lethal clamp')
    assert 'CoopAiPredictionHealthProtection.End(__state); return __exception;' in body(source, 'private static Exception Finalizer(Exception __exception, Health __state)'), 'exception/nested context cleanup'
    setter = body(read('Patches/Combat/BossLethalHealthProtectionPatch.cs'), 'private static void Prefix(Health __instance, ref float value)')
    ordered(setter, ['BossRushHealthHurtContextPatch.IsInsideHurt', 'value > 0f',
                     'CoopAiPredictionHealthProtection.TryClamp(__instance, ref value)', 'TryClampReverseScale',
                     'TryClampDragonKing', 'TryClampDragonDescendant', 'TryClampPetNestCompanion'], 'existing setter consumers preserved')
    track = body(source, 'internal void Track(CharacterMainControl character)')
    ordered(track, ['RefreshWorld();', 'tracked.Add(character)', 'authorities.Add(new Authority { Character = character })'], 'explicit ownership queue')
    receive = body(source, 'private void Receive(BossRushCoopMessage message)')
    ordered(receive, ['!message.IsFromHost', 'RefreshWorld();', 'state.Run != run', 'state.Generation != generation', 'latest = state;'], 'host/current world only')
    for signature in ('private void Receive(BossRushCoopMessage message)',
                      'private void OnAiSpawned(int id, CharacterMainControl character)',
                      'public override void OnUpdate(float deltaTime, float unscaledDeltaTime)',
                      'private void PublishAuthority()',
                      'internal static Health Begin(Health health, CharacterMainControl attacker)',
                      'internal static bool TryClamp(Health health, ref float value)'):
        assert '!BossRushCoop.HasBoundScene' in body(source, signature), 'bound actual Core at consumer: ' + signature
    ordered(receive, ['string.IsNullOrEmpty(state.SceneToken)', 'state.SceneToken != BossRushCoop.SceneToken', 'latest = state;'], 'exact certificate travel token')
    eligibility = body(source, 'private static bool IsCurrent(CoopAiPredictionReplicaMarker marker)')
    for token in ('marker.PredictionRun == BossRushCoop.RunId', 'marker.PredictionGeneration == BossRushCoop.SceneGeneration',
                  '!string.IsNullOrEmpty(marker.PredictionToken)', 'marker.PredictionToken == BossRushCoop.SceneToken'):
        assert token in eligibility, 'frozen prediction qualification: ' + token
    assert 'IsCurrent(marker)' in begin and 'IsCurrent(marker)' in clamp, 'both Hurt admission and actual lethal setter check frozen scene'
    assert 'latest.SceneToken == BossRushCoop.SceneToken' in body(source, 'private void OnAiSpawned(int id, CharacterMainControl character)'), 'native birth cannot replay old certificate'
    assert 'else if (latest != null && latest.SceneToken == BossRushCoop.SceneToken)' in body(source, 'public override void OnUpdate(float deltaTime, float unscaledDeltaTime)'), 'poll cannot replay old certificate'
    assert 'SceneToken = BossRushCoop.SceneToken' in body(source, 'private void PublishAuthority()'), 'host publishes actual travel token'
    refresh = body(source, 'private void RefreshWorld()')
    assert 'BossRushCoop.IsClient && !BossRushCoop.HasBoundScene' in refresh and 'ClearWorld();' in refresh, 'native Begin retires previous client qualification before generation advances'
    update = body(source, 'public override void OnUpdate(float deltaTime, float unscaledDeltaTime)')
    for token in ('entry.Id == 0', 'BossRushCoop.FindAi(id)', 'BindReplica(id, character);'):
        assert token in update, 'delayed signed ID producer: ' + token
    binding = body(source, 'private void BindReplica(int id, CharacterMainControl character)')
    for token in (
                  'bound.TryGetValue(id, out previous) && previous == character', 'old.Armed = false; old.Owner = null;',
                  'CoopAiPredictionHealthProtection.MarkReplica(character);', 'bound[id] = character;'):
        assert token in binding, 'same-ID replacement: ' + token
    assert 'if (entry.Id != 0) ids.Add(entry.Id);' in body(source, 'private void PublishAuthority()'), 'signed ID publication'
    arrival = body(source, 'private void OnAiSpawned(int id, CharacterMainControl character)')
    ordered(arrival, ['tracked.Contains(character)', 'entry.Id = id;', 'PublishAuthority();', 'latest.Generation == BossRushCoop.SceneGeneration', 'BindReplica(id, character);'], 'explicit native birth race')
    assert 'BossRushCoop.AiSpawned += OnAiSpawned;' in body(source, 'public override void OnAwake(ModBehaviour owner)'), 'native birth subscription'
    clear = body(source, 'private void ClearWorld()')
    ordered(clear, ['marker.Armed = false; marker.Owner = null;', 'authorities.Clear();', 'bound.Clear();', 'latest = null;'], 'immediate owner cleanup')
    assert 'Destroy(' not in clear, 'no delayed Destroy across a new binding'
    assert not re.search(r'\.(?:Replica|AuthorityBound)\s*=\s*false|\.AuthorityRun\s*=\s*(?:null|string\.Empty)', source), 'cleanup cannot erase permanent entity birth'
    ordered(body(source, 'public override void OnDestroy()'), ['subscription.Dispose();', 'ClearWorld();', 'ResetStaticCaches();'], 'single owner teardown')
    assert 'BossRushCoop.AiSpawned -= OnAiSpawned;' in body(source, 'public override void OnDestroy()'), 'native birth cleanup'
    assert 'CoopAiPredictionHealthProtection.TrackAuthority(character);' in read('Common/Coop/BossRushCoop.cs'), 'explicit facade registration producer'
    for path, token in [('Integration/DeathWraith/DeathWraithCoop.cs', 'MarkReplica(replica);'),
                        ('ModeG/ModeGCoopRuntime.cs', 'MarkReplica(character);'),
                        ('SkyIsland/SkyIslandSessionCoop.cs', 'MarkReplica(actor);')]:
        assert 'CoopAiPredictionHealthProtection.' + token in read(path), 'known replica consumer: ' + path
    assert not re.search(r'FindObjectsOfType|FindObjectOfType|CoopSyncDatabase', source), 'no blanket native AI ownership discovery'
    print('CoopAiPredictionHealthGuard: PASS')

if __name__ == '__main__':
    try: main()
    except AssertionError as error:
        print('CoopAiPredictionHealthGuard: FAIL - ' + str(error)); raise SystemExit(1)
