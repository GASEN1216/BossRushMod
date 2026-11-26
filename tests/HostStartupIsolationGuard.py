#!/usr/bin/env python3
"""
HostStartupIsolationGuard — 宿主启动期防护（2026-11-26 玩家反馈「每次重进 Mod 都加载不出来，身上 Mod 装备消失」）。

不变式：
1. 前置程序集：BossRushStartupGuard.cs 不得引用任何 Harmony / HarmonyLib 类型（它要在第一次触碰 Harmony 之前运行）；
   BeginAwake 里调 EnsureHarmonyLoaded；候选排序前置目录的副本第一、其余按版本从高到低；逐个 LoadFrom，
   一份抛错就换下一份（首选副本损坏时不能就此放弃）；前置创意工坊 ID 与 compile_official.bat 的
   HARMONY_MOD_ID 一致（旧脚本写错成 3588386576）。
2. Awake 顺序与隔离：BeginAwake 在 RegisterRuntimeModules 与所有初始化阶段之前；模块登记与五个初始化阶段
   各自单行 try/catch，失败只记阶段；补丁层失败走 FailBootstrap，退回同步登记全部 Mod 物品；CompleteAwake 收尾。
3. Start 隔离：集成启动单行 try/catch，失败后模块 OnStart 照常分发；CompleteStart 收尾。
4. 迟到激活：激活时已身处关卡场景（LevelManager.Instance 非空）就算迟到，不能等到主角已写入才算
   （官方 CreateMainCharacterAsync 先恢复物品树、隔几帧才写 mainCharacter）。
5. 正式构建可见：启动自检在 Start 末尾用 Debug.Log / Debug.LogWarning 输出，不用 DevLog（DevLog 在正式构建里整条消失）。
6. 生命周期：CompleteAwake 接上装备抢救；宿主 OnDestroy 调 Shutdown，Shutdown 退订装备抢救。
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

REGISTER = 'try { RegisterRuntimeModules(); } catch (Exception e) { BossRushStartupGuard.FailStage("register-modules", e); }'
STAGES = [
    ("bootstrap", r"try \{ InitializeBootstrapRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailBootstrap\(e\); \}"),
    ("always-on", r'try \{ InitializeAlwaysOnRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("always-on", e\); \}'),
    ("player-lifecycle", r'try \{ RegisterPlayerLifecycleRuntimeEvents\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("player-lifecycle", e\); \}'),
    ("debug-tools", r'try \{ InitializeDebugToolsRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("debug-tools", e\); \}'),
    ("achievement", r'try \{ InitializeAchievementRuntime\(\); \} catch \(Exception e\) \{ BossRushStartupGuard\.FailStage\("achievement", e\); \}'),
]
START = ('try { StartIntegrationRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("integration-start", e); } '
         'runtimeModuleHost.OnStart(); BossRushStartupGuard.CompleteStart();')
RETRY = ('for (int i = 0; i < copies.Count; i++) { string folder = SafeFolderName(copies[i]); '
         'try { Assembly preloaded = Assembly.LoadFrom(copies[i]); return "preloaded:"')
RETRY_CATCH = ('catch (Exception e) { if (firstFailure == null) firstFailure = e.GetType().Name + "@" + folder; } } '
               'return "error:" + firstFailure')


def check(raw):
    src = cleaned(raw)
    host, startup, bat = src["host"], src["startup"], src["compile_bat"]
    errors = []

    if re.search(r"\bHarmony(Lib)?\b", startup):
        errors.append("[前置] BossRushStartupGuard.cs 不得引用 Harmony / HarmonyLib 类型，否则预加载前 JIT 就失败")
    begin = squash(method_body(startup, "internal static void BeginAwake()"))
    if "harmonySource = EnsureHarmonyLoaded();" not in begin:
        errors.append("[前置] BeginAwake 必须调 EnsureHarmonyLoaded，先于宿主第一次触碰 Harmony")
    copies = squash(method_body(startup, "private static List<string> FindHarmonyCopies()"))
    if "if (aFirst != bFirst) return aFirst ? -1 : 1;" not in copies \
            or "int byVersion = versions[b].CompareTo(versions[a]);" not in copies:
        errors.append("[前置] 候选必须前置 HarmonyLib 目录的副本第一、其余按版本从高到低")
    ensure = squash(method_body(startup, "internal static string EnsureHarmonyLoaded()"))
    retry_at = ensure.find(RETRY)
    if retry_at < 0 or ensure.find(RETRY_CATCH, retry_at) < 0:
        errors.append("[重试] EnsureHarmonyLoaded 必须逐个 LoadFrom 候选，一份抛错就换下一份")
    guard_id = re.search(r'HarmonyPrerequisiteWorkshopId\s*=\s*"(\d+)"', startup)
    bat_id = re.search(r"^set HARMONY_MOD_ID=(\d+)\s*$", bat, re.M)
    if not guard_id or not bat_id or guard_id.group(1) != bat_id.group(1):
        errors.append("[前置] 前置创意工坊 ID 必须与 compile_official.bat 的 HARMONY_MOD_ID 一致: %s vs %s"
                      % (guard_id and guard_id.group(1), bat_id and bat_id.group(1)))

    if "lateActivation = LevelManager.Instance != null;" not in begin or "MainCharacter" in begin:
        errors.append("[迟到] 激活时已在关卡场景就算迟到激活，不能等主角写入后才算")

    awake = squash(method_body(host, "void Awake()"))
    begin_at = awake.find("BossRushStartupGuard.BeginAwake();")
    register_at = awake.find(REGISTER)
    if register_at < 0:
        errors.append("[隔离] Awake 的模块登记必须单行 try/catch 并上报 register-modules")
    elif begin_at < 0 or begin_at > register_at:
        errors.append("[顺序] Awake 必须先 BossRushStartupGuard.BeginAwake()，再登记模块")
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

    if squash(method_body(host, "void Start()")).strip() != START:
        errors.append("[Start] Start 必须隔离集成启动、照常分发模块 OnStart，最后 CompleteStart 打印自检")

    fail_stage = squash(method_body(startup, "internal static void FailStage(string stage, Exception e)"))
    if "failedStages.Add(stage);" not in fail_stage or "Debug.LogError(" not in fail_stage:
        errors.append("[隔离] FailStage 必须记阶段名并用 Debug.LogError 输出（正式构建可见）")
    fail_bootstrap = squash(method_body(startup, "internal static void FailBootstrap(Exception e)"))
    if 'FailStage("harmony-bootstrap", e);' not in fail_bootstrap \
            or "BossRushDynamicItemRegistry.EnsureAllRegistered()" not in fail_bootstrap:
        errors.append("[兜底] 补丁层失败时必须同步登记全部 Mod 物品，保住读档")

    complete_start = squash(method_body(startup, "internal static void CompleteStart()"))
    if "Debug.LogWarning(line);" not in complete_start or "Debug.Log(line);" not in complete_start \
            or "DevLog(" in complete_start:
        errors.append("[自检] 启动自检必须在 CompleteStart 用 Debug.Log / LogWarning 输出，不能用 DevLog")
    complete = squash(method_body(startup, "internal static void CompleteAwake(ModBehaviour host)"))
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
    ("startup", "if (aFirst != bFirst) return aFirst ? -1 : 1;", "", "不再优先前置目录", "前置 HarmonyLib 目录的副本第一"),
    ("startup", "int byVersion = versions[b].CompareTo(versions[a]);", "int byVersion = versions[a].CompareTo(versions[b]);",
     "备用副本改成从低版本试起", "按版本从高到低"),
    ("startup", 'if (firstFailure == null) firstFailure = e.GetType().Name + "@" + folder;',
     'return "error:" + e.GetType().Name;', "首选副本读不出就放弃", "[重试]"),
    ("startup", "for (int i = 0; i < copies.Count; i++)", "for (int i = 0; i < 1 && i < copies.Count; i++)",
     "只试第一份", "[重试]"),
    ("startup", 'HarmonyPrerequisiteWorkshopId = "3589088839"', 'HarmonyPrerequisiteWorkshopId = "3588386576"', "前置 ID 与构建脚本不一致", "HARMONY_MOD_ID 一致"),
    ("compile_bat", "set HARMONY_MOD_ID=3589088839", "set HARMONY_MOD_ID=3588386576", "构建脚本退回错误的前置 ID", "HARMONY_MOD_ID 一致"),
    ("startup", "lateActivation = LevelManager.Instance != null;",
     "lateActivation = LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null;",
     "迟到激活退回等主角写入", "[迟到]"),
    ("host", "BossRushStartupGuard.BeginAwake();", "", "Awake 不再先调 BeginAwake", "先 BossRushStartupGuard.BeginAwake"),
    ("host", REGISTER, "RegisterRuntimeModules();", "模块登记失去隔离", "模块登记必须单行"),
    ("host", 'try { InitializeAlwaysOnRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("always-on", e); }',
     "InitializeAlwaysOnRuntime();", "常驻阶段失去隔离", "always-on 阶段必须"),
    ("host", "try { InitializeBootstrapRuntime(); } catch (Exception e) { BossRushStartupGuard.FailBootstrap(e); }",
     'try { InitializeBootstrapRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("harmony-bootstrap", e); }',
     "补丁层失败不再走物品登记兜底", "bootstrap 阶段必须"),
    ("host", "BossRushStartupGuard.CompleteAwake(this);", "", "Awake 不再接上装备抢救", "CompleteAwake(this)"),
    ("host", 'try { StartIntegrationRuntime(); } catch (Exception e) { BossRushStartupGuard.FailStage("integration-start", e); }',
     "StartIntegrationRuntime();", "集成启动失去隔离，失败时模块 OnStart 全部跳过", "[Start]"),
    ("host", "BossRushStartupGuard.CompleteStart();", "", "Start 不再打印自检", "[Start]"),
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
