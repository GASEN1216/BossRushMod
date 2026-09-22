from pathlib import Path
import hashlib, subprocess, sys, xml.sax.saxutils
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'Build/runtime-regressions/AuditModeLifecycle'
sys.path.insert(0,str(ROOT/'tests'))
from cs_source_util import clean_source

def member(path, marker):
    raw=path.read_text(encoding='utf-8-sig')
    # brace parser uses production source utility only to locate method boundaries;
    # these methods have no brace characters in string literals.
    start=raw.index(marker); opening=raw.index('{',start); depth=0
    for i in range(opening,len(raw)):
        if raw[i]=='{': depth+=1
        elif raw[i]=='}':
            depth-=1
            if depth==0:
                hashes[str(path.relative_to(ROOT))]=hashlib.sha256(path.read_bytes()).hexdigest()
                return raw[start:i+1]
    raise ValueError(marker)

hashes={}
def execute(name, files, generated=''):
    out=OUT/name;out.mkdir(parents=True,exist_ok=True)
    if generated:
        (out/'Generated.cs').write_text(generated,encoding='utf-8');files=files+[out/'Generated.cs']
    for p in files:
        if not p.exists(): raise RuntimeError('Missing production/fixture source: '+str(p))
    incl=''.join('<Compile Include="'+xml.sax.saxutils.escape(str(p),{'"':'&quot;'})+'"/>' for p in files)
    project=out/'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+incl+'</ItemGroup></Project>',encoding='utf-8')
    code=subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release','--',str(ROOT)],cwd=ROOT)
    if code: raise SystemExit(code)

execute('transfer',[HERE/'Transfer.cs',ROOT/'ZombieMode/ZombieModeInventoryTransfer.cs'])
source=ROOT/'ZombieMode/ZombieModeRewardTriggerEffects.cs'
execute('projectile',[HERE/'Projectile.cs'],
    'using UnityEngine; using ItemStatsSystem; using Duckov.Utilities; namespace BossRush { public partial class ModBehaviour {'+member(source,'private bool TrySpawnZombieModePlayerSupportProjectile(')+'}}')
prod=ROOT/'ModeH/ModeHProductionCertification.cs'
writer='using System.Collections.Generic; namespace BossRush { internal sealed class ProductionStatusWriter {'+member(prod,'private void AppendEntryStatus(')+' internal void Capture(List<ModeHCommandCertificationStatusDto>s,string k,string e){AppendEntryStatus(s,k,e);} }}'
execute('report',[HERE/'Report.cs']+[ROOT/p for p in ['Common/Data/BossRushJsonValue.cs','Common/Data/JsonDataRegistry.cs','Utilities/SimpleJsonHelper.cs','ModeH/ModeHConfig.cs','ModeH/ModeHStateModel.cs','ModeH/ModeHStateDtos.cs','ModeH/ModeHContentModels.cs','ModeH/ModeHCanonicalDigest.cs','ModeH/ModeHContentCatalog.cs','ModeH/ModeHContentCatalogParsers.cs','ModeH/ModeHCommandCompatibilityRegistry.cs']],writer)
methods='\n'.join(member(prod,m) for m in ['private async Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> CreateOwnedDiagnosticAsync(', 'internal void Cancel()', 'private void ReleaseDiagnosticPair()', 'private void UnregisterDiagnostic('])
methods=methods.replace('Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle>','System.Threading.Tasks.Task<ModeHSpawnHandle>')
execute('ownership',[HERE/'Ownership.cs'],'using System; using UnityEngine; namespace BossRush { public partial class CertificationOwner {'+methods+'}}')
merchant=member(ROOT/'ModeE/ModeEMerchant.cs','private async UniTaskVoid SpawnModeEMerchant(').replace('async UniTaskVoid','async System.Threading.Tasks.Task')
execute('merchant_owner',[HERE/'MerchantOwner.cs'],'using System; using UnityEngine; namespace BossRush { public partial class ModBehaviour {'+merchant+'}}')
respawn=member(ROOT/'ModeF/ModeFRespawn.cs','private async UniTaskVoid RespawnModeFBossAsync(').replace('async UniTaskVoid','async System.Threading.Tasks.Task')
execute('respawn_owner',[HERE/'RespawnOwner.cs'],'using System; using UnityEngine; namespace BossRush { public partial class ModBehaviour {'+respawn+'}}')
cash='\n'.join(member(ROOT/'RandomEvents/RandomEventEffectsBridge_Loot.cs',m) for m in ['private async UniTaskVoid SpawnRandomEventCashPilesAsync(', 'private static void InvokeRandomEventCashCompletion('])
cash=cash.replace('async UniTaskVoid','async System.Threading.Tasks.Task').replace('UniTask.Yield()','System.Threading.Tasks.Task.Yield()')
execute('cash_owner',[HERE/'CashOwner.cs'],'using System; using UnityEngine; using UnityEngine.SceneManagement; using ItemStatsSystem; namespace BossRush { public partial class ModBehaviour {'+cash+'}}')
pause='\n'.join(member(ROOT/'ZombieMode/ZombieModeEntry.cs',m) for m in ['internal bool IsZombieModeRuntimePaused()', 'private void RefreshZombieModeRuntimePauseClock()', 'private void ResetZombieModeRuntimePauseClock()', 'internal float GetZombieModeRuntimeNow()'])
execute('pause_clock',[HERE/'PauseClock.cs'],'using UnityEngine; namespace BossRush { public partial class ModBehaviour {'+pause+'}}')
execute('wave_owner',[HERE/'WaveOwner.cs',ROOT/'WavesArena/WavesArenaRuntimeModule.cs',ROOT/'ModeD/ModeDRuntimeModule.cs'])
execute('milestone',[HERE/'Milestone.cs',ROOT/'LootAndRewards/InfiniteHellMilestoneDelivery.cs'])
f3=ROOT/'DebugAndTools/F3GameplayValidationAutotestStory.cs'
f3methods='\n'.join(member(f3,m) for m in ['private static bool TryReclaimAutotestItems(', 'private bool ClearAutotestSnapshotKey()'])
execute('autotest_restore',[HERE/'AutotestRestore.cs'],'using System; using Saves; namespace BossRush { public partial class RestoreProbe {'+f3methods+'}}')
readonly=ROOT/'DebugAndTools/F3GameplayValidationSkyIsland.cs'
buffer_methods='\n'.join(member(f3,m) for m in ['private static bool TryReclaimAutotestItems(', 'private bool ClearAutotestSnapshotKey()', 'private static string ReclaimAutotestItems(', 'private static int RemoveOwnedItems(', 'private static int RemoveFromBuffer(', 'private static int RemoveFromInventory('])
buffer_methods+='\n'+'\n'.join(member(readonly,m) for m in ['private static int CountOwnedItems(', 'private static int CountBufferedItems(', 'private static int CountInInventory('])
execute('autotest_buffer',[HERE/'AutotestBuffer.cs'],'using System; using System.Collections.Generic; using Saves; using ItemStatsSystem; namespace BossRush { public partial class BufferRestoreProbe {'+buffer_methods+'}}')
import json
(OUT/'sources.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
print('PASS AuditModeLifecycle production-linked regressions')
