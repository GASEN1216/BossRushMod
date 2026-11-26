#!/usr/bin/env python3
"""
HostStartupIsolationGuard — 宿主启动期防护（2026-11-26 玩家反馈「每次重进 Mod 都加载不出来，身上 Mod 装备消失」）。

不变式：
1. 前置程序集：BossRushStartupGuard.cs 不得引用任何 Harmony / HarmonyLib 类型（它要在第一次触碰 Harmony 之前运行）；
   BeginAwake 里调 EnsureHarmonyLoaded；优先前置目录的副本；前置创意工坊 ID 与 compile_official.bat 的
   HARMONY_MOD_ID 一致（旧脚本写错成 3588386576）。
2. Awake 顺序与隔离：BeginAwake 在 RegisterRuntimeModules 与所有初始化阶段之前；五个初始化阶段各自单行
   try/catch，失败只记阶段；补丁层失败走 FailBootstrap，退回同步登记全部 Mod 物品；CompleteAwake 收尾。
3. 正式构建可见：启动自检一行用 Debug.Log / Debug.LogWarning，不用 DevLog（DevLog 在正式构建里整条消失）。
4. 生命周期：CompleteAwake 接上装备抢救；宿主 OnDestroy 调 Shutdown，Shutdown 退订装备抢救。
每条断言都有内存变异探针，探针不转红本守卫自判失败。文本守卫防不住「保留 token、杀掉执行路径」，
行为正确性仍以 Windows 编译与实机为准。
"""
import re
import sys

from guard_mutation_util import cleaned, method_body, run_guard, squash

FILES = {
    "host": "ModBehaviour.cs",
    "startup": "Common/Lifecycle/BossRushStartupGuard.cs",
    "compile_bat": "compile_official.bat",
}

STAGES = [
    ("bootstrap", r"try \{ InitializeBootstrapRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailBootstrap\(e\); \}"),
    ("always-on", r'try \{ InitializeAlwaysOnRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("always-on", e\); \}'),
    ("player-lifecycle", r'try \{ RegisterPlayerLifecycleRuntimeEvents\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("player-lifecycle", e\); \}'),
    ("debug-tools", r'try \{ InitializeDebugToolsRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("debug-tools", e\); \}'),
    ("achievement", r'try \{ InitializeAchievementRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("achievement", e\); \}'),
]


def check(raw):
    src = cleaned(raw)
    host, startup, bat = src["host"], src["startup"], src["compile_bat"]
    errors = []

    if re.search(r"\bHarmony(Lib)?\b", startup):
        errors.append("[前置] BossRushStartupGuard.cs 不得引用 Harmony / HarmonyLib 类型，否则预加载前 JIT 就失败")
    begin = squash(method_body(startup, "internal static void BeginAwake()"))
    if "harmonySource = EnsureHarmonyLoaded();" not in begin:
        errors.append("[前置] BeginAwake 必须调 EnsureHarmonyLoaded，先于宿主第一次触碰 Harmony")
    if "return prerequisite ?? best;" not in squash(method_body(startup, "private static string FindHarmonyCopy()")):
        errors.append("[前置] FindHarmonyCopy 必须优先前置 HarmonyLib 目录里的副本")
    guard_id = re.search(r'HarmonyPrerequisiteWorkshopId\s*=\s*"(\d+)"', startup)
    bat_id = re.search(r"^set HARMONY_MOD_ID=(\d+)\s*$", bat, re.M)
    if not guard_id or not bat_id or guard_id.group(1) != bat_id.group(1):
        errors.append("[前置] 前置创意工坊 ID 必须与 compile_official.bat 的 HARMONY_MOD_ID 一致: %s vs %s"
                      % (guard_id and guard_id.group(1), bat_id and bat_id.group(1)))

    awake = squash(method_body(host, "void Awake()"))
    begin_at = awake.find("BossRushStartupGuard.BeginAwake();")
    register_at = awake.find("RegisterRuntimeModules();")
    if begin_at < 0 or register_at < 0 or begin_at > register_at:
        errors.append("[顺序] Awake 必须先 BossRushStartupGuard.BeginAwake()，再 RegisterRuntimeModules()")
    last = register_at
    for stage, pattern in STAGES:
        match = re.search(pattern, awake)
        if not match:
            errors.append("[隔离] Awake 的 %s 阶段必须单行 try/catch 并上报 BossRushStartupGuard" % stage)
            continue
        if match.start() < last:
            errors.append("[顺序] Awake 的 %s 阶段顺序不对" % stage)
        last = match.start()
    complete_at = awake.find("BossRushStartupGuard.CompleteAwake(this);")
    if complete_at < 0 or complete_at < last:
        errors.append("[顺序] Awake 最后必须调 BossRushStartupGuard.CompleteAwake(this)")

    fail_stage = squash(method_body(startup, "internal static void FailStage(string stage, Exception e)"))
    if "failedStages.Add(stage);" not in fail_stage or "Debug.LogError(" not in fail_stage:
        errors.append("[隔离] FailStage 必须记阶段名并用 Debug.LogError 输出（正式构建可见）")
    fail_bootstrap = squash(method_body(startup, "internal static void FailBootstrap(Exception e)"))
    if 'FailStage("harmony-bootstrap", e);' not in fail_bootstrap \
            or "BossRushDynamicItemRegistry.EnsureAllRegistered()" not in fail_bootstrap:
        errors.append("[兜底] 补丁层失败时必须同步登记全部 Mod 物品，保住读档")

    complete = squash(method_body(startup, "internal static void CompleteAwake(ModBehaviour host)"))
    if "Debug.LogWarning(line);" not in complete or "Debug.Log(line);" not in complete or "DevLog(" in complete:
        errors.append("[自检] 启动自检必须用 Debug.Log / LogWarning 输出，不能用 DevLog")
    if "BossRushEquipmentRescue.Attach(host, lateActivation)" not in complete:
        errors.append("[接线] CompleteAwake 必须接上装备抢救（含迟到激活标记）")

    if "BossRushStartupGuard.Shutdown();" not in squash(method_body(host, "void OnDestroy()")):
        errors.append("[生命周期] 宿主 OnDestroy 必须调 BossRushStartupGuard.Shutdown()")
    if "BossRushEquipmentRescue.Shutdown();" not in squash(method_body(startup, "internal static void Shutdown()")):
        errors.append("[生命周期] BossRushStartupGuard.Shutdown 必须退订装备抢救")
    return errors


