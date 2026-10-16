"""变异世界投影、击杀续作及即时个人治疗必须属于当前原生旅行。"""
from CoopPlayerAliveGuard import body, norm, read

def main():
    source = read('Integration/Mutators/MutatorCoopRuntimeModule.cs')
    update = norm(body(source, 'public override void OnUpdate('))
    assert 'kills.Tick(unscaledDeltaTime);' in update, 'personal receipt retries stay independent of world projection'
    assert 'view != null && !string.IsNullOrEmpty(BossRushCoop.SceneToken) && !HasCurrentView()' in update, 'new native scene retires cached old view before generation publication'
    assert 'MutatorManager.RemoveAll(); MutatorUI.HideAll(); view = null; appliedEpoch = null;' in update, 'retire stale modifiers and cached projection together'
    assert 'if (!BossRushCoop.HasBoundScene) return;' in update, 'unbound Core cannot reapply cached personal/enemy modifiers'
    apply = norm(body(source, 'private void Apply('))
    assert '!BossRushCoop.HasBoundScene' in apply, 'actual state consumer checks binding'
    for token in ('viewRun = BossRushCoop.RunId;', 'viewToken = BossRushCoop.SceneToken;', 'viewGeneration = BossRushCoop.SceneGeneration;'):
        assert token in apply, 'freeze accepted projection owner: ' + token
    current = norm(body(source, 'private bool HasCurrentView()'))
    for token in ('viewRun == BossRushCoop.RunId', 'viewGeneration == BossRushCoop.SceneGeneration', '!string.IsNullOrEmpty(viewToken)', 'viewToken == BossRushCoop.SceneToken'):
        assert token in current, 'cached view owner comparison: ' + token
    valid = norm(body(source, 'private bool IsDispatchValid('))
    assert 'BossRushCoop.HasBoundScene && !string.IsNullOrEmpty(token)' in valid and 'token == BossRushCoop.SceneToken' in valid, 'deferred actual kill cannot enter another native world'
    dispatch = norm(body(source, 'private IEnumerator DispatchKill('))
    assert dispatch.count('IsDispatchValid(playerId, source, expectedEpoch, scene, token, asset, readyEpoch)') == 3 and 'Token = token' in dispatch, 'pause, before delivery and post-callback world continuation retain original token and owner'
    entry = norm(body(source, 'internal static bool TryHandleEnemyKilled('))
    assert 'health.GetInstanceID(), player.AssetId, player.ReadyEpoch)' in entry, 'actual death entry freezes original asset and connection incarnation'
    assert '!string.IsNullOrEmpty(asset) && player.AssetId == asset' in valid, 'deferred kill cannot cross assets on the same body'
    assert '!string.IsNullOrEmpty(readyEpoch) && player.ReadyEpoch == readyEpoch' in valid, 'A to B to A and reconnected bodies cannot inherit an old continuation'
    assert 'Position = position }, asset);' in dispatch, 'actual personal delivery targets the frozen asset explicitly'
    kill = norm(body(source, 'private bool ApplyKill('))
    for token in ('if (string.IsNullOrEmpty(BossRushCoop.SceneToken) || !BossRushCoop.HasBoundScene) return false;', 'if (kill.Token != BossRushCoop.SceneToken) return true;', 'if (BossRushCoop.IsClient && !HasCurrentView()) return false;', 'return MutatorManager.ApplyCoopPersonalKill(kill.Position);'):
        assert token in kill, 'instant heal scope and actual application: ' + token
    print('MutatorCoopSceneOwnershipGuard: PASS')

if __name__ == '__main__':
    main()
