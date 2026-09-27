from pathlib import Path
import hashlib, os, subprocess, sys, xml.sax.saxutils
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=Path(os.environ.get('BOSSRUSH_FIXTURE_OUTPUT', str(ROOT/'Build/runtime-regressions/AuditModeLifecycle')))
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

def declaration(path, marker):
    raw=path.read_text(encoding='utf-8-sig')
    start=raw.index(marker)
    end=raw.index(';',start)+1
    hashes[str(path.relative_to(ROOT))]=hashlib.sha256(path.read_bytes()).hexdigest()
    return raw[start:end]

def type_block(path, marker):
    raw=path.read_text(encoding='utf-8-sig')
    start=raw.index(marker)
    opening=raw.index('{',start);depth=0
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
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+incl+'</ItemGroup></Project>',encoding='utf-8')
    code=subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release','--',str(ROOT)],cwd=ROOT)
    if code: raise SystemExit(code)

transfer_module=ROOT/'ZombieMode/ZombieModeRuntimeModule.cs'
transfer_inventory_module=ROOT/'ZombieMode/ZombieModeRuntimeModule_InventoryTransfer.cs'
transfer_host=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
transfer_bridge=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
enemy_runtime=ROOT/'ZombieMode/ZombieModeEnemyRuntime.cs'
enemy_module=ROOT/'ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs'
enemy_module_fields='\n'.join(declaration(enemy_module,m) for m in [
    'private readonly HashSet<int> zombieModeEnemyInstanceIds',
    'private readonly Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId',
])
enemy_module_methods='\n'.join(member(enemy_module,m) for m in [
    'internal bool IsZombieModeKnownEnemy(CharacterMainControl character)',
    'internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)',
    'internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)',
    'internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)',
    'internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)',
    'internal void ClearZombieModeEnemyInstanceIds()',
    'internal ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(',
    'internal static void RestoreZombieModeVisualScale(',
    'internal static void ReleaseZombieModeFootMarker(',
])
enemy_host_methods='\n'.join(member(ROOT/'ZombieMode/ZombieModeCombatHostBridge.cs',m) for m in [
    'internal bool IsZombieModeKnownEnemy(CharacterMainControl character)',
    'internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)',
    'internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)',
    'internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)',
    'internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)',
    'internal void ClearZombieModeEnemyInstanceIds()',
    'private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(',
    'internal int CalculateZombieModeEnemyPurificationPointsForRuntimeModule(',
    'internal void RestoreZombieModeVisualScaleForRuntimeModule(',
    'internal void ReleaseZombieModeFootMarkerForRuntimeModule(',
    'private static void RestoreZombieModeVisualScale(',
    'private static void ReleaseZombieModeFootMarker(',
])
transfer_module_methods='\n'.join([member(transfer_module,'internal bool IsZombieModeRunValid(int runId)')]+[member(transfer_inventory_module,m) for m in [
    'internal bool PrepareZombieModeInventoryTransfer(int runId)',
    'internal bool TryMoveZombieModeEntryItemToStorageOrInbox(Item item)',
    'internal void RollbackZombieModeInventoryTransfer()',
    'internal List<Item> CollectZombieModeTopLevelPlayerItems()',
    'internal void AddZombieModeTransferCandidate(List<Item> result, Item item)',
]])
transfer_host_methods='\n'.join(member(transfer_host,m) for m in [
    'private bool PrepareZombieModeInventoryTransferShell(int runId)',
    'private bool TryMoveZombieModeEntryItemToStorageOrInbox(Item item)',
    'private void RollbackZombieModeInventoryTransferShell()',
    'private List<Item> CollectZombieModeTopLevelPlayerItems()',
    'private void AddZombieModeTransferCandidate(List<Item> result, Item item)',
])
transfer_bridge_method=member(transfer_bridge,'internal bool PrepareZombieModeInventoryTransferForRuntimeModule(int runId)')
transfer='''using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace BossRush {
public enum ZombieModeLifecyclePhase { None, InitializingRun, WaitingStarterChoice, Active }
public enum ZombieModeRunOnlyObjectKind { Enemy, Boss }
public enum ZombieModeBossKind { Titan }
public enum ZombieModeEnemyKind { Normal, Elite, Special }
public enum ZombieModeSpecialKind { None }
public enum ZombieModeEliteAffix { Commander, Swift }
internal static class ZombieModePhaseGuards { internal static bool IsActive(ZombieModeLifecyclePhase phase) { return phase == ZombieModeLifecyclePhase.Active; } }
internal sealed class ZombieModeRunState { internal int RunId; internal int SceneBuildIndex = -1; internal bool IsCleaningUp; internal ZombieModeLifecyclePhase LifecyclePhase = ZombieModeLifecyclePhase.Active; }
public sealed class ZombieModeEntryTransaction {
 public bool InventoryTransferStarted;
 public readonly List<Item> InventoryTransferredItems = new List<Item>();
 public readonly List<ItemTreeData> InventoryTransferredInboxItems = new List<ItemTreeData>();
}
internal sealed partial class ZombieModeRuntimeModule {
 private ModBehaviour owner; private ZombieModeRunState runState; private ZombieModeEntryTransaction entryTransaction;
'''+enemy_module_fields+'''
 internal bool WasIndexedAtLastRunOnlyRegistration; internal Action LastRunOnlyCleanup; internal int RunOnlyRegistrationCount;
 internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState runState, ZombieModeEntryTransaction entryTransaction) { this.owner = owner; this.runState = runState; this.entryTransaction = entryTransaction; }
 internal void RegisterZombieModeRunOnlyObject(int runId, ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanupAction) {
  RunOnlyRegistrationCount++; LastRunOnlyCleanup = cleanupAction;
  ZombieModeEnemyRuntimeMarker marker = target as ZombieModeEnemyRuntimeMarker;
  WasIndexedAtLastRunOnlyRegistration = marker != null && IsZombieModeKnownEnemy(marker.Owner);
  TransferTrace.Add("runonly-register");
 }
'''+enemy_module_methods+'''
'''+transfer_module_methods+'''
}
public partial class ModBehaviour {
 private ZombieModeRuntimeModule zombieModeRuntimeModule; private ZombieModeEntryTransaction zombieModeEntryTransaction;
 internal void AttachTransferTestModule(ZombieModeRuntimeModule module, ZombieModeEntryTransaction transaction) { zombieModeRuntimeModule = module; zombieModeEntryTransaction = transaction; }
 public bool InventoryTransferStarted { get { return zombieModeEntryTransaction != null && zombieModeEntryTransaction.InventoryTransferStarted; } }
 public static void DevLog(string message) { Console.WriteLine(message); }
 private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind) { TransferTrace.Add("calculate-points:" + isBoss + ":" + enemyKind); return 733; }
'''+transfer_host_methods+'\n'+transfer_bridge_method+'''
'''+enemy_host_methods+'''
 public bool IsKnownEnemyForTest(CharacterMainControl character) { return IsZombieModeKnownEnemy(character); }
 public bool TryGetKnownMarkerForTest(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker) { return TryGetZombieModeKnownEnemyMarker(character, out marker); }
 public void RegisterEnemyIndexForTest(CharacterMainControl character) { RegisterZombieModeEnemyInstanceId(character); }
 public void UnregisterEnemyIndexForTest(CharacterMainControl character) { UnregisterZombieModeEnemyInstanceId(character); }
 public void ClearEnemyIndexForTest() { ClearZombieModeEnemyInstanceIds(); }
 public ZombieModeEnemyRuntimeMarker RegisterEnemyForTest(int runId, CharacterMainControl enemy, bool isBoss = false, ZombieModeBossKind bossKind = ZombieModeBossKind.Titan, int overridePointValue = -1, ZombieModeEnemyKind enemyKind = ZombieModeEnemyKind.Normal, ZombieModeSpecialKind specialKind = ZombieModeSpecialKind.None, List<ZombieModeEliteAffix> eliteAffixes = null) { return RegisterZombieModeEnemyRuntimeShell(runId, enemy, isBoss, bossKind, overridePointValue, enemyKind, specialKind, eliteAffixes); }
 public int RunOnlyRegistrationCountForTest { get { return zombieModeRuntimeModule == null ? 0 : zombieModeRuntimeModule.RunOnlyRegistrationCount; } }
 public bool WasIndexedAtRunOnlyRegistrationForTest { get { return zombieModeRuntimeModule != null && zombieModeRuntimeModule.WasIndexedAtLastRunOnlyRegistration; } }
 public void RunLastCleanupForTest() { if (zombieModeRuntimeModule != null && zombieModeRuntimeModule.LastRunOnlyCleanup != null) zombieModeRuntimeModule.LastRunOnlyCleanup(); }
 public bool Prepare() { return PrepareZombieModeInventoryTransferShell(1); }
 public bool PrepareViaRuntimeBridge() { return PrepareZombieModeInventoryTransferForRuntimeModule(1); }
 public void Rollback() { RollbackZombieModeInventoryTransferShell(); }
}
}'''
execute('transfer',[HERE/'Transfer.cs',ROOT/'Utilities/RunScopedRegistry.cs'],transfer)
source=ROOT/'ZombieMode/ZombieModeRewardTriggerEffects.cs'
execute('projectile',[HERE/'Projectile.cs'],
    'using UnityEngine; using ItemStatsSystem; using Duckov.Utilities; namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {'+member(source,'private bool TrySpawnZombieModePlayerSupportProjectile(')+'}}')
