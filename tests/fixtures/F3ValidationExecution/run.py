"""Run the production coroutine stack fixture with its Build-only output paths."""
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
    methods = extract(stages, 'private IEnumerator RunIsolatedCase(') + '\n' + extract(execution, 'private static bool TryStep(')
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
''' + methods + '\n} }'
    out = root / 'Build/f3-validation-execution'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'ProductionCase.cs').write_text(generated, encoding='utf-8')
    (out / 'source.sha256').write_text(hashlib.sha256(methods.encode('utf-8')).hexdigest(), encoding='utf-8')
    raise SystemExit(subprocess.call(
        ["dotnet", "run", "--project", str(HERE / "F3ValidationExecution.csproj"),
         "--configuration", "Release", "--verbosity", "quiet"],
        cwd=HERE.parents[2], env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en-US")))
