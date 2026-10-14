"""Run the production coroutine stack fixture with its Build-only output paths.

2026-09-14: besides the main suite shell (RunIsolatedCase / TryStep), the Sky Island shell is extracted too —
RunSkyIslandSync / RunSkyIslandCase / SkyIslandSessionStillValid, the shared RunSyncCase and the SkyIslandSkipCase
signal. Before this none of the island SKIP branches had ever been executed offline.
"""
from pathlib import Path
import os
import subprocess
import hashlib
import sys

HERE = Path(__file__).resolve().parent

def extract(source, signature):
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

if __name__ == "__main__":
    root = HERE.parents[2]
    stages = (root / 'DebugAndTools/F3GameplayValidationStages.cs').read_text(encoding='utf-8-sig')
    execution = (root / 'DebugAndTools/F3GameplayValidationExecution.cs').read_text(encoding='utf-8-sig')
    runner = (root / 'DebugAndTools/F3GameplayValidationRunner.cs').read_text(encoding='utf-8-sig')
    island = (root / 'DebugAndTools/F3GameplayValidationSkyIsland.cs').read_text(encoding='utf-8-sig')
    performance = (root / 'DebugAndTools/F3GameplayValidationResourcePerformance.cs').read_text(encoding='utf-8-sig')
    runtime = (root / 'DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs').read_text(encoding='utf-8-sig')
    scenes = (root / 'DebugAndTools/F3GameplayValidationScenes.cs').read_text(encoding='utf-8-sig')
    modes = (root / 'DebugAndTools/F3GameplayValidationModes.cs').read_text(encoding='utf-8-sig')
    seasons = (root / 'DebugAndTools/F3GameplayValidationSeasons.cs').read_text(encoding='utf-8-sig')
    deep = (root / 'DebugAndTools/F3GameplayValidationDeepFlows.cs').read_text(encoding='utf-8-sig')
    zombie = (root / 'DebugAndTools/F3GameplayValidationZombie.cs').read_text(encoding='utf-8-sig')
    modef = (root / 'ModeF/ModeFExtraction.cs').read_text(encoding='utf-8-sig')
    methods = extract(stages, 'private IEnumerator RunIsolatedCase(') + '\n' + extract(execution, 'private static bool TryStep(')
    island_methods = '\n'.join((
        extract(island, 'private bool SkyIslandSessionStillValid(out string reason)'),
        extract(island, 'private void RunSkyIslandSync(string id, SyncValidation validation)'),
        extract(island, 'private IEnumerator RunSkyIslandCase(string caseId, Func<IEnumerator> factory)'),
        extract(runner, 'private void RunSyncCase(string id, SyncValidation validation)'),
        extract(execution, 'private static bool TryStep('),
    ))
    skip_signal = extract(island, 'internal sealed class SkyIslandSkipCase : Exception')
    generated = '''using System; using System.Collections; using System.Collections.Generic; using System.Diagnostics; using Duckov.Economy;
namespace BossRush { internal class ProductionCase {
internal bool Cancelled, Reclaimed;
internal Action OnReclaim;
internal readonly List<string> Results = new List<string>();
private bool _operationSucceeded;
private bool ShouldAbort() { return Cancelled; }
private string DescribeAbortReason() { return "cancelled"; }
private IEnumerator EnsureArenaForCase(string id) { _operationSucceeded = true; yield break; }
private IEnumerator ForceReclaimArena() { Reclaimed = true; if (OnReclaim != null) OnReclaim(); yield return null; }
private void Record(string id, string outcome, long ms, string metrics, string reason) { Results.Add(id+":"+outcome); }
internal IEnumerator Run(Func<IEnumerator> factory) { return RunIsolatedCase("CASE", factory); }
''' + methods + '''
}
internal struct SceneStub { internal int handle; }
internal sealed class SkyIslandSession { internal bool IsReady = true; internal SceneStub ValidationScene; }
internal class ProductionSkyIslandCase {
internal SkyIslandSession Session;
internal bool Cancelled;
internal readonly List<string> Results = new List<string>();
internal readonly List<string> Metrics = new List<string>();
internal readonly List<string> Reasons = new List<string>();
private int _skyIslandSceneHandle;
private const float SkyIslandCaseTimeoutSeconds = 60f;
internal delegate bool SyncValidation(out string metrics, out string reason);
private SkyIslandSession SkyIslandSessionOrNull() { return Session; }
private bool ShouldAbort() { return Cancelled; }
private string DescribeAbortReason() { return "cancelled"; }
private void Record(string id, string outcome, long ms, string metrics, string reason) { Results.Add(id+":"+outcome); Metrics.Add(metrics); Reasons.Add(reason); }
internal void Begin(int handle) { _skyIslandSceneHandle = handle; }
internal void Sync(string id, SyncValidation validation) { RunSkyIslandSync(id, validation); }
internal IEnumerator Coroutine(string id, Func<IEnumerator> factory) { return RunSkyIslandCase(id, factory); }
''' + island_methods + '''
}
''' + skip_signal + '''
}'''
    performance_methods = '\n'.join((
        extract(runner, 'private IEnumerator SamplePerformance('),
        extract(performance, 'private IEnumerator SamplePerformanceWindow('),
        extract(runtime, 'private void BeginSkyIslandFrameProfile()'),
        extract(runtime, 'private string AppendSkyIslandFrameProfile('),
    ))
    generated += '''
namespace BossRush { internal sealed class ProductionPerformance {
internal bool Cancelled, _skyIslandMode = true;
internal float _peakFrameMs, _baselineP95Ms = 13f, _finalP95Ms = 17f;
internal long _baselineMemory = 11, _finalMemory = 19;
private string _peakStage, _status = "probe";
internal readonly List<string> Results = new List<string>();
internal readonly List<string> Reasons = new List<string>();
internal readonly List<string> Metrics = new List<string>();
private bool ShouldAbort() { return Cancelled; }
private void Record(string id, string outcome, long ms, string metrics, string reason) { Results.Add(outcome); Reasons.Add(reason); Metrics.Add(metrics); }
private string SkyIslandFrameProfileMetrics(out string reason) { reason = null; SkyIslandFrameProfile.Recording = false; SkyIslandFrameProfile.Closed++; return ",profile=collected"; }
internal IEnumerator Run(float seconds = 5f, bool baseline = true) { return SamplePerformance("SKY_PERF_PROBE", seconds, baseline); }
''' + performance_methods + '\n} }\n'
    scene_methods = '\n'.join(extract(scenes, signature) for signature in (
        'private IEnumerator LoadScene(', 'private static bool IsRuntimeReady(', 'private static string DescribeSceneReadiness('))
    generated += '''
namespace BossRush { internal sealed class ProductionSceneCase {
private readonly SceneHost _host = new SceneHost();
private const float SceneTimeoutSeconds = 3f;
internal bool _operationSucceeded;
internal string _operationReason;
internal int _lastSceneClicksFed, _sceneClicksFed;
internal readonly List<string> Results = new List<string>(), Reasons = new List<string>(), Metrics = new List<string>();
private static string BaseSceneNameForValidation() { return "Base"; }
private bool ShouldAbort() { return false; }
private string DescribeAbortReason() { return "cancelled"; }
private void WriteRaw(string value) { }
internal static int ClosureClicks;
private static bool TryClickAutotestClosureContinue() { ClosureClicks++; return true; }
private void Record(string id, string status, long elapsed, string metrics, string reason) { Results.Add(status); Reasons.Add(reason); Metrics.Add(metrics); }
internal IEnumerator Run(bool toBase = false) { return LoadScene("arena-id", "SCENE", true, toBase); }
''' + scene_methods + '\n}}\n'
    entry_preparation = extract(modes, 'internal sealed class F3ModeDLoadoutLease')
    generated += 'namespace BossRush { ' + entry_preparation + ' }\n'
    protection = '\n'.join(extract(seasons, signature) for signature in (
        'private void TryDamageModeGChildProtector(', 'private static bool IsLiveCharacter(', 'private static void ApplyValidationDamage('))
    field = seasons[seasons.index('private static readonly System.Reflection.FieldInfo _modeGChildProtectorField'):]
    field = field[:field.index(';') + 1]
    generated += '''namespace BossRush { internal sealed class ProductionGProtectionCase {
internal readonly List<string> Logs = new List<string>();
private void WriteRaw(string value) { Logs.Add(value); }
internal void Run(CharacterMainControl boss) { TryDamageModeGChildProtector(boss); }
''' + field + protection + '} }\n'
    f_trigger = extract(modef, 'internal bool DebugTriggerModeFExtractionForValidation(')
    f_extract = extract(deep, 'private IEnumerator RunModeFExtraction(')
    z_extract = extract(zombie[zombie.index('internal sealed partial class ZombieModeRuntimeModule'):], 'internal IEnumerator ValidationCompleteZombieExtraction(')
    generated += """
namespace BossRush { internal sealed class ProductionFTrigger {
internal bool modeFActive = true, Admitted = true;
internal readonly FExtractionState modeFState = new FExtractionState();
internal readonly object coopExtractionPosition = new object();
private bool IsCoopModeFLocalAdmitted() { return Admitted; }
internal bool Run(out string reason) { return DebugTriggerModeFExtractionForValidation(out reason); }
""" + f_trigger + """ }
internal sealed class ProductionFExtractionCase {
internal readonly FExtractionHost _host = new FExtractionHost();
private const float SceneTimeoutSeconds = 30f;
internal int Restores, ClosureClicks;
internal bool BaseReady;
internal readonly List<string> Results = new List<string>(), Reasons = new List<string>(), Metrics = new List<string>();
private bool PrepareModeEFValidationEntry(bool f, out string reason) { reason = null; return true; }
private void RestoreModeDValidationEntry() { Restores++; }
private bool ShouldAbort() { return false; }
private IEnumerator WaitSeconds(float seconds) { yield return null; }
private bool IsRuntimeReady(string scene) { return BaseReady; }
private static string BaseSceneNameForValidation() { return "Base"; }
private bool TryClickAutotestClosureContinue() { ClosureClicks++; return true; }
private void Record(string id, string result, long ms, string metrics, string reason) { Results.Add(result); Reasons.Add(reason); Metrics.Add(metrics); }
internal IEnumerator Run() { return RunModeFExtraction(); }
""" + f_extract + """ }
internal sealed class ProductionZombieExtractionCase {
internal readonly ZombieExtractionState runState = new ZombieExtractionState();
internal bool Active = true, AllInside, coopZombieStarterChosen = true;
internal int Pickups, Starts;
internal bool Passed;
internal string Metrics, Reason;
private bool IsZombieModeActive { get { return Active; } }
private bool IsCoopZombieLocalAssetCurrent() { return true; }
private bool IsZombieModeRunValid(int run) { return Active && runState.RunId == run; }
private void CollectZombieModePurificationPoint(int run, int amount, object point, object star) { Pickups++; }
private void BeginZombieModeExtractionOpportunity(int run) { }
private void EnsureZombieModeExtractionArea(int run) { runState.ActiveExtractionArea = new CountDownArea(); }
private bool CoopZombieAllInsideExtraction(CountDownArea area) { return AllInside; }
private string StartZombieModeExtractionFromUi(int run) { Starts++; runState.ExtractionChanneling = true; return null; }
internal IEnumerator Run() { return ValidationCompleteZombieExtraction(delegate(bool ok, string m, string r) { Passed = ok; Metrics = m; Reason = r; }); }
""" + z_extract + '} }\n'
    # Preserve the real namespace import required by native DestroyTree extension binding.
    generated = '\n'.join(line for line in modes.splitlines() if line in ('using ItemStatsSystem;', 'using UnityEngine;')) + '\n' + generated
    out = root / 'Build/f3-validation-execution'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'ProductionCase.cs').write_text(generated, encoding='utf-8')
    (out / 'source.sha256').write_text(hashlib.sha256((methods + island_methods + skip_signal + performance_methods + scene_methods + entry_preparation + field + protection + f_trigger + f_extract + z_extract).encode('utf-8')).hexdigest(), encoding='utf-8')
    sys.path.insert(0, str(root / 'tools'))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(HERE / "F3ValidationExecution.csproj", out / 'runs', root)
    print(output)
    raise SystemExit(code)
