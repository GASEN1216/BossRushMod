#!/usr/bin/env python3
"""DeathBroadcastIsolationGuard — 官方死亡 / 受伤广播逐订阅者隔离 + 丧尸 Boss 死亡兜底。

不变式（2026-11-06，玩家反馈「丧尸第 10 波 Boss 不见了、卡关」）：
- Health.Hurt 上有独立 transpiler，把 OnDeadEvent、Health.OnDead、Health.OnHurt 三处广播的 Invoke
  原地换成隔离调用；CharacterMainControl.OnDead 上把 BeforeCharacterSpawnLootOnDead 换成隔离调用。
  只改调用点：不插入、不删除指令，不声明 Prefix / Postfix / Finalizer（不改准入、伤害与返回值）。
- 每个隔离调用都在循环里逐订阅者 try/catch，抛异常的订阅者不会截断后面的订阅者。
- 静态多播的调用列表按委托引用缓存（稳态零分配），并在 Mod 卸载清理路径里释放。
- 丧尸模式 Boss 波每 0.25 秒兜底扫本波 Boss：已死走同一条死亡路由，已销毁按本波扣除。
文本守卫只证明接线；隔离行为与真实官方 IL 命中由执行回归 DeathBroadcastIsolation 证明。
"""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
PATCH = "Patches/Combat/DeathBroadcastIsolationPatch.cs"
SWEEP = "ZombieMode/ZombieModeRuntimeModule_BossSilentDeathSweep.cs"
BOSS = "ZombieMode/ZombieModeRuntimeModule_BossController.cs"
TUNING = "ZombieMode/ZombieModeTuning.cs"
HOOKS = "Utilities/AlwaysOnRuntimeHooks.cs"
REGRESSIONS = "tools/run_runtime_regressions.py"


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def norm(text):
    return re.sub(r"\s+", " ", text)


def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 0
    for i in range(opening, len(text)):
        depth += (text[i] == "{") - (text[i] == "}")
        if depth == 0:
            return text[opening:i + 1]
    raise AssertionError(signature)


def check(errors, condition, message):
    if not condition:
        errors.append(message)


def class_block(text, name):
    return body(text, "static class " + name)


