"""World projections wait for the actual bound Core; personal receipts remain independent."""
from pathlib import Path
import re
from cs_source_util import clean_source
ROOT=Path(__file__).resolve().parents[1]
def read(p):return clean_source((ROOT/p).read_text(encoding='utf-8-sig'))
def body(s,sig):
 assert s.count(sig)==1,sig
 a=s.index('{',s.index(sig));i=a+1;n=1
 while n:n+=(s[i]=='{')-(s[i]=='}');i+=1
 return re.sub(r'\s+',' ',s[a+1:i-1])
def need(s,sig,token):assert token in body(s,sig),sig+' missing '+token
def main():
 w=read('Integration/DeathWraith/DeathWraithCoop.cs')
 for sig in ('private void TickCoopWraithRequests()', 'private void ReceiveCoopWraithPart(', 'private bool IsCoopWraithSpawnCurrent(', 'private void BindCoopWraithReplicas()'):need(w,sig,'BossRushCoop.HasBoundScene')
 need(w,'private void BindCoopWraithReplicas()','coopWraithLatest.Generation != BossRushCoop.SceneGeneration')
 update=body(w,'public override void OnUpdate(')
 assert update.index('coopWraithClears.Tick(unscaledDeltaTime);')<update.index('if (!BossRushCoop.HasBoundScene) return;')<update.index('TickCoopWraithRequests();'),'wraith personal receipt retry must precede world gate'
 assert 'HasBoundScene' not in body(w,'private bool ApplyCoopWraithClear('),'legacy killed-record acknowledgement cannot depend on a world Core'
 p=read('PetNest/PetNestCoopRuntimeModule.cs')
 for sig in ('private void OnRequest(', 'private bool IsSpawnCurrent(', 'private void BindReplica(', 'public override void OnUpdate('):need(p,sig,'BossRushCoop.HasBoundScene')
 bind=body(p,'private void BindReplica(')
 assert bind.index('!BossRushCoop.HasBoundScene')<bind.index('!CurrentWorld(packet)')<bind.index('packet.ai == 0')<bind.index('BossRushCoop.FindAi'), 'pet removal and attach must both wait, then revalidate retained packet generation'
 state=body(p,'private void OnState(')
 assert 'Remove(packet.owner' not in state and state.index('pending[packet.owner] = packet;')<state.index('BindReplica(packet);'),'pet receive must retain full state including removal before attempting world effect'
 need(p,'private void OnAiSpawned(','BindReplica(packet);');need(p,'public override void OnUpdate(','BindReplica(packet);')
 need(p,'private async UniTaskVoid Spawn(','!IsSpawnCurrent(entry, master, run, scene)')
 assert 'HasBoundScene' not in body(p,'private void OnAward('),'personal experience receipt cannot depend on a world Core'
 n=read('Integration/NPCs/Common/CoopNpcWorld.cs');s=read('Integration/NPCs/Common/CoopNpcWorld_Spouses.cs')
 need(n,'internal void Tick()','!BossRushCoop.HasBoundScene');need(n,'private bool IsCoopNpcSceneCurrent(','BossRushCoop.HasBoundScene')
 for token in ('requestGeneration == generation','sceneGeneration == BossRushCoop.SceneGeneration','runId == BossRushCoop.RunId'):need(n,'private bool IsCoopNpcSceneCurrent(',token)
 need(n,'private async UniTaskVoid CreateReplica(','!IsCoopNpcSceneCurrent(requestGeneration)')
 need(s,'private async UniTaskVoid CreateWorldSpouse(','!IsCoopNpcSceneCurrent(requestGeneration)')
 need(s,'private async UniTask CreateSpouseReplica(','!IsCoopNpcSceneCurrent(requestGeneration)')
 need(s,'private bool SetLocalSpouseIntent(','if (!BossRushCoop.HasBoundScene) return true;')
 need(s,'private void OnSpouseRequest(','!BossRushCoop.HasBoundScene')
 c=read('Integration/Coop/BossRushCoopContentRuntimeModule.cs')
 for sig in ('private void OnSummon(', 'private void OnSummonCount(', 'internal static bool IsSummonOwnerCurrent(', 'internal static bool RequestSummon(', 'public override void OnUpdate('):need(c,sig,'BossRushCoop.HasBoundScene')
 assert 'HasBoundScene' not in body(c,'private void OnSummonAck('),'committed summon ACK must not be discarded while a new Core binds'
 assert 'HasBoundScene' not in body(c,'private void OnSnapshotApplied('),'actual native item KV restore is not a world-AI projection'
 need(w,'private void ReceiveCoopWraithPart(','part.Token != BossRushCoop.SceneToken')
 need(w,'private void ReceiveCoopWraithWorld(','world.Token != BossRushCoop.SceneToken')
 need(w,'private void BindCoopWraithReplicas()','coopWraithLatest.Token != BossRushCoop.SceneToken')
 need(w,'private bool IsCoopWraithPendingHere(','ReferenceEquals(request.Core, Duckov.Scenes.MultiSceneCore.Instance)')
 need(w,'private bool IsCoopWraithPendingHere(','request.Token == BossRushCoop.SceneToken')
 need(w,'private void QueueCoopDeathWraith(','Core = Duckov.Scenes.MultiSceneCore.Instance')
 need(w,'private void QueueCoopDeathWraith(','Run = BossRushCoop.RunId')
 need(w,'private bool IsCoopWraithPendingHere(','request.Run == BossRushCoop.RunId')
 need(w,'private void TickCoopWraithRequests()','if (string.IsNullOrEmpty(request.Run)) request.Run = BossRushCoop.RunId;')
 refresh=body(w,'private void RefreshCoopWraithWorld()')
 assert 'sameRun' not in refresh and 'if (IsCoopWraithPendingHere(request))' in refresh,'each pending tomb must retain its own room/Core identity across late scope refresh'
 need(w,'private void TickCoopWraithRequests()','if (string.IsNullOrEmpty(request.Token)) request.Token = BossRushCoop.SceneToken;')
 need(w,'private bool IsCoopWraithSpawnCurrent(','entry.Token == BossRushCoop.SceneToken')
 need(p,'private static bool CurrentWorld(','packet.sceneToken == BossRushCoop.SceneToken')
 for sig in ('private void OnState(', 'private void OnRequest('):need(p,sig,'!CurrentWorld(packet)')
 need(p,'private bool IsSpawnCurrent(','entry.SceneToken == BossRushCoop.SceneToken')
 need(n,'private void OnState(','state.Token != BossRushCoop.SceneToken')
 need(n,'private bool IsCoopNpcSceneCurrent(','sceneToken == BossRushCoop.SceneToken')
 need(s,'private void OnSpouseRequest(','intent.Token != BossRushCoop.SceneToken')
 need(c,'private static bool CurrentSummonWorld(','packet.token == BossRushCoop.SceneToken')
 need(c,'private void OnSummon(','!CurrentSummonWorld(packet)')
 need(c,'private void OnSummonCount(','!CurrentSummonWorld(packet)')
 need(c,'internal static bool IsSummonOwnerCurrent(','token == BossRushCoop.SceneToken')
 for path in ('Integration/Frostmourne/FrostmourneAction_Coop.cs','Integration/NewWeapons/SummonStaff/SummonStaffAction.cs'):
  need(read(path),'internal async UniTask SpawnForCoop(','coopToken = BossRushCoop.SceneToken;')
 for path in ('Integration/Frostmourne/FrostmourneAction.cs','Integration/NewWeapons/SummonStaff/SummonStaffAction.cs'):
  assert 'IsSummonOwnerCurrent(player, coopAsset, coopToken)' in read(path),path+' await must use captured travel token'
 for path,signature,predicate,count in (
  ('Integration/Frostmourne/FrostmourneAction.cs','private async UniTask SpawnZombiesAsync(','IsSpawnRequestStillValid',4),
  ('Integration/NewWeapons/SummonStaff/SummonStaffAction.cs','private async UniTask SpawnAlliesAsync(','IsRequestValid',3)):
  spawn=body(read(path),signature)
  assert 'var creationLease = new CoopBossCreationLease();' in spawn,path+' local cast must freeze the original world before its factory await'
  checks=re.findall(r'if\s*\(([^{};]*?' + predicate + r'\([^;{}]*?\))\)',spawn)
  assert len(checks)==count and all(check.startswith('!creationLease.IsValid() || !'+predicate+'(') for check in checks),path+' every pre/post factory validation must reject revoked world creation'
  assert spawn.index('new CoopBossCreationLease()')<spawn.index('await '),path+' lease capture must precede the asynchronous factory'
 print('CoopPersonalSceneBindingGuard PASS')
if __name__=='__main__':
 try:main()
 except AssertionError as e:print('CoopPersonalSceneBindingGuard FAIL:',e);raise SystemExit(1)
