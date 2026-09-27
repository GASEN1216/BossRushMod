"""约束丧尸宿主桥、构造期备用状态和生命周期 owner 的归属。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
BRIDGES = {"ZombieModeEntryHostBridge.cs", "ZombieModeCombatHostBridge.cs", "ZombieModeRewardHostBridge.cs"}


def body(source, signature):
    assert source.count(signature) == 1, "唯一成员缺失: " + signature
    start = source.index("{", source.index(signature))
    depth = 0
    for i in range(start, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return re.sub(r"\s+", " ", source[start + 1:i]).strip()
    raise AssertionError(signature)


def ordered(source, tokens):
    positions = [source.find(token) for token in tokens]
    assert min(positions) >= 0 and positions == sorted(positions), "生命周期顺序变化: " + repr(tokens)


def main():
    sources = {p.name: clean_source(p.read_text(encoding="utf-8-sig")) for p in (ROOT / "ZombieMode").glob("*.cs")}
    hosts = {name for name, source in sources.items() if re.search(r"\bpartial\s+class\s+ModBehaviour\b", source)}
    assert hosts == BRIDGES, "ZombieMode 宿主 partial 清单变化: " + repr(hosts)
    for name in BRIDGES:
        assert len((ROOT / "ZombieMode" / name).read_text(encoding="utf-8-sig").splitlines()) <= 1200, name
        assert sources[name].count("partial class ModBehaviour") == 1, name
    for name, types in {
        "ZombieModeBossController.cs": ["ZombieModeTimedRunScopedRuntime", "ZombieModeAreaTickRuntime", "ZombieModeBossShieldRuntime"],
        "ZombieModeEnemyRuntime.cs": ["ZombieModeVisualScaleRecord", "ZombieModeFootMarkerPool", "ZombieModeEnemyRuntimeMarker"],
        "ZombieModeExtractionController.cs": ["ZombieModeExtractionController", "ZombieModeExtractionOpportunityView"],
        "ZombieModeHudController.cs": ["ZombieModeHudController"],
    }.items():
        assert "partial class ModBehaviour" not in sources[name], name + " 混装宿主"
        for type_name in types:
            assert re.search(r"\bclass\s+" + type_name + r"\b", sources[name]), type_name
    host = sources["ZombieModeEntryHostBridge.cs"]
    lifecycle = sources["ZombieModeRuntimeModule_HostLifecycle.cs"]
    all_hosts = "\n".join(sources[name] for name in BRIDGES)
    assert "private readonly ZombieModeHostLifecycle zombieModeHostLifecycle = new ZombieModeHostLifecycle();" in host
    assert "zombieModeUnattached" not in all_hosts, "备用状态不能留在宿主"
    assert "new ZombieModeRuntimeModule(" not in lifecycle, "不得创建第二个运行时模块"
    fields = [
        ("ZombieModeRunState", "zombieModeUnattachedRunState", "RunState"),
        ("ZombieModeEntryTransaction", "zombieModeUnattachedEntryTransaction", "EntryTransaction"),
        ("Dictionary<string, int[]>", "zombieModeUnattachedRewardCandidateCache", "RewardCandidateCache"),
        ("List<int>", "zombieModeUnattachedRewardCandidateScratch", "RewardCandidateScratch"),
        ("HashSet<int>", "zombieModeUnattachedOpaqueFilterLogIds", "OpaqueFilterLogIds"),
    ]
    for type_name, field, prop in fields:
        statement = "private " + type_name + " " + field + " = new " + type_name + "();"
        assert lifecycle.count(statement) == 1, "构造期唯一备用对象: " + field
        assert lifecycle.count("new " + type_name + "()") == 1, "getter 不得替换备用对象: " + field
        assert "zombieModeRuntimeModule." + prop + " : " + field in lifecycle, "状态选择: " + field
    assert "private bool zombieModeUnattachedPendingEntry;" in lifecycle
    attach = body(lifecycle, "internal void AttachZombieModeRuntimeModule(")
    ordered(attach, ["if (module == null) return;", "module.AdoptHostState(", "zombieModeUnattachedRunState,", "zombieModeUnattachedPendingEntry);", "zombieModeRuntimeModule = module;"] + [field + " = null;" for _, field, _ in fields])
    detach = body(lifecycle, "internal void DetachZombieModeRuntimeModule(")
    ordered(detach, ["if (!ReferenceEquals(zombieModeRuntimeModule, module)) return;"] + [field + " = module." + prop + ";" for _, field, prop in fields] + ["zombieModeUnattachedPendingEntry = module.PendingEntry;", "zombieModeRuntimeModule = null;"])
    for method, args in [("AttachZombieModeRuntimeModule", "module"), ("DetachZombieModeRuntimeModule", "module"), ("CleanupZombieModeForSceneChange", "reason"), ("CleanupZombieModeOnDestroy", "")]:
        signature = ("internal" if method.startswith(("Attach", "Detach")) else "private") + " void " + method + "("
        assert body(host, signature) == "zombieModeHostLifecycle." + method + "(" + args + ");", method + " 必须薄转发"
    for method in ["CleanupZombieModeForSceneChange", "CleanupZombieModeOnDestroy"]:
        cleanup = body(lifecycle, "internal void " + method + "(")
        ordered(cleanup, ["ShouldRollbackZombieModeEntryResources()", "RollbackZombieModeInventoryTransferShell();", "RefundZombieModeInvitationIfNeeded();", "RefundZombieModeCashIfNeeded();", "CleanupZombieModeRunOnlyState(", "zombieModeRunState.ClearRuntime();", "LifecyclePhase = ZombieModeLifecyclePhase.None;", "pendingZombieModeEntry = false;", "zombieModeEntryTransaction.Reset();"])
    scene = body(lifecycle, "internal void CleanupZombieModeForSceneChange(")
    ordered(scene, ["!IsZombieModeActive && !IsZombieModeStartupInProgress() && zombieModeRunState.RunOnlyObjects.Count <= 0", "return;", "LifecyclePhase = ZombieModeLifecyclePhase.Exiting;"])
    coroutine = body(lifecycle, "internal Coroutine StartZombieModeCoroutine(")
    ordered(coroutine, ["if (!IsZombieModeRunValid(runId) || routine == null)", "return null;", "ModBehaviour coroutineOwner = owner;", "Coroutine coroutine = coroutineOwner.StartCoroutine(routine);", "if (coroutine != null)", "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Coroutine, null, null, delegate", "coroutineOwner.StopCoroutine(coroutine);", "return coroutine;"])
    assert "owner.StopCoroutine" not in coroutine, "回收闭包必须保留原宿主"
    assert body(host, "private Coroutine StartZombieModeCoroutine(") == "ZombieModeRuntimeModule module = zombieModeRuntimeModule; return module != null ? module.StartZombieModeCoroutine(routine, runId) : null;"
    print("ZombieModeHostOwnershipGuard: PASS")


if __name__ == "__main__":
    main()
