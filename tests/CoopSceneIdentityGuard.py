"""Host travel identity must bind an actual local load, never a same-name roster."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(source, signature):
    assert source.count(signature) == 1, 'unique member: ' + signature
    start = source.index('{', source.index(signature))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if depth == 0:
            return re.sub(r'\s+', ' ', source[start + 1:end]).strip()
    raise AssertionError('unclosed member: ' + signature)


def need(source, tokens, label):
    for token in tokens:
        assert token in source, label + ': ' + token


def main():
    service = clean_source((ROOT / 'Common/Coop/BossRushCoopSceneIdentity.cs').read_text(encoding='utf-8-sig'))
    transport = clean_source((ROOT / 'Common/Coop/BossRushCoopTransport.cs').read_text(encoding='utf-8-sig'))
    need(body(service, 'private bool CurrentCore()'), ('Context() && approved == null && loading == null && Stable(observedCore)',
        'ReferenceEquals(observedCore, MultiSceneCore.Instance)', 'observedCore.gameObject.scene.handle == observedHandle',
        'observedCore.SceneInfo.ID == observedId'), 'actual bound Core')
    need(body(service, 'internal void ObserveCore(MultiSceneCore core)'), ('!Stable(core)', 'completed.Marker.session != session()',
        'completed.Marker.session == session()', '!ReferenceEquals(core, completed.Source)', 'handle != completed.SourceHandle',
        'id == completed.Marker.sceneId', 'localToken = completed.Marker.token;'), 'actual completion')
    adopt = body(service, 'internal bool TryAdoptInitial(string token, string sceneId)')
    need(adopt, ('host()', 'token != firstOfferedToken || everBound || travelSeen || loadSeen || !CurrentCore()',
        '!ReferenceEquals(initialCore, observedCore) || sceneId != observedId'), 'bootstrap only')
    stage = body(service, 'internal void StageMarker(BossRushCoopSceneMarker marker)')
    need(stage, ('!Context() || host() || marker == null || !marker.Valid()', 'retiredSessions.Contains(marker.session)',
        'markerSerials.TryGetValue(marker.session, out prior) && marker.serial <= prior', 'markerSerials[marker.session] = marker.serial;'), 'ingress replay scope')
    begin = body(service, 'internal void ObserveNativeBegin(BossRushCoopSceneMarker destination)')
    need(begin, ('Approval next = staged;', 'staged = null;', 'approved = SameContext(next) && next.Marker.SameDestination(destination) ? next : null;'), 'one native Begin one marker')
    started = body(service, 'private void OnStartedLoading(SceneLoadingContext context)')
    need(started, ('Approval next = approved;', 'approved = null;', 'loading = null;', '!ReferenceEquals(next.Source, MultiSceneCore.Instance)',
        'next.Source.gameObject.scene.handle != next.SourceHandle', 'SceneInfoCollection.GetSceneInfo(next.Marker.sceneId)',
        'info.SceneReference.Name != context.sceneName', 'loading = next;'), 'real native load event')
    reset = body(service, 'internal void ResetSession(string acceptedSession = null)')
    need(reset, ('acceptedRoom != acceptedSession', 'retiredSessions.Add(acceptedRoom);',
        'ReferenceEquals(contextService, service())', 'ReferenceEquals(contextPeer, host() ? null : peer())',
        'staged.Marker.session == acceptedSession', 'approved.Marker.session == acceptedSession', 'loading.Marker.session == acceptedSession'), 'exact room preservation')
    assert 'markerSerials.Clear()' not in reset, 'room reset cannot erase serial high-water'
    need(body(service, 'public void Dispose()'), ('SceneLoader.onStartedLoadingScene -= OnStartedLoading;',
        'SceneManager.sceneUnloaded -= OnSceneUnloaded;', 'ResetSession();'), 'owned subscriptions')
    install = body(transport, 'internal void Install()') if transport.count('internal void Install()') == 1 else None
    bridge = body(transport, 'internal sealed class BossRushCoopSceneIdentityBridge : IDisposable')
    need(body(bridge, 'internal void Install()'), ('harmony.Patch(broadcastBegin, transpiler: sendPatch);',
        'Patch(mod, "BeforeMarkerIngress", null);', 'Patch(begin, "BeforeBeginIngress", null);', 'identity.Install();'), 'native hook wiring')
    send = body(bridge, 'private static IEnumerable<CodeInstruction> ObserveNativeSend(IEnumerable<CodeInstruction> instructions, ILGenerator generator)')
    need(send, ('method.Name != "SendRpc"', 'method.GetGenericArguments()[0].Name != "SceneBeginLoadRpc"',
        'OpCodes.Ldobj', 'OpCodes.Box', '"BeforeNativeSend"', 'result.Add(instruction);', '"AfterNativeSend"', 'if (matched != 1) throw'), 'exact actual native send')
    assert send.index('"BeforeNativeSend"') < send.rindex('result.Add(instruction);') < send.index('"AfterNativeSend"'), 'marker brackets native call'
    need(body(bridge, 'private static BossRushCoopSceneMarker BeforeNativeSend(object message, object instance)'),
        ('ReadDestination(owner.messageDestination, message)', 'marker.session = owner.session();', 'Guid.NewGuid().ToString("N")', 'owner.send(bytes);'), 'actual destination marker')
    need(body(bridge, 'private static void AfterNativeSend(BossRushCoopSceneMarker marker, object instance)'),
        ('owner.identity.ApproveHost(marker);',), 'approve only after native send returns')
    need(body(bridge, 'private bool Ingress(object context)'), ('live() && !host()', '!(bool)isServer.GetValue(context, null)',
        'ReferenceEquals(contextService.GetValue(context, null), service())', 'ReferenceEquals(sender.GetValue(context, null), peer())'), 'native context authentication')
    need(body(bridge, 'private static void BeforeMarkerIngress(object __0, object __1)'),
        ('bytes.Length > 4096', 'BossRushCoopJson.FromJson<BossRushCoopSceneMarker>', 'owner.identity.StageMarker('), 'pre-pump real codec')
    for signature in ('private static void AfterNewVote(object __instance, VoteObservation __state)', 'private static void AfterCancel(object __instance, VoteObservation __state)'):
        need(body(bridge, signature), ('owner.identity.Revision == __state.Revision', 'owner.identity.CancelPending();'), 'nested native calls')
    fixture = (ROOT / 'tests/fixtures/CoopInstalledApi/SceneIdentity/run.py').read_text(encoding='utf-8-sig')
    need(fixture, ('BossRushCoopSceneIdentity.cs', 'BossRushCoopJson.cs', 'BossRushJsonValue.cs', 'SimpleJsonHelper.cs', 'freshTargetPath'), 'real source fixture')
    assert "'SceneIdentity'" in (ROOT / 'tests/fixtures/CoopInstalledApi/run.py').read_text(encoding='utf-8-sig'), 'registered runtime entry'
    print('CoopSceneIdentityGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('CoopSceneIdentityGuard: FAIL - ' + str(error))
        raise SystemExit(1)
