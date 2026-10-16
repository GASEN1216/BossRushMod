"""Burst commands retain full actions/ACKs while sharing the next world capture."""
from pathlib import Path
from cs_source_util import clean_source
from CoopGameplaySceneGuard import body

ROOT = Path(__file__).resolve().parents[1]


def main():
    source = clean_source((ROOT / 'Utilities/CoopGameplayChannel.cs').read_text(encoding='utf-8-sig'))
    command = body(source, 'private void HandleCommand(')
    assert 'publishDirty = true;' in command and 'Publish();' not in command, 'commands mark dirty without per-command world serialization'
    assert command.index('window.Waiting.Remove(window.Next++);') < command.index('execute(next);'), 'consume sequence before potentially partial business effects'
    assert 'finally { Acknowledge(next, message.SenderId); }' in command, 'ACK remains immediate on partial failure'
    assert 'if (publishTimer > 0f && !publishDirty) return;' in body(source, 'internal void Tick('), 'dirty result publishes on next module tick without timer delay'
    assert 'publishDirty = false;' in body(source, 'internal void Publish()'), 'successful capture clears dirty once'
    d = clean_source((ROOT / 'ModeD/ModeDRuntimeModule_Coop.cs').read_text(encoding='utf-8-sig'))
    tick = body(d, 'private void TickCoopModeD(')
    assert 'coopChannel.RequestRepeat("admission", text: coopEpoch, flag: decision);' in tick, 'D automatic admission retry reuses original in-flight request'
    assert 'coopModeDStarterSent.TryGetValue(player.Id, out sent)' in tick, 'D starter receipt allocates once per participant attempt'
    for path, token in (('ModeE/ModeERuntimeModule_Coop.cs', '!participant.StarterSent && coopPersonal.Deliver('),
                        ('ModeF/ModeFRuntimeModule_Coop.cs', '!state.StarterSent && coopPersonal.Deliver(')):
        assert token in clean_source((ROOT / path).read_text(encoding='utf-8-sig')), 'starter delivery queue owns retries: ' + path
    assert 'confirmed.Asset != coopModeDLocalAsset || confirmed.Accepted != decision' in tick, 'D stops automatic reports only on matching original asset and business decision'
    e = clean_source((ROOT / 'ModeE/ModeERuntimeModule_Coop.cs').read_text(encoding='utf-8-sig'))
    entry = body(e, 'private void TickCoopModeE(')
    assert 'confirmed.AssetId != coopModeELocalAsset' in entry and '!(admitted ? confirmed.Admitted : confirmed.Declined)' in entry, 'E retries remain open until exact asset admission business confirmation'
    print('CoopGameplayPublicationGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('CoopGameplayPublicationGuard: FAIL - ' + str(error))
        raise SystemExit(1)
