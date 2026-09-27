"""编译真实 HostLifecycle、宿主入口和运行时状态迁交方法，隔离 Unity 外部依赖。"""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ZombieModeHostOwners"
HASHES = {}


def member(path, signature):
    source = path.read_text(encoding="utf-8-sig")
    assert source.count(signature) == 1, signature
    HASHES[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    module = ROOT / "ZombieMode/ZombieModeRuntimeModule.cs"
    host = ROOT / "ZombieMode/ZombieModeEntryHostBridge.cs"
    models = ROOT / "ZombieMode/ZombieModeModels.cs"
    visuals = ROOT / "ZombieMode/ZombieModeBossVisuals.cs"
    generated = "using System; using System.Collections; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement; namespace BossRush {\n"
    generated += "\n".join(member(models, signature) for signature in [
        "public enum ZombieModeLifecyclePhase", "public enum ZombieModeCombatPhase",
        "public enum ZombieModeFailureReason", "public enum ZombieModeRunOnlyObjectKind",
        "public sealed class ZombieModeRunOnlyRecord", "public sealed class ZombieModeEntryTransaction",
    ])
    generated += member(ROOT / "ZombieMode/ZombieModeTuning.cs", "public static class ZombieModePhaseGuards")
    generated += "\ninternal sealed partial class ZombieModeRuntimeModule : BossRushRuntimeModuleBase {\n"
    # 字段声明逐字抽取；在 OnAwake/AdoptHostState 前保持 null 默认值。
    source = module.read_text(encoding="utf-8-sig")
    for field in ["private ModBehaviour owner;", "private ZombieModeRunState runState;", "private ZombieModeEntryTransaction entryTransaction;", "private Dictionary<string, int[]> rewardCandidateCache;", "private List<int> rewardCandidateScratch;", "private HashSet<int> opaqueFilterLogIds;", "private bool pendingEntry;", "private static int nextRunId;"]:
        assert source.count(field) == 1
        generated += field + "\n"
    generated += "\n".join(member(module, signature) for signature in [
        "public override void OnAwake(", "public override void OnDestroy()", "internal void AdoptHostState(",
        "internal ZombieModeRunState RunState", "internal ZombieModeEntryTransaction EntryTransaction",
        "internal Dictionary<string, int[]> RewardCandidateCache", "internal List<int> RewardCandidateScratch",
        "internal HashSet<int> OpaqueFilterLogIds", "internal bool PendingEntry", "internal static int NextRunId",
        "internal bool IsZombieModeRunValid(", "internal bool IsZombieModeStartupInProgress()",
        "internal bool ShouldRollbackZombieModeEntryResources()", "internal void RegisterZombieModeRunOnlyObject(",
        "internal void InvalidateZombieModeRun()", "internal bool ShouldSettleZombieModeFailureInsurance(",
        "internal void CleanupZombieModeRunOnlyState(",
    ])
    generated += "\n}\npublic partial class ModBehaviour {\n"
    declaration = "private readonly ZombieModeHostLifecycle zombieModeHostLifecycle = new ZombieModeHostLifecycle();"
    assert host.read_text(encoding="utf-8-sig").count(declaration) == 1
    generated += declaration + "\n"
    generated += "\n".join(member(host, signature) for signature in [
        "private ZombieModeRuntimeModule zombieModeRuntimeModule", "internal void AttachZombieModeRuntimeModule(",
        "internal void DetachZombieModeRuntimeModule(", "private ZombieModeRunState zombieModeRunState",
        "private ZombieModeEntryTransaction zombieModeEntryTransaction", "private Dictionary<string, int[]> zombieModeRewardCandidateCache",
        "private List<int> zombieModeRewardSafeCandidateScratch", "private HashSet<int> zombieModeOpaqueFilterLogIds",
        "private bool pendingZombieModeEntry", "private int nextZombieModeRunId",
        "private void CleanupZombieModeForSceneChange(", "private void CleanupZombieModeOnDestroy()",
        "private Coroutine StartZombieModeCoroutine(", "private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(",
    ])
    generated += "\n}\ninternal sealed partial class ZombieModeBossVisuals : MonoBehaviour {\n"
    cache_field = "private static readonly Texture2D[] SigilTextures = new Texture2D[5];"
    assert visuals.read_text(encoding="utf-8-sig").count(cache_field) == 1
    generated += cache_field + "\n" + member(visuals, "internal static void ResetStaticCaches()")
    generated += "\n}}\n"
    (OUT / "Production.cs").write_text(generated, encoding="utf-8")
    production_files = [ROOT / "ZombieMode/ZombieModeRuntimeModule_HostLifecycle.cs", ROOT / "Utilities/RunScopedRegistry.cs"]
    for p in production_files:
        HASHES[str(p.relative_to(ROOT))] = hashlib.sha256(p.read_bytes()).hexdigest()
    (OUT / "production-source-sha256.json").write_text(json.dumps(HASHES, indent=2), encoding="utf-8")
    files = production_files + [OUT / "Production.cs", HERE / "Stubs.cs", HERE / "Program.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '"/>' for p in files)
    project += '</ItemGroup></Project>'
    path = OUT / "ZombieModeHostOwners.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