PROBES = [
    ("startup", "using System;", "using System; using HarmonyLib;", "启动防护引用 HarmonyLib", "不得引用 Harmony"),
    ("startup", "harmonySource = EnsureHarmonyLoaded();", 'harmonySource = "skipped";', "不再预加载 0Harmony", "EnsureHarmonyLoaded"),
    ("startup", "return prerequisite ?? best;", "return best;", "不再优先前置目录", "优先前置"),
    ("startup", 'HarmonyPrerequisiteWorkshopId = "3589088839"', 'HarmonyPrerequisiteWorkshopId = "3588386576"', "前置 ID 与构建脚本不一致", "HARMONY_MOD_ID 一致"),
    ("compile_bat", "set HARMONY_MOD_ID=3589088839", "set HARMONY_MOD_ID=3588386576", "构建脚本退回错误的前置 ID", "HARMONY_MOD_ID 一致"),
    ("host", "BossRushStartupGuard.BeginAwake();", "", "Awake 不再先调 BeginAwake", "先 BossRushStartupGuard.BeginAwake"),
    ("host", 'try { InitializeAlwaysOnRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("always-on", e); }',
     "InitializeAlwaysOnRuntime();", "常驻阶段失去隔离", "always-on 阶段必须"),
    ("host", "try { InitializeBootstrapRuntime(); } catch (Exception e) { BossRushStartupGuard.FailBootstrap(e); }",
     'try { InitializeBootstrapRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("harmony-bootstrap", e); }',
     "补丁层失败不再走物品登记兜底", "bootstrap 阶段必须"),
    ("host", "BossRushStartupGuard.CompleteAwake(this);", "", "Awake 不再收尾自检", "CompleteAwake(this)"),
    ("host", "BossRushStartupGuard.Shutdown();", "", "OnDestroy 不再关停启动防护", "宿主 OnDestroy"),
    ("startup", "failedStages.Add(stage);", "", "失败阶段不再记录", "FailStage 必须"),
    ("startup", 'RunStage("dynamic-items-fallback", () => BossRushDynamicItemRegistry.EnsureAllRegistered());', "",
     "兜底登记被删", "[兜底]"),
    ("startup", "else Debug.Log(line);", "else ModBehaviour.DevLog(line);", "启动自检退回 DevLog", "[自检]"),
    ("startup", "RunStage(\"equipment-rescue\", () => BossRushEquipmentRescue.Attach(host, lateActivation));", "",
     "装备抢救没接上", "[接线]"),
    ("startup", "BossRushEquipmentRescue.Shutdown();", "", "Shutdown 不再退订装备抢救", "退订装备抢救"),
]


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    sys.exit(run_guard("HostStartupIsolationGuard", FILES, check, PROBES))