prod=ROOT/'ModeH/ModeHProductionCertification.cs'
writer='using System.Collections.Generic; namespace BossRush { internal sealed class ProductionStatusWriter {'+member(prod,'private void AppendEntryStatus(')+' internal void Capture(List<ModeHCommandCertificationStatusDto>s,string k,string e){AppendEntryStatus(s,k,e);} }}'
execute('report',[HERE/'Report.cs']+[ROOT/p for p in ['Common/Data/BossRushJsonValue.cs','Common/Data/JsonDataRegistry.cs','Utilities/SimpleJsonHelper.cs','ModeH/ModeHConfig.cs','ModeH/ModeHStateModel.cs','ModeH/ModeHStateDtos.cs','ModeH/ModeHContentModels.cs','ModeH/ModeHCanonicalDigest.cs','ModeH/ModeHContentCatalog.cs','ModeH/ModeHContentCatalogParsers.cs','ModeH/ModeHCommandCompatibilityRegistry.cs']],writer)
methods='\n'.join(member(prod,m) for m in ['private async Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> CreateOwnedDiagnosticAsync(', 'internal void Cancel()', 'private void ReleaseDiagnosticPair()', 'private void UnregisterDiagnostic('])
methods=methods.replace('Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle>','System.Threading.Tasks.Task<ModeHSpawnHandle>')
execute('ownership',[HERE/'Ownership.cs'],'using System; using UnityEngine; namespace BossRush { public partial class CertificationOwner {'+methods+'}}')
merchant=member(ROOT/'Utilities/ModeEFMerchantRuntime.cs','internal async UniTaskVoid SpawnModeEMerchant(').replace('async UniTaskVoid','async System.Threading.Tasks.Task')
merchant_owner_methods = []
for mode, lifecycle, entry, signatures in [
    ('E', 'ModeE/ModeERuntimeModule.cs', 'ModeE/ModeEStartup.cs', ['private int BeginModeESession()', 'private void InvalidateModeESession()', 'internal bool IsModeESessionStillValid(', 'internal bool IsModeEOrModeFSpawnSessionStillValid(']),
    ('F', 'ModeF/ModeFRuntimeModule.cs', 'ModeF/ModeFEntry.cs', ['private int BeginModeFSession()', 'private void InvalidateModeFSession()', 'internal bool IsModeFSessionStillValid(']),
]:
    methods = '\n'.join(member(ROOT/lifecycle, sig) for sig in ['public override void OnAwake(', 'public override void OnDestroy()'])
    methods += '\n' + '\n'.join(member(ROOT/entry, sig) for sig in signatures)
    merchant_owner_methods.append('internal sealed partial class Mode'+mode+'RuntimeModule {'+methods+'}')