def main():
    errors = []
    patch = source(PATCH)

    # --- 补丁身份：两个独立 transpiler 类，只有 Transpiler ---
    hurt_attr = "[HarmonyPatch(typeof(Health), nameof(Health.Hurt))] internal static class HealthDeathBroadcastIsolationPatch"
    loot_attr = '[HarmonyPatch(typeof(CharacterMainControl), "OnDead")] internal static class CharacterLootBroadcastIsolationPatch'
    flat = norm(patch)
    check(errors, hurt_attr in flat, "[Identity] Health.Hurt 广播隔离补丁类缺失或目标改变")
    check(errors, loot_attr in flat, "[Identity] CharacterMainControl.OnDead 掉落广播隔离补丁类缺失或目标改变")
    for name in ("HealthDeathBroadcastIsolationPatch", "CharacterLootBroadcastIsolationPatch"):
        block = norm(class_block(patch, name))
        check(errors, block.count("[HarmonyTranspiler]") == 1, "[TranspilerOnly] " + name + " 必须且只能声明一个 Transpiler")
        for forbidden in ("[HarmonyPrefix]", "[HarmonyPostfix]", "[HarmonyFinalizer]", "__result", "__instance"):
            check(errors, forbidden not in block, "[TranspilerOnly] " + name + " 不得出现 " + forbidden)
    hurt_transpiler = norm(class_block(patch, "HealthDeathBroadcastIsolationPatch"))
    check(errors, "DeathBroadcastIsolation.IsolateHurtBroadcasts( instructions, out deathEvent, out death, out hurt);" in hurt_transpiler,
          "[Wiring] Hurt transpiler 必须调用 IsolateHurtBroadcasts")
    check(errors, "return codes;" in hurt_transpiler, "[Wiring] Hurt transpiler 必须返回改写结果")
    loot_transpiler = norm(class_block(patch, "CharacterLootBroadcastIsolationPatch"))
    check(errors, "DeathBroadcastIsolation.IsolateBeforeLootBroadcast(instructions, out rewritten);" in loot_transpiler,
          "[Wiring] OnDead transpiler 必须调用 IsolateBeforeLootBroadcast")

    # --- 三处 + 一处广播都接到对应隔离方法 ---
    hurt_rewrite = norm(body(patch, "internal static List<CodeInstruction> IsolateHurtBroadcasts("))
    for field, delegate_type, method in (
        ("OnDeadEvent", "typeof(UnityEvent<DamageInfo>)", "InvokeDeathUnityEvent"),
        ("OnDead", "typeof(Action<Health, DamageInfo>)", "InvokeDeathBroadcast"),
        ("OnHurt", "typeof(Action<Health, DamageInfo>)", "InvokeHurtBroadcast"),
    ):
        expected = ('AccessTools.Field(typeof(Health), "' + field + '"), ' + delegate_type
                    + ", AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(" + method + ")));")
        check(errors, expected in hurt_rewrite, "[Rewrite] Health." + field + " 未接到 " + method)
    loot_rewrite = norm(body(patch, "internal static List<CodeInstruction> IsolateBeforeLootBroadcast("))
    check(errors, ('AccessTools.Field(typeof(CharacterMainControl), "BeforeCharacterSpawnLootOnDead"), typeof(Action<DamageInfo>), '
                   'AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(InvokeBeforeLootBroadcast)));') in loot_rewrite,
          "[Rewrite] BeforeCharacterSpawnLootOnDead 未接到 InvokeBeforeLootBroadcast")

    # --- 改写只在原地换调用：不插入、不删除 ---
    rewrite = norm(body(patch, "private static int RewriteBroadcastCalls("))
    check(errors, "codes[j].opcode = OpCodes.Call; codes[j].operand = isolated;" in rewrite,
          "[InPlace] 必须原地把 Invoke 换成隔离调用")
    for forbidden in (".Insert(", ".InsertRange(", ".RemoveAt(", ".RemoveRange(", ".Remove(", ".Add("):
        check(errors, forbidden not in rewrite, "[InPlace] 改写不得增删指令: " + forbidden)
    check(errors, 'target.Name == "Invoke" && target.DeclaringType == delegateType' in rewrite,
          "[InPlace] 只能改写对应广播类型的 Invoke")

    # --- 逐订阅者 try/catch ---
    isolated = norm(body(patch, "private static void InvokeIsolated("))
    check(errors, re.search(r"for \(int i = 0; i < list\.Length; i\+\+\) \{ try \{ list\[i\]\(health, damageInfo\); \} catch \(Exception exception\) \{ ReportSubscriberFault\(channel, list\[i\], exception\); \} \}", isolated),
          "[PerSubscriber] 静态多播必须逐订阅者 try/catch")
    check(errors, "if (list == null || !ReferenceEquals(broadcast, cachedSource))" in isolated and "broadcast.GetInvocationList()" in isolated,
          "[ZeroAlloc] 静态多播调用列表必须按委托引用缓存，只在换新时 GetInvocationList")
    for method, channel in (("InvokeDeathBroadcast", "cachedDeathSource, ref cachedDeathList, \"Health.OnDead\""),
                            ("InvokeHurtBroadcast", "cachedHurtSource, ref cachedHurtList, \"Health.OnHurt\"")):
        check(errors, channel in norm(body(patch, "internal static void " + method + "(")),
              "[PerSubscriber] " + method + " 必须走带独立缓存的 InvokeIsolated")
    loot = norm(body(patch, "internal static void InvokeBeforeLootBroadcast("))
    check(errors, re.search(r"for \(int i = 0; i < list\.Length; i\+\+\) \{ Action<DamageInfo> handler = \(Action<DamageInfo>\)list\[i\]; try \{ handler\(damageInfo\); \} catch", loot),
          "[PerSubscriber] 掉落广播必须逐订阅者 try/catch")
    unity = norm(body(patch, "internal static void InvokeDeathUnityEvent("))
    check(errors, "calls = prepareInvokeMethod.Invoke(deathEvent, null) as IList;" in unity,
          "[PerListener] OnDeadEvent 必须取官方 PrepareInvoke 执行快照")
    check(errors, re.search(r"for \(int i = 0; i < calls\.Count; i\+\+\) \{.*try \{ invokableCallInvokeMethod\.Invoke\(call, invokeParameters\); \} catch \(TargetInvocationException exception\)", unity),
          "[PerListener] OnDeadEvent 必须逐监听器 try/catch")
    check(errors, "deathEvent.Invoke(damageInfo);" in unity, "[Fallback] 反射解析失败时必须退回整体调用并 catch")

    # --- 诊断节流与清理 ---
    report = norm(body(patch, "private static void ReportSubscriberFault("))
    check(errors, "unchecked(now - lastFaultLogTick) < FaultLogIntervalMilliseconds" in report and "Debug.LogWarning(" in report,
          "[Diagnostics] 订阅者异常必须节流记录到正式构建可见的日志")
    reset = norm(body(patch, "internal static void ResetStaticCaches("))
    for field in ("cachedDeathSource = null;", "cachedDeathList = null;", "cachedHurtSource = null;", "cachedHurtList = null;"):
        check(errors, field in reset, "[Cleanup] ResetStaticCaches 缺少 " + field)
    hooks = norm(source(HOOKS))
    check(errors, "HarmonyBindingSelfCheck.ResetStaticCaches(); DeathBroadcastIsolation.ResetStaticCaches();" in hooks,
          "[Cleanup] Mod 卸载清理路径必须释放广播隔离缓存")

    # --- 丧尸 Boss 死亡兜底 ---
    tick = norm(body(source(BOSS), "internal void TickZombieModeBossController("))
    check(errors, re.search(r"return; \} SweepZombieModeSilentBossDeaths\(\); if \(runState\.CombatPhase != ZombieModeCombatPhase\.Combat\) \{ return; \}", tick),
          "[ZombieSweep] Boss 控制器每帧必须先跑死亡兜底，再处理卡死与技能")
    sweep_src = source(SWEEP)
    sweep = norm(body(sweep_src, "private void SweepZombieModeSilentBossDeaths("))
    check(errors, "if (runState.CombatPhase != ZombieModeCombatPhase.Combat || runState.CurrentWaveBossInstances.Count <= 0) { return; }" in sweep,
          "[ZombieSweep] 战斗外 / 无 Boss 时必须 O(1) 早返")
    check(errors, "zombieModeBossSilentDeathSweepNextTime = now + ZombieModeTuning.BossSilentDeathSweepIntervalSeconds;" in sweep,
          "[ZombieSweep] 兜底轮询必须节流")
    check(errors, "if (boss == null) {" in sweep and "SettleZombieModeUnroutedBoss(runId, instance);" in sweep,
          "[ZombieSweep] 已销毁 Boss 必须按本波扣除")
    check(errors, "dead = health != null && health.IsDead;" in sweep and "HandleZombieModeHealthDead(runId, health, default(DamageInfo));" in sweep,
          "[ZombieSweep] 已死 Boss 必须走同一条死亡路由")
    check(errors, "if (runState.CurrentWaveBossesRemaining <= 0) { CompleteZombieModeWave(runId); }" in sweep,
          "[ZombieSweep] 兜底扣完必须推进本波结算")
    settle = norm(body(sweep_src, "private void SettleZombieModeUnroutedBoss("))
    check(errors, "if (marker.DeathSettled || marker.RemovedFromRuntime) { return; } marker.DeathSettled = true;" in settle,
          "[ZombieSweep] 兜底结案必须与死亡回调去重")
    check(errors, "runState.CurrentWaveBossesRemaining = Mathf.Max(0, runState.CurrentWaveBossesRemaining - 1);" in settle,
          "[ZombieSweep] 兜底结案必须扣本波 Boss 计数")
    tuning = norm(source(TUNING))
    check(errors, "public const float BossSilentDeathSweepIntervalSeconds = 0.25f;" in tuning,
          "[ZombieSweep] 兜底轮询间隔应为 0.25 秒")

    # --- 执行回归已登记 ---
    regressions = (ROOT / REGRESSIONS).read_text(encoding="utf-8")
    check(errors, "DeathBroadcastIsolation" in regressions, "[Regression] 执行回归 DeathBroadcastIsolation 未登记")

    if errors:
        print("DeathBroadcastIsolationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1
    print("DeathBroadcastIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
