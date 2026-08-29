#!/usr/bin/env python3
"""
ModeHEntryIntegrationGuard — Mode H 入口接线守卫（设计提案 §17.1、§24.3、§26.1）。

不变式：
- pending entry intent 已收敛为命名枚举 BossRushPendingEntryKind，并保留 Mode G 兼容 wrapper；
- 切图前冻结 kind + 目标 sceneName/sceneID + 单调递增 sceneGeneration；
  generation 每次创建 intent 只递增一次，只有匹配的 scene callback 能消费；
- 显式 Mode H intent 先于全部自动判定；没有 H intent 时
  Mode E -> F -> G -> D -> Normal 顺序逐字保持；
- Integration 场景回调在 H intent 下跳过 Legacy 接管四件事；
- 两个 arena setup 协程在 H intent 下直接 yield break，不做 Legacy 初始化；
- ContinuousClearEnemiesUntilWaveStart 增加 Mode H 谓词；
- 唯一 runtime module：ModBehaviour 持有实例并把同一引用注册给 host，禁止二次 new；
- 旧模式最终入口只读取 risk ready / external asset blocked，
  不得读取 content ready、recovery-only 或 run owner。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

HELPER = os.path.join(REPO_ROOT, "MapSelection", "BossRushMapSelectionHelper.cs")
ENTRY_FLOW = os.path.join(REPO_ROOT, "WavesArena", "BossRushEntryFlow.cs")
INTEGRATION = os.path.join(REPO_ROOT, "Integration", "BossRushIntegration_StartAndScene.cs")
TRAVEL = os.path.join(REPO_ROOT, "Integration", "BossRushIntegration_TravelAndSetup.cs")
MAINTENANCE = os.path.join(REPO_ROOT, "WavesArena", "WavesArenaEnemyMaintenance.cs")
REGISTRATION = os.path.join(REPO_ROOT, "Common", "Lifecycle", "BossRushRuntimeModuleRegistration.cs")
MODEH_ENTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHEntry.cs")

LEGACY_ENTRIES = [
    ("ModeD/ModeD.cs", "TryStartModeD"),
    ("ModeE/ModeEStartup.cs", "TryStartModeE"),
    ("ModeF/ModeFEntry.cs", "TryStartModeF"),
    ("ModeG/ModeGEntry.cs", "TryStartModeG"),
    ("WavesArena/WavesArenaEntryAndTeleport.cs", "StartBossRush_WavesArena"),
    ("ZombieMode/ZombieModeEntry.cs", "CanStartZombieModeMapSelectionPhase1"),
    ("ZombieMode/ZombieModeMapSelection.cs", "TryBeginZombieModeMapSelectionShell"),
]

FORBIDDEN_IN_LEGACY = [
    "IsModeHContentReady",
    "IsModeHRecoveryOnlyBlocked",
    "IsModeHRunOwnerActive",
]

# 允许把风险门判定委托给同一 partial class 里的具名 helper，但**必须**指明 helper 名与
# 它所在的文件，且那个文件仍要被本 guard 逐字检查到 IsLegacyModeEntryAllowed。
# 起因：ZombieMode/ZombieModeEntry.cs 已顶到 large_file_existing_allowlist 的行数上限，
# 无法再容纳「区分扫描失败与真实押品风险」所需的分支。
LEGACY_GATE_DELEGATES = {
    "ZombieMode/ZombieModeEntry.cs": (
        "IsZombieModeStartBlocked",
        "ZombieMode/ZombieModeMapSelection.cs",
    ),
}


def main():
    errors = []

    helper = read_text(HELPER)
    if helper is None:
        errors.append("[File] 缺少 MapSelection/BossRushMapSelectionHelper.cs")
    else:
        code = strip_cs_comments(helper)
        checks = [
            (r"public enum BossRushPendingEntryKind", "typed pending entry kind 枚举"),
            (r"None = 0,", "枚举显式整数"),
            (r"ModeG = 1,", "Mode G 枚举值"),
            (r"ModeH = 2", "Mode H 枚举值"),
            (r"public static bool HasPendingModeGEntryIntent\(\)", "保留 Mode G 兼容查询"),
            (r"public static void MarkEntryFlowFromMapSelectionUi\(bool modeGEntryIntent\)",
             "保留 Mode G 布尔 overload"),
            (r"public static void MarkEntryFlowFromDirectTeleport\(bool modeGEntryIntent\)",
             "保留 Mode G 直传 overload"),
            (r"public static bool HasPendingModeHEntryIntent\(\)", "Mode H typed 查询"),
            (r"public static int FreezeModeHEntryIntent\(string targetSceneName, string targetSceneId\)",
             "切图前冻结 scene pair 与 generation"),
            (r"modeHSceneGenerationCounter\+\+;", "generation 单调递增"),
            (r"public static bool TryMatchModeHSceneIntent\(", "只有匹配场景能消费 intent"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[Helper] 不满足: " + desc)

        freeze = re.search(r"public static int FreezeModeHEntryIntent\([\s\S]{0,600}?\n        \}", code)
        if freeze and freeze.group(0).count("modeHSceneGenerationCounter++") != 1:
            errors.append("[Helper] 创建 intent 时 generation 必须只递增一次")

        match_fn = re.search(r"public static bool TryMatchModeHSceneIntent\([\s\S]{0,1200}?\n        \}", code)
        if match_fn:
            body = match_fn.group(0)
            if "pendingModeHTargetSceneName" not in body or "pendingModeHTargetSceneId" not in body:
                errors.append("[Helper] 场景消费判定必须同时比较 sceneName 与 sceneID")
            if "modeHSceneGenerationCounter++" in body:
                errors.append("[Helper] 场景消费判定不得再次递增 generation")

        clear = re.search(r"public static void ClearPendingEntryFlowState\(\)[\s\S]{0,700}?\n        \}", code)
        if clear:
            body = clear.group(0)
            for field in ["pendingEntryKind = BossRushPendingEntryKind.None;",
                          "pendingModeHTargetSceneName = null;",
                          "pendingModeHSceneGeneration = 0;"]:
                if field not in body:
                    errors.append("[Helper] 清理未覆盖: " + field)

    entry_flow = read_text(ENTRY_FLOW)
    if entry_flow is None:
        errors.append("[File] 缺少 WavesArena/BossRushEntryFlow.cs")
    else:
        code = strip_cs_comments(entry_flow)
        if not re.search(r"ModeG,\s*\n\s*ModeH", code):
            errors.append("[EntryFlow] BossRushEntryMode 未追加 ModeH")
        determine = re.search(
            r"private BossRushEntryMode DetermineBossRushEntryMode\(string context\)[\s\S]*?\n        \}", code)
        if not determine:
            errors.append("[EntryFlow] 未找到入场判定方法")
        else:
            body = determine.group(0)
            h_pos = body.find("HasPendingModeHEntryIntent")
            e_pos = body.find("BossRushEntryMode.ModeE")
            f_pos = body.find("BossRushEntryMode.ModeF")
            g_pos = body.find("BossRushEntryMode.ModeG")
            d_pos = body.find("BossRushEntryMode.ModeD")
            if h_pos < 0:
                errors.append("[EntryFlow] 缺少显式 Mode H intent 判定")
            elif not (h_pos < e_pos < f_pos < g_pos < d_pos):
                errors.append("[EntryFlow] 判定顺序必须是 H > E > F > G > D")
            if "DetectBossRushTicketItem" not in body:
                errors.append("[EntryFlow] 旧判定链被破坏（缺少船票检测）")
        if not re.search(r"internal bool IsModeHEntryIntentNow\(\)", code):
            errors.append("[EntryFlow] 缺少 Mode H intent 查询")
        if not re.search(r"if \(entryMode == BossRushEntryMode\.ModeH\)[\s\S]{0,400}?yield break;", code):
            errors.append("[EntryFlow] DemoChallenge setup 缺少 Mode H 早退分支")

    integration = read_text(INTEGRATION)
    if integration is None:
        errors.append("[File] 缺少 Integration/BossRushIntegration_StartAndScene.cs")
    else:
        code = strip_cs_comments(integration)
        if not re.search(r"bool deferArenaCommitForModeH =", code):
            errors.append("[Integration] 缺少 Mode H defer 判定")
        if not re.search(r"bool deferArenaCommit = deferArenaCommitForModeG \|\| deferArenaCommitForModeH;", code):
            errors.append("[Integration] Legacy 接管必须由 G/H 组合判定门控")
        if code.count("if (!deferArenaCommit)") != 2:
            errors.append("[Integration] 必须恰好两处使用组合 defer 判定（arena commit 与 Legacy 清理块）")
        if re.search(r"if \(!deferArenaCommitForModeG\)", code):
            errors.append("[Integration] 仍存在只看 Mode G 的旧判定")

    travel = read_text(TRAVEL)
    if travel is None:
        errors.append("[File] 缺少 Integration/BossRushIntegration_TravelAndSetup.cs")
    else:
        code = strip_cs_comments(travel)
        if not re.search(r"if \(entryMode == BossRushEntryMode\.ModeH\)[\s\S]{0,400}?yield break;", code):
            errors.append("[Travel] GroundZero setup 缺少 Mode H 早退分支")

    maintenance = read_text(MAINTENANCE)
    if maintenance and "IsModeHRunInProgressSafe()" not in strip_cs_comments(maintenance):
        errors.append("[Maintenance] ContinuousClear 循环缺少 Mode H 谓词")

    registration = read_text(REGISTRATION)
    if registration is None:
        errors.append("[File] 缺少 Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
    else:
        code = strip_cs_comments(registration)
        if not re.search(r"modeHRuntime = new ModeHRuntimeModule\(\);", code):
            errors.append("[Host] Mode H 实例必须先保存到字段")
        if not re.search(r"runtimeModuleHost\.Register\(modeHRuntime\);", code):
            errors.append("[Host] 必须把同一个引用注册给 host")
        if re.search(r"Register\(new ModeHRuntimeModule\(\)\)", code):
            errors.append("[Host] 禁止注册匿名新实例（会造成入口/host 实例分裂）")
        if not re.search(r"internal ModeHRuntimeModule ModeHRuntime", code):
            errors.append("[Host] 缺少唯一实例只读门面")

    # 全仓禁止二次 new ModeHRuntimeModule
    for root, _dirs, files in os.walk(REPO_ROOT):
        if any(part in root for part in (".git", "Build", "鸭科夫源码", "tests", "wiki-site")):
            continue
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            code = strip_cs_comments(read_text(path) or "")
            if "new ModeHRuntimeModule()" in code and name != "BossRushRuntimeModuleRegistration.cs":
                errors.append("[Host] {} 出现二次 new ModeHRuntimeModule()".format(name))

    # 旧模式入口只读两个门
    for rel, method in LEGACY_ENTRIES:
        text = read_text(os.path.join(REPO_ROOT, rel))
        if text is None:
            errors.append("[Legacy] 缺少文件: " + rel)
            continue
        code = strip_cs_comments(text)
        if "IsLegacyModeEntryAllowed" not in code:
            delegate = LEGACY_GATE_DELEGATES.get(rel)
            if delegate is None or delegate[0] not in code:
                errors.append("[Legacy] {} 未接入 Mode H 真实资产风险门".format(rel))
            else:
                # 委托目标必须真的读了门，否则等于把判定丢了
                host = strip_cs_comments(read_text(os.path.join(REPO_ROOT, delegate[1])) or "")
                if delegate[0] not in host or "IsLegacyModeEntryAllowed" not in host:
                    errors.append(
                        "[Legacy] {} 委托的 {} 未在 {} 中真正读取风险门".format(
                            rel, delegate[0], delegate[1]))
        for forbidden in FORBIDDEN_IN_LEGACY:
            if forbidden in code:
                errors.append("[Legacy] {} 不得读取 Mode H 门: {}".format(rel, forbidden))

    modeh_entry = read_text(MODEH_ENTRY)
    if modeh_entry is None:
        errors.append("[File] 缺少 ModeH/ModeHEntry.cs")
    else:
        code = strip_cs_comments(modeh_entry)
        checks = [
            (r"internal static bool TryEnter\(ModBehaviour owner, string sceneName, string sceneId, out string reasonId\)",
             "入口方法签名"),
            (r"BossRushMapSelectionHelper\.FreezeModeHEntryIntent\(map\.SceneName, map\.SceneId\)",
             "切图前冻结 scene pair"),
            (r"HasLegacyModeConflictForModeH", "旧模式冲突拒绝"),
            (r"ModeHPresentationAssetCache\.TryPreflight\(\)", "展示资源预检"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[Entry] 不满足: " + desc)

    if errors:
        print("ModeHEntryIntegrationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHEntryIntegrationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
