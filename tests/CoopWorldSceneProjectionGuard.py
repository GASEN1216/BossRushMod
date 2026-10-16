"""Direct world projections revalidate actual native travel token at the mutation point."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT=Path(__file__).resolve().parents[1]


def read(path): return clean_source((ROOT/path).read_text(encoding='utf-8-sig'))


def body(source, signature):
    assert source.count(signature)==1, signature
    opening=source.index('{',source.index(signature));depth=0
    for end in range(opening,len(source)):
        depth+=(source[end]=='{')-(source[end]=='}')
        if depth==0:return re.sub(r'\s+',' ',source[opening+1:end]).strip()
    raise AssertionError('unclosed: '+signature)


def need(source, signature, *tokens):
    current=body(source,signature)
    for token in tokens: assert token in current,signature+': '+token
    return current


def main():
    random=read('RandomEvents/RandomEventsRuntimeModule_Coop.cs')
    need(random,'internal static void PublishCoopFx(','BossRushCoop.HasBoundScene','Token = BossRushCoop.SceneToken')
    need(random,'private void ReceiveCoopFx(','!BossRushCoop.HasBoundScene','fx.Token != BossRushCoop.SceneToken')
    effects=read('SkyIsland/SkyIslandCoopWorldEffects.cs')
    need(effects,'private void ApplyPlayerEffects(','!BossRushCoop.HasBoundScene','value.SceneToken != BossRushCoop.SceneToken')
    need(effects,'private void ReceiveSlow(','data.SceneToken != BossRushCoop.SceneToken')
    need(effects,'private void ReceiveWorld(','state.SceneToken == BossRushCoop.SceneToken','latest.SceneToken != state.SceneToken')
    need(effects,'private bool IsWorldCurrent()','BossRushCoop.HasBoundScene && latest != null && latest.SceneToken == BossRushCoop.SceneToken')
    need(effects,'private void ApplyWorld(','!IsWorldCurrent()')
    need(effects,'private void Publish()','SceneToken = BossRushCoop.SceneToken')
    session=read('SkyIsland/SkyIslandSessionCoop.cs')
    need(session,'private bool IsWorldSessionValid()','BossRushCoop.HasBoundScene')
    need(session,'private void OnCoopChallenge(','IsWorldSessionValid()','data.SceneToken == BossRushCoop.SceneToken')
    need(session,'private void OnCoopGates(','!BossRushCoop.HasBoundScene','data.SceneToken == BossRushCoop.SceneToken')
    need(session,'private void OnCoopState(','state.SceneToken == BossRushCoop.SceneToken')
    tick=need(session,'private void TickCoop()','!BossRushCoop.HasBoundScene','pendingCoopState.SceneToken == BossRushCoop.SceneToken')
    assert tick.index('coopChallengeDelivery.Tick(')<tick.index('!BossRushCoop.HasBoundScene'),'echo personal receipts must retry before world gate'
    need(session,'internal void ApplyCoopState(','!BossRushCoop.HasBoundScene','state.SceneToken != BossRushCoop.SceneToken')
    harvest=read('SkyIsland/SkyIslandFieldcraftCoop.cs')
    need(harvest,'private void AdmitHarvest(','!BossRushCoop.HasBoundScene','request.SceneToken != BossRushCoop.SceneToken')
    need(harvest,'private void OnCoopGatherState(','!BossRushCoop.HasBoundScene','state.SceneToken != BossRushCoop.SceneToken')
    tick=need(harvest,'internal void TickCoopHarvest()','SceneToken = BossRushCoop.SceneToken','!BossRushCoop.HasBoundScene')
    assert tick.index('coopHarvestDelivery.Tick(')<tick.index('!BossRushCoop.HasBoundScene'),'personal harvest ownership survives scene bind wait'
    assert 'HasBoundScene' not in body(harvest,'private bool ApplyCoopHarvest('),'personal harvest uses its original trip/asset owner'
    gnats=read('SkyIsland/SkyIslandGnatsCoop.cs')
    need(gnats,'private void ReceiveCoopGnats(','data.SceneToken != BossRushCoop.SceneToken')
    need(gnats,'private void ReceiveCoopEnvironment(','!BossRushCoop.HasBoundScene','data.SceneToken != BossRushCoop.SceneToken')
    need(gnats,'private void ReceiveCoopUse(','!BossRushCoop.HasBoundScene','data.SceneToken != BossRushCoop.SceneToken')
    need(gnats,'private bool IsCoopWorldCurrent()','BossRushCoop.HasBoundScene && coopLatest != null && coopLatest.SceneToken == BossRushCoop.SceneToken')
    need(gnats,'private void FrameCoopReplicas(','!IsCoopWorldCurrent()')
    tick=need(gnats,'private void TickCoopGnats()','!BossRushCoop.HasBoundScene','SceneToken = BossRushCoop.SceneToken')
    assert tick.index('coopGnatPersonal.Tick(')<tick.index('!BossRushCoop.HasBoundScene'),'personal bite/cull/cost receipt retry precedes world gate'
    assert 'HasBoundScene' not in body(gnats,'private bool ApplyCoopPersonal('),'personal gnat receipts cannot depend on world binding'
    contact=read('Integration/DragonKing/DragonKingCoopContactRuntimeModule.cs')
    need(contact,'private void ApplyBody(','!BossRushCoop.HasBoundScene','value.SceneToken != BossRushCoop.SceneToken')
    need(contact,'private void RefreshBody()','worldToken == BossRushCoop.SceneToken','worldToken = BossRushCoop.SceneToken;')
    need(contact,'private bool SendPush(','!BossRushCoop.HasBoundScene','value.SceneToken != BossRushCoop.SceneToken','SceneToken = value.SceneToken')
    need(contact,'private bool ApplyPush(','!BossRushCoop.HasBoundScene','value.SceneToken != worldToken')
    need(contact,'private void ReceiveBodyRequest(','!message.IsFromHost','value.Asset != BossRushCoop.LocalAssetId')
    update=need(contact,'public override void OnUpdate(','if (bodyRequested && bodyDirty) PublishBody();')
    assert 'nextBodyPublish' not in update,'no owner must not publish 4 Hz body traffic'
    need(contact,'private void PublishBody()','token = localBody != null ? Guid.NewGuid().ToString("N")','tokenUntil = Time.unscaledTime + 1f;')
    need(contact,'private bool ApplyPush(','Time.unscaledTime >= tokenUntil')
    need(contact,'private IEnumerator ApplyPushOverTime(','value.Token != token','BossSkillDamageRules.IsDodging(body)','Time.deltaTime')
    need(gnats,'private void ReceiveCoopGnats(','data.Revision <= coopReceivedRevision','coopKilled.Add(key)')
    assert 'coopLatest.Killed' not in body(gnats,'private void FrameCoopReplicas('),'death table must not be reparsed every rendered frame'
    tick=need(gnats,'private void TickCoopGnats()','coopRecentKilled','if (BossRushCoop.Broadcast(','coopRecentKilled.Dequeue();')
    assert 'coopKilled.ToArray' not in tick and 'new List<string>(coopKilled)' not in tick,'pose packets must not repeat whole-raid death table'
    assert 'PublishPlayerEffects(' not in body(effects,'private void OnLocalShot('),'gun totals use existing effects cadence instead of per-shot packets'
    descendant=read('Integration/DragonDescendant/DragonDescendantAbilities.cs')
    need(descendant,'private void RefreshPlayerReference()','BossRushCoop.GetCombatTarget(')
    rocket=read('Integration/DragonDescendant/DragonDescendantAbilities_ProjectilesAndGrenades.cs')
    need(rocket,'internal bool CanCompleteRocket(','BossRushCoop.IsPlayer(playerCharacter)')
    collision=read('Integration/DragonDescendant/DragonDescendantAbilities_CollisionAndIce.cs')
    need(collision,'private void OnTriggerEnter(','BossRushCoop.IsPlayer(character)','playersInTrigger[other] = character;')
    need(collision,'public void OnCollisionWithPlayer(','collisionTimes.TryGetValue(player, out lastHit)')
    need(collision,'private void ApplyKnockback(','DragonKingCoopContactRuntimeModule.Push(player,')
    print('CoopWorldSceneProjectionGuard: PASS')


if __name__=='__main__':
    try:main()
    except AssertionError as error:
        print('CoopWorldSceneProjectionGuard: FAIL - '+str(error));raise SystemExit(1)
