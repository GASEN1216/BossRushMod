"""CR-2026-09-05-013/014/016：完整计划判胜、增援owner与整批容量；静态守卫，不冒充实机。"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from modeh_guard_util import strip_cs_comments


def check(sources):
    errors = []
    control = strip_cs_comments(sources["ModeHCombatControl.cs"])
    flow = strip_cs_comments(sources["ModeHRuntimeModule_CombatFlow.cs"])
    profiles = strip_cs_comments(sources["ModeHRuntimeModule_CombatProfiles.cs"])
    tx = strip_cs_comments(sources["ModeHSpawnTransaction.cs"])
    for text, needle, label in [
        (control, "_entryBatchIndex >= _lastEntryBatchIndex && !_enemySpawningPending", "胜利必须等最后批次与在途生成"),
        (flow, "_combatControl.SetEnemySpawningPending(", "生产Tick必须同步生成状态"),
        (flow, "_pendingEnemyBatchKeys.Count > 0 || _reinforcementSpawnInFlight", "pending与in-flight都必须阻挡判胜"),
        (flow, "ReleaseReinforcementRuntimeObjects();", "战斗公共收尾必须释放增援"),
        (profiles, "Math.Min(corridor.SimultaneousCap, ModeHConfig.MaxConcurrentEnemyInstances)", "容量同时受场次与全局上限约束"),
        (profiles, "nextBatchCount > cap - _combatTelemetry.LiveEnemyCount", "按整批大小检查余量"),
        (profiles, "- _reinforcementReservedEnemyCount", "容量必须包含在途预留"),
        (profiles, "_reinforcementTransactions.Add(tx);", "事务创建立即登记owner"),
        (profiles, "ReferenceEquals(control, _combatControl)", "旧回调必须校验本场控制器身份"),
        (profiles, "_runState.MatchIndex == matchIndex", "旧回调必须校验场次"),
        (profiles, "if (version == _reinforcementOwnerVersion)", "旧finally不能清新场owner"),
        (profiles, "_reinforcementTransactions[i].Cancel();", "已提交与挂起事务统一取消"),
        (profiles, "IDisposable disposable = batch as IDisposable", "手工驱动的子协程必须释放"),
        (tx, "int batchGeneration = _spawnGeneration;", "迭代器必须冻结事务代次"),
        (tx, "batchGeneration != _spawnGeneration", "旧迭代器不能影响重用后的事务"),
        (tx, "private async Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> CreateOwnedIsolatedAsync(", "停止协程后的晚结果必须由独立UniTask续作接管"),
        (tx, "if (!IsActive || generation != _spawnGeneration)", "异步完成必须检查取消与代次"),
    ]:
        if needle not in text:
            errors.append(label)
    before_begin = profiles.find("_reinforcementTransactions.Add(tx);")
    begin = profiles.find("tx.Begin(_map")
    if not (0 <= before_begin < begin): errors.append("Begin之前必须登记事务owner")
    release = profiles[profiles.index("private void ReleaseReinforcementRuntimeObjects()"):]
    if release.index("_reinforcementOwnerVersion++;") > release.index("_owner.StopCoroutine(routine)"):
        errors.append("停止协程前必须先作废本场身份")
    if any(x in tx for x in ["Task.Run(", "ContinueWith(", "ConfigureAwait(false)"]):
        errors.append("不得把Unity生成回收转到线程池")
    return errors


def main():
    names = ["ModeHCombatControl.cs", "ModeHRuntimeModule_CombatFlow.cs",
             "ModeHRuntimeModule_CombatProfiles.cs", "ModeHSpawnTransaction.cs"]
    sources = {name: (ROOT / "ModeH" / name).read_text(encoding="utf-8-sig") for name in names}
    errors = check(sources)
    for name in ["run.py", "Harness.cs", "README.md"]:
        if not (ROOT / "tests/fixtures/ModeHReinforcementSecondReview" / name).is_file():
            errors.append("缺持续执行回归 " + name)
    # 只变异内存源码，验证守卫确实拒绝关键不变量回退。
    mutations = [
        (names[0], "_entryBatchIndex >= _lastEntryBatchIndex && !_enemySpawningPending", "true"),
        (names[1], "_combatControl.SetEnemySpawningPending(", "_combatControl.Ignored("),
        (names[1], "ReleaseReinforcementRuntimeObjects();", ""),
        (names[2], "nextBatchCount > cap - _combatTelemetry.LiveEnemyCount", "_combatTelemetry.LiveEnemyCount >= cap"),
        (names[2], "- _reinforcementReservedEnemyCount", ""),
        (names[2], "_reinforcementTransactions.Add(tx);", ""),
        (names[2], "ReferenceEquals(control, _combatControl)", "true"),
        (names[2], "_runState.MatchIndex == matchIndex", "true"),
        (names[2], "_reinforcementTransactions[i].Cancel();", ""),
        (names[3], "if (!IsActive || generation != _spawnGeneration)", "if (false)"),
        (names[3], "batchGeneration != _spawnGeneration", "false"),
    ]
    for filename, old, new in mutations:
        altered = dict(sources)
        if old not in altered[filename]:
            errors.append("反向变异锚点缺失 " + old)
            continue
        altered[filename] = altered[filename].replace(old, new)
        if not check(altered): errors.append("反向变异未被拒绝 " + old)
    if errors:
        print("ModeHReinforcementSecondReviewGuard: FAIL\n  - " + "\n  - ".join(errors))
        return 1
    print("ModeHReinforcementSecondReviewGuard: PASS (11 negative mutations)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
