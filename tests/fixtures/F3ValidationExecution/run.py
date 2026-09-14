"""Run the production coroutine stack fixture with its Build-only output paths.

2026-09-14: besides the main suite shell (RunIsolatedCase / TryStep), the Sky Island shell is extracted too —
RunSkyIslandSync / RunSkyIslandCase / SkyIslandSessionStillValid, the shared RunSyncCase and the SkyIslandSkipCase
signal. Before this none of the island SKIP branches had ever been executed offline.
"""
from pathlib import Path
import os
import subprocess
import hashlib

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
    methods = extract(stages, 'private IEnumerator RunIsolatedCase(') + '\n' + extract(execution, 'private static bool TryStep(')
    island_methods = '\n'.join((
        extract(island, 'private bool SkyIslandSessionStillValid(out string reason)'),
        extract(island, 'private void RunSkyIslandSync(string id, SyncValidation validation)'),
        extract(island, 'private IEnumerator RunSkyIslandCase(string caseId, Func<IEnumerator> factory)'),
        extract(runner, 'private void RunSyncCase(string id, SyncValidation validation)'),
        extract(execution, 'private static bool TryStep('),
    ))
    skip_signal = extract(island, 'internal sealed class SkyIslandSkipCase : Exception')
    generated = '''using System; using System.Collections; using System.Collections.Generic; using System.Diagnostics;
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
    out = root / 'Build/f3-validation-execution'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'ProductionCase.cs').write_text(generated, encoding='utf-8')
    (out / 'source.sha256').write_text(hashlib.sha256((methods + island_methods + skip_signal).encode('utf-8')).hexdigest(), encoding='utf-8')
    raise SystemExit(subprocess.call(
        ["dotnet", "run", "--project", str(HERE / "F3ValidationExecution.csproj"),
         "--configuration", "Release", "--verbosity", "quiet"],
        cwd=HERE.parents[2], env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en-US")))
