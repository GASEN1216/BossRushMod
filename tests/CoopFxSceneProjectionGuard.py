"""内容表现与危险体只能消费当前实际 Core 的旅行 token。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index('{', source.index(signature)); depth = 1; end = start + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start + 1:end - 1]


def main():
    source = clean_source((ROOT / 'Integration/Coop/BossRushCoopFxRuntimeModule.cs').read_text(encoding='utf-8-sig'))
    for signature in ('private void Send(MethodInfo method, object[] args)',
                      'private void OnMessage(BossRushCoopMessage message)',
                      'internal static void SubmitHazardHit(Component component, DamageInfo damage)',
                      'private void OnHazardHit(BossRushCoopMessage message)',
                      'public override void OnUpdate(float deltaTime, float unscaledDeltaTime)',
                      'private void OnHazards(BossRushCoopMessage message)'):
        assert '!BossRushCoop.HasBoundScene' in body(source, signature), 'actual scene bound gate: ' + signature
    for signature in ('private void Send(MethodInfo method, object[] args)',
                      'internal static void SubmitHazardHit(Component component, DamageInfo damage)',
                      'public override void OnUpdate(float deltaTime, float unscaledDeltaTime)'):
        assert 'token = BossRushCoop.SceneToken' in body(source, signature), 'actual token producer: ' + signature
    for signature in ('private void OnMessage(BossRushCoopMessage message)',
                      'private void OnHazardHit(BossRushCoopMessage message)',
                      'private void OnHazards(BossRushCoopMessage message)'):
        value = body(source, signature)
        assert 'string.IsNullOrEmpty(packet.token)' in value and 'packet.token != BossRushCoop.SceneToken' in value, 'exact nonempty token consumer: ' + signature
    print('CoopFxSceneProjectionGuard: PASS')


if __name__ == '__main__':
    try: main()
    except AssertionError as error:
        print('CoopFxSceneProjectionGuard: FAIL - ' + str(error)); raise SystemExit(1)
