"""World channel ownership, ordered execution ACK and actual scene projection boundary."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(source, signature):
    assert source.count(signature) == 1, 'unique member: ' + signature
    opening = source.index('{', source.index(signature))
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if depth == 0:
            return re.sub(r'\s+', ' ', source[opening + 1:end]).strip()
    raise AssertionError('unclosed member: ' + signature)


def need(source, tokens, label):
    for token in tokens:
        assert token in source, label + ': ' + token


def main():
    source = clean_source((ROOT / 'Utilities/CoopGameplayChannel.cs').read_text(encoding='utf-8-sig'))
    request = body(source, 'internal bool Request(')
    assert request == 'return QueueRequest(action, value, text, flag, false);', 'each manual interaction keeps its own request'
    assert body(source, 'internal bool RequestRepeat(') == 'return QueueRequest(action, value, text, flag, true);', 'periodic facts opt into reuse explicitly'
    request = body(source, 'private bool QueueRequest(')
    need(request, ('pendingRequests.Add(new PendingRequest', 'Core = core, Handle = core != null ? core.gameObject.scene.handle',
        'Service = BossRushCoop.NativeService, Peer = BossRushCoop.NativeHostPeer', 'Epoch = BossRushCoop.LocalReadyEpoch, Token = BossRushCoop.SceneToken', 'FlushRequests(0f);'), 'frozen real request owner')
    need(request, ('if (repeat)', 'if (IsRequestCurrent(pending) && pending.Command.Action == action && pending.Command.Value == value',
        'pending.Command.Text == text && pending.Command.Flag == flag) return true;'), 'reuse only an outstanding identical fact owned by this Core/connection')
    assert request.index('if (repeat)') < request.index('pendingRequests.Add(new PendingRequest'), 'reuse before allocating another periodic request'
    assert 'if (!BossRushCoop.SessionReady) return true;' not in request, 'unready UI must retain its request'
    current = body(source, 'private bool IsRequestCurrent(')
    need(current, ('pending.Core != null && ReferenceEquals(pending.Core, core)', 'core.gameObject.scene.handle == pending.Handle && core.gameObject.scene.isLoaded',
        'ReferenceEquals(pending.Service, BossRushCoop.NativeService)', 'ReferenceEquals(pending.Peer, BossRushCoop.NativeHostPeer)',
        'pending.Asset == BossRushCoop.LocalAssetId', 'pending.Epoch == BossRushCoop.LocalReadyEpoch', 'pending.Token == BossRushCoop.SceneToken'), 'real Core/connection/asset lease')
    flush = body(source, 'private void FlushRequests(')
    need(flush, ('!BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene', 'if (command.Sequence == 0)',
        'pending.Token = command.SceneToken = BossRushCoop.SceneToken;', 'command.Sequence = ++nextSequence;',
        'if (BossRushCoop.SendToHost(topic + ".command", command)) pending.Retry = 1f;'), 'retry until execution ACK')
    assert 'pendingRequests.Remove(pending)' not in flush[flush.index('if (command.Sequence == 0)'):], 'transport acceptance cannot remove request'
    command = body(source, 'private void HandleCommand(')
    need(command, ('!BossRushCoop.HasBoundScene', 'command.ReadyEpoch != player.ReadyEpoch', 'command.SceneToken != BossRushCoop.SceneToken',
        'if (command.Sequence < window.Next) { Acknowledge(command, message.SenderId); return; }',
        'if (command.Sequence - window.Next > 128) return;', 'while (window.Waiting.TryGetValue(window.Next, out next))',
        'execute(next);', 'window.Waiting.Remove(window.Next++);', 'Acknowledge(next, message.SenderId);'), 'ordered world execution')
    assert 'window.Next = command.Sequence;' not in command, 'later sequence cannot consume missing action'
    assert command.index('window.Waiting.Remove(window.Next++);') < command.index('execute(next);') < command.index('Acknowledge(next, message.SenderId);'), 'terminal dispatch prevents duplicate partial effects'
    assert 'finally { Acknowledge(next, message.SenderId); }' in command, 'failed dispatch must terminate transport retries without claiming business success'
    need(body(source, 'private void HandleAcknowledgement('), ('!message.IsFromHost', 'IsRequestCurrent(pending)',
        'pending.Command.Sequence == ack.Sequence', 'pending.Command.ReadyEpoch == ack.ReadyEpoch', 'pending.Command.SceneToken == ack.SceneToken'), 'authenticated exact ACK')
    need(body(source, 'internal void Publish()'), ('!BossRushCoop.HasBoundScene', 'SceneToken = BossRushCoop.SceneToken'), 'bound host capture')
    need(body(source, 'private void HandleSnapshot('), ('snapshot.SceneGeneration < generation', 'snapshot.SceneToken != BossRushCoop.SceneToken', 'pendingSnapshot = snapshot;', 'ApplyPendingSnapshot();'), 'token snapshot admission')
    need(body(source, 'private void ApplyPendingSnapshot()'), ('!BossRushCoop.HasBoundScene', 'snapshot.SceneGeneration != generation',
        'snapshot.SceneToken != BossRushCoop.SceneToken', 'apply(state);', 'pendingSnapshot = null;'), 'deferred actual Core projection')
    need(body(source, 'public void Dispose()'), ('acknowledgements.Dispose()', 'pendingRequests.Clear(); pendingSnapshot = null;'), 'owned cleanup')
    cases = (ROOT / 'tests/fixtures/CoopProtocol/ChannelScenes.cs').read_text(encoding='utf-8-sig')
    need(cases, ('BossRushCoop.FailSends = 1', 'BossRushCoop.DropAcks = 1', 'command(2)', 'command(1)', 'guest.Core.Destroyed = true',
        'guest.Bound = false', '"wrong-core"', '"new-current-core"'), 'executed variable boundary cases')
    need(cases, ('i < 10000', 'i < 512', 'client.RequestRepeat("paid"', '"service"', 'permits a later business retry'), 'actual repeated requests and distinct transaction quantities')
    callers = (
        ('ModeE/ModeERuntimeModule_Coop.cs', 'private void TickCoopModeE(', ('RequestRepeat("admission"', 'RequestRepeat("hire"')),
        ('ModeE/ModeERuntimeModule_CoopCoal.cs', 'private void TickCoopCoal(', ('RequestRepeat("coal"',)),
        ('ModeE/ModeERuntimeModule_CoopConsumables.cs', 'private void TickCoopUtilities(', ('RequestRepeat("utility"',)),
        ('ModeF/ModeFRuntimeModule_Coop.cs', 'private void TickCoopModeF(', ('RequestRepeat("admission"',)),
        ('ModeF/ModeFRuntimeModule_CoopFortifications.cs', 'private void TickCoopFortificationTransactions(', ('RequestRepeat("fortification"',)),
        ('ModeG/ModeGCoopRuntime.cs', 'private void RetryCoopAdmission(', ('RequestRepeat("admitted"',)),
        ('ModeH/ModeHRuntimeModule_Coop.cs', 'private void TryPrepareCoopBet(', ('RequestRepeat("funded"',)),
        ('ZombieMode/ZombieModeRuntimeModule_Coop.cs', 'public override void OnUpdate(', ('SendCoopZombieCommand("starter", repeat: true)', 'SendCoopZombieCommand("reward-done", runState.CurrentWave, repeat: true)')),
        ('ZombieMode/ZombieModeRuntimeModule_Coop.cs', 'private void SendCoopZombieCommand(', ('else if (repeat) coopZombieWorld.RequestRepeat(action, value, text, flag);', 'else coopZombieWorld.Request(action, value, text, flag);')),
    )
    for path, signature, tokens in callers:
        need(body(clean_source((ROOT / path).read_text(encoding='utf-8-sig')), signature), tokens, 'actual periodic caller ' + path)
    print('CoopGameplaySceneGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('CoopGameplaySceneGuard: FAIL - ' + str(error))
        raise SystemExit(1)
