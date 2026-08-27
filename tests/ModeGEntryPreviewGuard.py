#!/usr/bin/env python3
"""
ModeGEntryPreviewGuard — Mode G 入口 preview 守卫（规格 §20 第 4 条）。

不变式：
- ModeGEntryPreview 不可变（sealed + readonly 字段），冻结 runSeed/runFormat/
  署名轮换/两个契约候选/scene pair/能力 revision（>=6 项）；
- ExpirySeconds 冻结 300 秒；过期 preview 不进 Starting；
- GetOrCreateModeGEntryPreview 对未过期 preview 原样复用（取消重开不刷契约候选）；
- Entry 拒绝路径不写 Legacy 存档/BossFilter（静态禁止项）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENTRY = os.path.join(REPO_ROOT, "ModeG", "ModeGEntry.cs")
INTERACTABLE = os.path.join(REPO_ROOT, "ModeG", "ModeGInteractable.cs")
RUNTIME = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")
BRIDGE = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeBridge.cs")
SCENE_ENTRY = os.path.join(REPO_ROOT, "Integration", "BossRushIntegration_StartAndScene.cs")
MAP_HELPER = os.path.join(REPO_ROOT, "MapSelection", "BossRushMapSelectionHelper.cs")
LEGACY_INTERACTABLE = os.path.join(REPO_ROOT, "Interactables", "BossRushInteractables.cs")
DIRECT_ENTRY = os.path.join(REPO_ROOT, "WavesArena", "WavesArenaEntryAndTeleport.cs")
ENTRY_FLOWS = [
    os.path.join(REPO_ROOT, "WavesArena", "BossRushEntryFlow.cs"),
    os.path.join(REPO_ROOT, "Integration", "BossRushIntegration_TravelAndSetup.cs"),
]

FROZEN_FIELDS = [
    "runSeed",
    "runFormat",
    "signatureRotation",
    "contractCandidateIds",
    "sceneName",
    "sceneId",
    "verificationRevision",
]


def main():
    errors = []
    if not os.path.exists(ENTRY):
        print("ModeGEntryPreviewGuard: FAIL (1 errors)")
        print("  - ModeGEntry.cs 不存在")
        return 1

    with open(ENTRY, "r", encoding="utf-8", errors="replace") as fh:
        content = fh.read()

    if not re.search(r"public sealed class ModeGEntryPreview", content):
        errors.append("[SealedPreview] ModeGEntryPreview 必须是 sealed class")

    if not re.search(r"public const double ExpirySeconds = 300\.0", content):
        errors.append("[ExpiryFrozen] ExpirySeconds 必须冻结为 300.0")

    for field in FROZEN_FIELDS:
        if not re.search(r"public readonly [\w\[\]<>]+ " + field + r"\s*;", content):
            errors.append("[FrozenField:{}] preview 缺少 readonly 冻结字段".format(field))

    if not re.search(r"public bool IsFresh\(long nowTicks\)", content):
        errors.append("[IsFresh] preview 缺少过期判定 IsFresh")

    # 过期 preview 不进 Starting，且必须绑定当前 runtime scene + 冻结 pair/revision。
    if not re.search(
            r"IsModeGEntryPreviewValidForCurrentScene\(ModeGEntryPreview preview\)"
            r"[\s\S]{0,180}?!preview\.IsFresh\(DateTime\.UtcNow\.Ticks\)", content):
        errors.append("[ExpiredRejected] TryStartModeG 未拒绝过期 preview")
    if not re.search(
            r"GetActiveScene\(\)\.name[\s\S]{0,180}?preview\.sceneName"
            r"[\s\S]{0,180}?preview\.verificationRevision"
            r"[\s\S]{0,220}?ModeGMapSupportRegistry\.IsSupported\("
            r"\s*preview\.sceneName, preview\.sceneId, preview\.verificationRevision\)", content):
        errors.append("[ExactSceneRevision] preview 未绑定当前 sceneName + sceneId + revision")
    if not re.search(
            r"ModeGEntryPreview preview = modeGEntryPreview \?\? GetOrCreateModeGEntryPreview\(\);"
            r"[\s\S]{0,180}?if \(!IsModeGEntryPreviewValidForCurrentScene\(preview\)\)", content):
        errors.append("[StartConsumesPreviewValidation] TryStartModeG 未消费完整 preview 校验")

    # 取消后在同地图重开不刷契约；换地图不得复用旧 pair 的 preview。
    if not re.search(
            r"modeGEntryPreview != null\s*&&\s*modeGEntryPreview\.IsFresh\(nowTicks\)"
            r"[\s\S]{0,320}?modeGEntryPreview\.sceneName, sceneName"
            r"[\s\S]{0,180}?modeGEntryPreview\.sceneId, sceneId"
            r"[\s\S]{0,260}?return modeGEntryPreview;", content):
        errors.append("[ReuseFresh] preview 未限定同一冻结 scene pair 后复用")

    # 契约候选恰好 2 个、升序（注释与结构约束）
    if "contractCandidateIds" in content and not re.search(
            r"恰好 2 个|升序", content):
        errors.append("[ContractPairDoc] 契约候选对缺少升序/恰好 2 个约束说明")

    # 拒绝路径不写 Legacy/存档/BossFilter（静态禁止项）
    for forbidden in ["BossFilter", "SaveFile", "OnSetFile", "OnCollectSaveData"]:
        if forbidden in content:
            errors.append("[NoLegacyWrite] Entry 不得触碰 {}: 出现于 ModeGEntry.cs".format(forbidden))

    # 入口事务：取消/确认前失败退还 UI 预扣船票；只在付款所有权转移后清场。
    if not re.search(
            r"TryRefundModeGPendingPrepaidTicket\(\).*?HasPendingPrepaidTicket\(\)"
            r".*?ClearPendingEntryFlowState\(\).*?TryRefundModeGEntryItem",
            content, re.DOTALL):
        errors.append("[PrepaidRefund] ModeGEntry 缺少幂等的 UI 预扣船票退款")
    if not re.search(
            r"ticketConsumed = ticketPrepaid \|\|.*?if \(ticketPrepaid\)"
            r".*?ClearPendingEntryFlowState\(\).*?StartModeGRuntime",
            content, re.DOTALL):
        errors.append("[PaymentTransfer] 必须在预扣票所有权转移后才启动 Mode G")
    if "DisableAllSpawners()" in content or "ClearEnemiesForBossRush()" in content:
        errors.append("[EntryNoDestructiveCommit] TryStartModeG 预检/初始化完成前不得破坏性清场")

    runtime_content = open(RUNTIME, "r", encoding="utf-8", errors="replace").read()
    bridge_content = open(BRIDGE, "r", encoding="utf-8", errors="replace").read()
    if not re.search(
            r"TryAdvanceLifecycle\(ModeGLifecyclePhase\.Active\).*?"
            r"PrepareModeGArenaRuntime\(_preview\).*?"
            r"CommitModeGArenaEntry\(_preview\).*?SpawnWaveAsync\(0\)",
            runtime_content, re.DOTALL):
        errors.append("[ArenaCommitOrder] 可失败准备必须在 Active 后、不可逆清场前，首波生成最后执行")
    if not re.search(
            r"internal bool CommitModeGArenaEntry\(ModeGEntryPreview preview\).*?"
            r"IsModeGEntryPreviewValidForCurrentScene\(preview\).*?"
            r"DisableAllSpawners\(\).*?ClearEnemiesForBossRush\(\).*?return true;",
            bridge_content, re.DOTALL):
        errors.append("[ArenaCommitMissing] Mode G runtime bridge 缺少统一清场提交")

    scene_content = open(SCENE_ENTRY, "r", encoding="utf-8", errors="replace").read()
    # 2026-08-28：Legacy 接管的延后判定改为 Mode G / Mode H 组合标志（typed pending entry kind）；
    # 行为不变——Mode G intent 仍在切场景前冻结并延后 active/清场提交，断言强度保持一致。
    if not re.search(
            r"deferArenaCommitForModeG\s*=\s*.*?HasPendingModeGEntryIntent\(\);\s*"
            r".*?bool deferArenaCommit = deferArenaCommitForModeG \|\| deferArenaCommitForModeH;"
            r".*?if \(!deferArenaCommit\)\s*\{\s*bossRushArenaActive = true;\s*\}"
            r".*?if \(!deferArenaCommit\).*?DisableAllSpawners\(\).*?"
            r"ContinuousClearEnemiesUntilWaveStart\(\)",
            scene_content, re.DOTALL):
        errors.append("[SceneLoadDeferral] 通用过图路径未对 Mode G 延后 active/清场提交")

    map_helper_content = open(MAP_HELPER, "r", encoding="utf-8", errors="replace").read()
    # 2026-08-28：pendingModeGEntryIntent 布尔收敛为 BossRushPendingEntryKind 枚举，
    # 布尔 overload 保留为兼容 wrapper；冻结时机与随事务清理的语义完全不变。
    if not re.search(
            r"private static BossRushPendingEntryKind pendingEntryKind = BossRushPendingEntryKind\.None;"
            r".*?MarkEntryFlowFromMapSelectionUi\(bool modeGEntryIntent\).*?"
            r"BossRushPendingEntryKind\.ModeG.*?"
            r"MarkEntryFlowFromMapSelectionUi\(BossRushPendingEntryKind kind\).*?"
            r"pendingEntryKind = kind;.*?"
            r"MarkEntryFlowFromDirectTeleport\(bool modeGEntryIntent\).*?"
            r"BossRushPendingEntryKind\.ModeG.*?"
            r"MarkEntryFlowFromDirectTeleport\(BossRushPendingEntryKind kind\).*?"
            r"pendingEntryKind = kind;.*?HasPendingModeGEntryIntent\(\).*?"
            r"pendingEntryKind == BossRushPendingEntryKind\.ModeG.*?"
            r"ClearPendingEntryFlowState\(\).*?pendingEntryKind = BossRushPendingEntryKind\.None",
            map_helper_content, re.DOTALL):
        errors.append("[FrozenEntryIntent] Mode G 过图意图未在切场景前冻结并随事务清理")
    legacy_entry_content = open(
        LEGACY_INTERACTABLE, "r", encoding="utf-8", errors="replace").read()
    direct_entry_content = open(
        DIRECT_ENTRY, "r", encoding="utf-8", errors="replace").read()
    if "ShowBossRushMapSelection(host.IsModeGEntryIntentNow());" not in legacy_entry_content:
        errors.append("[MapSelectionIntentCapture] 地图选择入口未在切场景前冻结 Mode G 意图")
    if "BossRushOption_ModeG" in legacy_entry_content \
            or "AddComponent<ModeGInteractable>" in legacy_entry_content:
        errors.append("[NoLegacySignOption] 旧 BossRush 路牌不得再注入 Mode G 独立选项")
    if "MarkEntryFlowFromDirectTeleport(IsModeGEntryIntentNow());" not in direct_entry_content:
        errors.append("[DirectIntentCapture] 直接传送入口未在切场景前冻结 Mode G 意图")
    if not re.search(
            r"TryRefundModeGPendingPrepaidTicket\(\).*?ClearPendingEntryFlowState\(\)"
            r".*?RollbackModeGStagedArenaEntry\(\)",
            content, re.DOTALL):
        errors.append("[StagingRollback] 取消/预检失败未释放 Mode G 竞技场暂存所有权")

    if not os.path.exists(INTERACTABLE):
        errors.append("[InteractableMissing] ModeGInteractable.cs 不存在")
    else:
        interactable = open(INTERACTABLE, "r", encoding="utf-8", errors="replace").read()
        if not re.search(
                r"if \(!wasConfirmed\).*?TryRefundModeGPendingPrepaidTicket\(\)",
                interactable, re.DOTALL):
            errors.append("[CancelRefund] 确认页取消未退还 UI 预扣船票")
        if not re.search(
                r"LastConfirmationAttemptedStart = true.*?TryStartModeG\(\)",
                interactable, re.DOTALL):
            errors.append("[CancelResult] 确认页未区分取消与实际启动失败")
        if not re.search(
                r"private bool OpenConfirmPage\(.*?ClaimModalInput\(root, \"ModeGConfirmPage\"\)"
                r".*?!ZombieModeUIHelper\.IsModalInputPaused.*?CloseModal\(\);.*?return false;",
                interactable, re.DOTALL):
            errors.append("[ModalPauseFailClosed] 确认页未在暂停租约失败时关闭并返回失败")
        if not re.search(
                r"TryOpenConfirmation\(ModBehaviour host\).*?IsVerifiedSceneName\("
                r".*?TryPreflight\(\).*?IsModeGEntryPreviewValidForCurrentScene\(preview\)"
                r".*?return presenter\.OpenConfirmPage\(host, preview\);",
                interactable, re.DOTALL):
            errors.append("[AutomaticEntryPreflight] 自动入口未消费地图/展示/preview 完整预检")

    for flow in ENTRY_FLOWS:
        flow_content = open(flow, "r", encoding="utf-8", errors="replace").read()
        match = re.search(
            r"if \(entryMode == BossRushEntryMode\.ModeG\)([\s\S]*?)yield break;",
            flow_content)
        if not match:
            errors.append("[EntryFlowMissing] {} 缺少 Mode G 分支".format(os.path.relpath(flow, REPO_ROOT)))
            continue
        before_modal = match.group(1).split("TryOpenConfirmation", 1)[0]
        if "DisableAllSpawners()" in before_modal or "ClearEnemiesForBossRush()" in before_modal:
            errors.append("[CancelSceneMutation] {} 在确认页前破坏性清场".format(
                os.path.relpath(flow, REPO_ROOT)))
        if "TryRefundModeGPendingPrepaidTicket()" not in match.group(1):
            errors.append("[OpenFailureRefund] {} 确认页打开失败未退还预扣票".format(
                os.path.relpath(flow, REPO_ROOT)))

    if errors:
        print("ModeGEntryPreviewGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGEntryPreviewGuard: PASS")
    print("  冻结字段: {} 项".format(len(FROZEN_FIELDS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