execute('merchant_owner',[HERE/'MerchantOwner.cs'],'using System; using UnityEngine; namespace BossRush { internal sealed partial class ModeEFMerchantRuntime {'+merchant+'}'+''.join(merchant_owner_methods)+'}')
merchant_lifecycle='\n'.join(member(ROOT/'Utilities/ModeEFMerchantRuntime.cs', signature) for signature in ['internal void CleanupModeEMerchant()', 'private System.Collections.IEnumerator CacheAllModeFShopItemInstancesAsync()'])
execute('merchant_shared_lifecycle',[HERE/'MerchantSharedLifecycle.cs', ROOT/'Utilities/RunScopedRegistry.cs'],'using System; using System.Collections.Generic; using UnityEngine; using Duckov.Economy; using ItemStatsSystem; namespace BossRush { internal sealed partial class ModeEFMerchantRuntime {'+merchant_lifecycle+'}}')
respawn=member(ROOT/'ModeF/ModeFRespawn.cs','private async UniTaskVoid RespawnModeFBossAsync(').replace('async UniTaskVoid','async System.Threading.Tasks.Task')
execute('respawn_owner',[HERE/'RespawnOwner.cs'],'using System; using UnityEngine; namespace BossRush { internal sealed partial class ModeFRuntimeModule {'+respawn+'}}')
cash='\n'.join(member(ROOT/'RandomEvents/RandomEventEffectsBridge_Loot.cs',m) for m in ['private async UniTaskVoid SpawnRandomEventCashPilesAsync(', 'private static void InvokeRandomEventCashCompletion('])
cash=cash.replace('async UniTaskVoid','async System.Threading.Tasks.Task').replace('UniTask.Yield()','System.Threading.Tasks.Task.Yield()')
execute('cash_owner',[HERE/'CashOwner.cs'],'using System; using UnityEngine; using UnityEngine.SceneManagement; using ItemStatsSystem; namespace BossRush { internal sealed partial class RandomEventsRuntimeModule {'+cash+'}}')
runtime_module=ROOT/'ZombieMode/ZombieModeRuntimeModule.cs'
entry=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
pause_methods='\n'.join(member(runtime_module,m) for m in [
    'internal void TickZombieMode(float deltaTime)',
    'internal bool IsZombieModeGamePaused()',
    'internal bool IsZombieModeRuntimePaused()',
    'internal void RefreshZombieModeRuntimePauseClock()',
    'internal void ResetZombieModeRuntimePauseClock()',
    'internal float GetZombieModeRuntimeNow()',
])
host_methods='\n'.join(member(entry,m) for m in [
    'private void TickZombieMode(float deltaTime)',
    'internal bool IsZombieModeGamePaused()',
    'internal bool IsZombieModeRuntimePaused()',
    'private void RefreshZombieModeRuntimePauseClock()',
    'private void ResetZombieModeRuntimePauseClock()',
    'internal float GetZombieModeRuntimeNow()',
])
pause='''using System; using UnityEngine; using Duckov.UI; namespace BossRush {
internal enum ZombieModeLifecyclePhase { None, Active }
internal static class ZombieModePhaseGuards { internal static bool IsRunActive(ZombieModeLifecyclePhase phase) { return phase == ZombieModeLifecyclePhase.Active; } }
internal sealed class ZombieModeRunState { internal int RunId = 1; internal ZombieModeLifecyclePhase LifecyclePhase = ZombieModeLifecyclePhase.Active; }
internal sealed partial class ZombieModeRuntimeModule {
 private ModBehaviour owner; private ZombieModeRunState runState;
 private float runtimePausedDuration; private float runtimePauseStartTime = -1f; private int runtimePauseRunId;
 internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState runState) { this.owner = owner; this.runState = runState; }
'''+pause_methods+'''\n}
public partial class ModBehaviour { private ZombieModeRuntimeModule zombieModeRuntimeModule;
'''+host_methods+'''\n}
}'''
execute('pause_clock',[HERE/'PauseClock.cs'],pause)
duration_module=ROOT/'ZombieMode/ZombieModeRuntimeModule.cs'
duration_host=ROOT/'ZombieMode/ZombieModeRewardHostBridge.cs'
duration_bridge=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
duration_module_members='\n'.join([
    declaration(duration_module,'private const int ZombieModePreparationDurationMinimumSeconds ='),
    declaration(duration_module,'private const int ZombieModePreparationDurationMaximumSeconds ='),
    declaration(duration_module,'private static readonly int[] ZombieModePreparationDurationOptions ='),
    member(duration_module,'internal bool IsZombieModeRunValid(int runId)'),
    member(duration_module,'internal int GetZombieModeSelectedPreparationDuration(int runId)'),
    member(duration_module,'internal void OpenZombieModePreparationDurationEditor(int runId)'),
    member(duration_module,'internal void SetZombieModePreparationDuration(int runId, int seconds)'),
])
duration_host_members='\n'.join(member(duration_host,signature) for signature in [
    'public int GetZombieModeSelectedPreparationDuration(int runId)',
    'public void OpenZombieModePreparationDurationEditor(int runId)',
    'public void SetZombieModePreparationDuration(int runId, int seconds)',
])
duration_show_bridge=member(duration_bridge,'internal void ShowZombieModeRewardSelectionForRuntimeModule(')
duration='''using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace BossRush {
public enum ZombieModeLifecyclePhase { None, InitializingRun, WaitingStarterChoice, Active }
public enum ZombieModeCombatPhase { None, RewardSelection }
internal static class ZombieModePhaseGuards { internal static bool IsActive(ZombieModeLifecyclePhase phase) { return phase == ZombieModeLifecyclePhase.Active; } }
internal sealed class ZombieModeRewardNode { internal bool BossNode; }
internal sealed class ZombieModeRunState {
 internal int RunId; internal int SceneBuildIndex = -1; internal bool IsCleaningUp;
 internal ZombieModeLifecyclePhase LifecyclePhase = ZombieModeLifecyclePhase.Active;
 internal ZombieModeCombatPhase CombatPhase = ZombieModeCombatPhase.RewardSelection;
 internal int SelectedPreparationDurationSeconds; internal ZombieModeRewardNode CurrentRewardNode;
}
internal static class ZombieModeTuning { internal const float PreparationCountdownSeconds = 45f; }
internal sealed partial class ZombieModeRuntimeModule {
 private ModBehaviour owner; private ZombieModeRunState runState;
 internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState runState) { this.owner = owner; this.runState = runState; }
'''+duration_module_members+'''
}
public partial class ModBehaviour {
 private ZombieModeRuntimeModule zombieModeRuntimeModule;
 internal bool RewardSelectionShown; internal int ShownRunId; internal bool ShownBossNode; internal bool ShownExpanded;
 internal void AttachTestModule(ZombieModeRuntimeModule module) { zombieModeRuntimeModule = module; }
 private void ShowZombieModeRewardSelection(int runId, bool bossNode, bool restEditorExpanded = false) {
  RewardSelectionShown = true; ShownRunId = runId; ShownBossNode = bossNode; ShownExpanded = restEditorExpanded;
 }
'''+duration_show_bridge+'\n'+duration_host_members+'''
}
}
namespace UnityEngine {
 public static class Mathf { public static int RoundToInt(float value) { return (int)Math.Round(value); } public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); } }
}
namespace UnityEngine.SceneManagement {
 public struct Scene { public int buildIndex; }
 public static class SceneManager { public static Scene ActiveScene; public static Scene GetActiveScene() { return ActiveScene; } }
}
public static class Program {
 private static int checks;
 private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); }
 public static void Main() {
  var owner = new BossRush.ModBehaviour(); var state = new BossRush.ZombieModeRunState { RunId = 7, CurrentRewardNode = new BossRush.ZombieModeRewardNode { BossNode = true } };
  var module = new BossRush.ZombieModeRuntimeModule(owner, state); owner.AttachTestModule(module);
  Check(owner.GetZombieModeSelectedPreparationDuration(7) == 45, "unset run duration keeps the 45-second fallback");
  Check(owner.GetZombieModeSelectedPreparationDuration(6) == 45, "stale run duration keeps the fallback");
  state.SelectedPreparationDurationSeconds = 1;
  Check(owner.GetZombieModeSelectedPreparationDuration(7) == 15, "stored duration clamps to the 15-second minimum");
  state.SelectedPreparationDurationSeconds = 301;
  Check(owner.GetZombieModeSelectedPreparationDuration(7) == 300, "stored duration clamps to the 300-second maximum");
  state.SelectedPreparationDurationSeconds = 45;
  for (int seconds = 15; seconds <= 300; seconds += 15) {
   owner.RewardSelectionShown = false; owner.SetZombieModePreparationDuration(7, seconds);
   Check(state.SelectedPreparationDurationSeconds == seconds && owner.RewardSelectionShown && owner.ShownRunId == 7 && owner.ShownBossNode && !owner.ShownExpanded,
    "accepted option persists and redraws the current Boss node collapsed: " + seconds);
  }
  int previous = state.SelectedPreparationDurationSeconds; owner.RewardSelectionShown = false;
  owner.SetZombieModePreparationDuration(7, 151);
  Check(state.SelectedPreparationDurationSeconds == previous && !owner.RewardSelectionShown, "non-option duration is ignored");
  owner.RewardSelectionShown = false; owner.OpenZombieModePreparationDurationEditor(7);
  Check(owner.RewardSelectionShown && owner.ShownRunId == 7 && owner.ShownBossNode && owner.ShownExpanded, "editor opens for the current Boss reward node expanded");
  owner.RewardSelectionShown = false; state.CurrentRewardNode = null; owner.OpenZombieModePreparationDurationEditor(7);
  Check(!owner.RewardSelectionShown, "editor does not open without a reward node");
  state.CurrentRewardNode = new BossRush.ZombieModeRewardNode { BossNode = false }; state.CombatPhase = BossRush.ZombieModeCombatPhase.None;
  owner.OpenZombieModePreparationDurationEditor(7); owner.SetZombieModePreparationDuration(7, 60);
  Check(!owner.RewardSelectionShown && state.SelectedPreparationDurationSeconds == previous, "editor and setter stay closed outside reward selection");
  state.CombatPhase = BossRush.ZombieModeCombatPhase.RewardSelection; state.IsCleaningUp = true;
  owner.SetZombieModePreparationDuration(7, 60); owner.OpenZombieModePreparationDurationEditor(7);
  Check(!owner.RewardSelectionShown && state.SelectedPreparationDurationSeconds == previous, "cleaning-up run rejects editor and duration updates");
  Console.WriteLine("AuditModeLifecycle preparation duration: " + checks + " PASS / 0 FAIL");
 }
}'''
execute('preparation_duration',[],duration)
models=ROOT/'ZombieMode/ZombieModeModels.cs'
cleanup=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
run_only_module=ROOT/'ZombieMode/ZombieModeRuntimeModule.cs'
run_only_bridges=ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs'
run_only_module_methods='\n'.join(member(run_only_module,m) for m in [
    'internal void RegisterZombieModeRunOnlyObject(',
    'internal void PruneZombieModeRunOnlyEnemyRecords(',
    'internal void RemoveZombieModeRunOnlyObjectRecord(',
    'internal void PruneZombieModeUnknownRunOnlyRecords()',
    'internal void InvalidateZombieModeRun()',
    'internal bool ShouldSettleZombieModeFailureInsurance(',
    'internal void CleanupZombieModeRunOnlyState(',
])
run_only_host_methods='\n'.join(member(cleanup,m) for m in [
    'private void RegisterZombieModeRunOnlyObject(',
    'private void PruneZombieModeRunOnlyEnemyRecords(',
    'private void RemoveZombieModeRunOnlyObjectRecord(',
    'private void PruneZombieModeUnknownRunOnlyRecords()',
    'private void InvalidateZombieModeRun()',
    'private bool ShouldSettleZombieModeFailureInsurance(',
    'private void CleanupZombieModeRunOnlyState(',
])
run_only_owner_bridges='\n'.join(member(run_only_bridges,m) for m in [
    'internal void SettleZombieModeFailureInsuranceForRuntimeModule(',
    'internal void RemoveZombieModeAttributeModifiersForRuntimeModule()',
    'internal void RemoveZombieModeOptionRuntimeEffectsForRuntimeModule()',
    'internal void CleanupZombieModeFortificationInteractionStateForRuntimeModule()',
    'internal void ClearZombieModeSupportSpawnQueueForRuntimeModule()',
    'internal void ClearZombieModeEnemyInstanceIdsForRuntimeModule()',
    'internal void ClearZombieModeRewardShellForRuntimeModule()',
    'internal void RestoreZombieModeMapIsolationShellForRuntimeModule()',
])
run_only_owner_bridges+='\n'+member(ROOT/'ZombieMode/ZombieModeEntryHostBridge.cs','private void RestoreZombieModeMapIsolationShell()')
run_only='''using System;
using System.Collections.Generic;
using UnityEngine;
namespace BossRush {
'''+member(models,'public enum ZombieModeRunOnlyObjectKind')+'\n'+member(models,'public sealed class ZombieModeRunOnlyRecord')+'''
internal sealed partial class ZombieModeRuntimeModule {
 private static int nextRunId; private ModBehaviour owner; private ZombieModeRunState runState;
 internal static int TestNextRunId { get { return nextRunId; } set { nextRunId = value; } }
 internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState runState) { this.owner = owner; this.runState = runState; }
'''+run_only_module_methods+'''
}
public partial class ModBehaviour {
'''+run_only_owner_bridges+'\n'+run_only_host_methods+'''
}
}'''
execute('runonly_cleanup',[HERE/'RunOnlyCleanup.cs',ROOT/'Utilities/RunScopedRegistry.cs'],run_only)
extraction_module=ROOT/'ZombieMode/ZombieModeRuntimeModule_Extraction.cs'
extraction_methods='\n'.join(member(extraction_module,m) for m in [
    'private void CompleteZombieModeExtractionSuccess(int runId)',
    'private bool SettleZombieModeExtractionCashShell()',
    'private bool TryDispatchZombieModeExtractionSuccess(CountDownArea area)',
])
extraction_fixture='''using System;
using System.Collections.Generic;
namespace BossRush {
internal enum ZombieModeCombatPhase { ExtractionOpportunity, SuccessExit }
internal enum ZombieModeFailureReason { SuccessfulExtraction }
internal sealed class ZombieModeRunState {
 internal int RunId = 7; internal bool ExtractionSuccessHandled; internal bool ExtractionChanneling = true;
 internal bool BeaconChanneling = true; internal ZombieModeCombatPhase CombatPhase = ZombieModeCombatPhase.ExtractionOpportunity;
 internal CountDownArea ActiveExtractionArea; internal long PurificationPoints = 41;
}
internal sealed class CountDownArea { internal Action<CountDownArea> onCountDownStopped; internal Action onCountDownSucceed; }
internal static class ExtractionTrace { internal static readonly List<string> Events = new List<string>(); }
internal static class L10n { internal static string T(string value) { return value; } internal static string T(string chinese, string english) { return chinese; } }
internal static class NotificationText { internal static void Push(string value) { ExtractionTrace.Events.Add("notification:" + value); } }
internal static class EconomyManager {
 internal static bool Result; internal static bool Add(long amount) { ExtractionTrace.Events.Add("payout:" + amount); return Result; }
}
public partial class ModBehaviour {
 internal void NotifyCampaignZombieExtracted() { ExtractionTrace.Events.Add("campaign"); }
 internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason) { ExtractionTrace.Events.Add("cleanup:" + reason); }
 internal void ShowBigBanner(string text) { ExtractionTrace.Events.Add("banner:" + text); }
 internal static void DevLog(string text) { }
}
internal sealed partial class ZombieModeRuntimeModule {
 private readonly ModBehaviour owner; private readonly ZombieModeRunState runState;
 internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState runState) { this.owner = owner; this.runState = runState; }
 internal bool IsZombieModeRunValid(int runId) { return runId > 0 && runState.RunId == runId; }
 private void TryReleaseZombieModeExtractionCountdownUi() { ExtractionTrace.Events.Add("release-countdown"); }
 internal void ShowZombieModeExtractionOpportunityUi(int runId) { ExtractionTrace.Events.Add("show-opportunity"); }
 private void TryNotifyZombieModeExtraction() { ExtractionTrace.Events.Add("notify-evacuated"); }
 private void TryLoadBaseSceneAfterZombieModeExtraction() { ExtractionTrace.Events.Add("load-base"); }
 internal void CompleteForTest(int runId) { CompleteZombieModeExtractionSuccess(runId); }
'''+extraction_methods+'''
}
internal static class Program {
 private static int checks;
 private static void Check(bool condition, string label) { if (!condition) throw new Exception("AuditModeLifecycle extraction: " + label); checks++; }
 private static int Index(string value) { return ExtractionTrace.Events.IndexOf(value); }
 private static ZombieModeRuntimeModule NewModule(ModBehaviour owner, ZombieModeRunState state, Action<CountDownArea> stopped, Action success) {
  state.ActiveExtractionArea = new CountDownArea { onCountDownStopped = stopped, onCountDownSucceed = success };
  return new ZombieModeRuntimeModule(owner, state);
 }
 public static void Main() {
  var owner = new ModBehaviour();
  var successState = new ZombieModeRunState();
  var successModule = NewModule(owner, successState, area => ExtractionTrace.Events.Add("stopped"), () => ExtractionTrace.Events.Add("success"));
  EconomyManager.Result = true; ExtractionTrace.Events.Clear(); successModule.CompleteForTest(7);
  Check(successState.PurificationPoints == 0 && successState.ExtractionSuccessHandled, "successful payout clears points and claims success once");
  Check(Index("payout:41") < Index("campaign") && Index("campaign") < Index("stopped") && Index("stopped") < Index("success") && Index("success") < Index("cleanup:SuccessfulExtraction"), "payout, campaign, official callbacks, and cleanup keep their order");
  int successEventCount = ExtractionTrace.Events.Count; successModule.CompleteForTest(7);
  Check(ExtractionTrace.Events.Count == successEventCount, "duplicate success callback does not pay or clean twice");

  var failedState = new ZombieModeRunState();
  var failedModule = NewModule(owner, failedState, area => ExtractionTrace.Events.Add("stopped"), () => ExtractionTrace.Events.Add("success"));
  EconomyManager.Result = false; ExtractionTrace.Events.Clear(); failedModule.CompleteForTest(7);
  Check(failedState.PurificationPoints == 41 && !failedState.ExtractionSuccessHandled, "failed payout retains purification points and leaves success unclaimed");
  Check(!failedState.ExtractionChanneling && !failedState.BeaconChanneling && failedState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity, "failed payout returns to the extraction opportunity state");
  Check(Index("release-countdown") >= 0 && Index("show-opportunity") > Index("release-countdown") && Index("campaign") < 0 && Index("cleanup:SuccessfulExtraction") < 0, "failed payout releases countdown and restores the choice without transitioning scenes");

  var fallbackState = new ZombieModeRunState { PurificationPoints = 0 };
  var fallbackModule = NewModule(owner, fallbackState, area => ExtractionTrace.Events.Add("stopped"), null);
  ExtractionTrace.Events.Clear(); fallbackModule.CompleteForTest(7);
  Check(Index("campaign") < Index("stopped") && Index("stopped") < Index("release-countdown") && Index("release-countdown") < Index("notify-evacuated") && Index("notify-evacuated") < Index("load-base") && Index("load-base") < Index("cleanup:SuccessfulExtraction"), "missing success listener uses the ordered manual evacuation and base-scene fallback before cleanup");
  Console.WriteLine("AuditModeLifecycle extraction settlement: " + checks + " PASS / 0 FAIL");
 }
}
}'''
execute('extraction_settlement',[],extraction_fixture)
hud_module=ROOT/'ZombieMode/ZombieModeRuntimeModule_Hud.cs'
hud_constants='\n'.join(declaration(hud_module,m) for m in [
    'private const float ZombieModeHudRefreshInterval =',
    'private const float ZombieModeHudGainHoldSeconds =',
    'private const float ZombieModeHudGainFadeSeconds =',
])
hud_state=type_block(hud_module,'internal sealed class ZombieModeHudRuntimeState')
hud_methods='\n'.join(member(hud_module,m) for m in [
    'internal void CreateZombieModeHud(int runId)',
    'internal bool SetZombieModeHudVisibility(ZombieModeHudController controller, bool hidden)',
    'internal void CleanupZombieModeHud(ZombieModeHudController controller)',
    'private ZombieModeHudRuntimeState GetZombieModeHudState(ZombieModeHudController controller)',
    'private static bool SetZombieModeHudText(ref string lastValue, string value)',
    'private bool TickZombieModeHudPurification(',
])
hud_field=declaration(hud_module,'private readonly Dictionary<int, ZombieModeHudRuntimeState> zombieModeHudStates =')
hud_fixture='''using System;
using System.Collections.Generic;
using UnityEngine;
namespace BossRush {
'''+hud_state+'''
internal sealed partial class ZombieModeRuntimeModule {
 private ModBehaviour owner; private readonly int activeRunId;
'''+hud_field+'\n'+hud_constants+'''
 internal Action LastRunOnlyCleanup;
 internal ZombieModeHudController LastCreatedController;
 internal ZombieModeRuntimeModule(ModBehaviour owner, int activeRunId) { this.owner = owner; this.activeRunId = activeRunId; }
 internal bool IsZombieModeRunValid(int runId) { return runId > 0 && runId == activeRunId; }
 internal void RegisterZombieModeRunOnlyObject(int runId, ZombieModeRunOnlyObjectKind kind, GameObject root, UnityEngine.Object target, Action cleanupAction) {
  LastCreatedController = target as ZombieModeHudController; LastRunOnlyCleanup = cleanupAction;
  HudRuntimeTrace.Events.Add("register:" + kind.ToString());
 }
 internal int HudStateCount { get { return zombieModeHudStates.Count; } }
 internal ZombieModeHudRuntimeState GetHudStateForTest(ZombieModeHudController controller) { return GetZombieModeHudState(controller); }
 internal bool TickPurificationForTest(ZombieModeHudController controller, ZombieModeHudRuntimeState state, float deltaTime) { return TickZombieModeHudPurification(controller, state, controller.RunId, deltaTime); }
 internal bool CacheMainTextForTest(ref string lastValue, string value) { return SetZombieModeHudText(ref lastValue, value); }
'''+hud_methods+'''
}
}'''
execute('hud_runtime',[HERE/'HudRuntime.cs'],hud_fixture)
mode_d_destroy = '\n'.join(member(ROOT/'ModeD/ModeDRuntimeModule_Lifecycle.cs', sig) for sig in ['private void CleanupModeDRuntimeOnDestroy()', 'private void CleanupModeDWaveEnemiesOnExit()'])
execute('wave_owner',[HERE/'WaveOwner.cs',ROOT/'WavesArena/WavesArenaRuntimeModule.cs',ROOT/'ModeD/ModeDRuntimeModule.cs',ROOT/'ModeD/ModeDRuntimeModule_WaveResolution.cs'], 'using System; namespace BossRush { internal sealed partial class ModeDRuntimeModule {'+mode_d_destroy+'}}')
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
