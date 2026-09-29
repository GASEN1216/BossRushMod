"""
ModeHRuntimeResilienceGuard — Mode H 比赛进行中的运行时韧性守卫（2026-09-29 实机日志复核）。

背景（玩家实机日志 + owner「这些都不能放到游戏里给玩家看到，一定要百分百可用」）：
  - `[ModeH] 运行时阶段失败: update - NullReferenceException` 后整局关停，观战租约释放，镜头回到看台身体；
    根因是危险边界伤害走 Health.Hurt，死亡分支里第三方击杀提示 Mod 读 null fromCharacter 抛 NRE，
    冒进 Mode H 的每帧驱动；
  - 同一异常让 Health.OnDead 后面的 Mode H 路由收不到死亡，「都死完了还提示有一个敌人」，到时判负；
  - 预选配装抽到其它 Mod 的枪（92235 荷鲁斯之眼）时弹匣不可用，kit_apply_magazine_missing 同一场反复技术重开；
    owner 定：兼容其它 Mod 的枪——预选按模板弹匣判据把关，实例弹匣仍不可用时降级为只放背包弹药，不重开；
  - 恢复壳（「技术中止，本场按同一看盘重开」）是开发调试信息，却在技术重试时弹出、关停后打开没有按钮；
  - 拍铃按招牌持有者在场拒绝（command_signature_owner_absent），owner 定：拍铃给所有人用；
  - 押钱结算排在战报落盘之前，后续失败回落重打会让同一场结两次。

本守卫只证明「接线与顺序在」，不证明实机行为；每条断言配一条内存变异探针，探针必须让 check() 转红。
"""
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from cs_source_util import clean_source  # noqa: E402
from ModeHOneClickFlowGuard import method_body, squeeze  # noqa: E402

FILES = {
    "module": "ModeH/ModeHRuntimeModule.cs",
    "match": "ModeH/ModeHRuntimeModule_MatchFlow.cs",
    "combat_flow": "ModeH/ModeHRuntimeModule_CombatFlow.cs",
    "bet": "ModeH/ModeHRuntimeModule_BetFlow.cs",
    "ui_flow": "ModeH/ModeHRuntimeModule_UiFlow.cs",
    "recovery": "ModeH/ModeHRuntimeModule_Recovery.cs",
    "panel": "ModeH/ModeHRecoveryPanel.cs",
    "entry": "ModeH/ModeHInteractable.cs",
    "control": "ModeH/ModeHCombatControl.cs",
    "telemetry": "ModeH/ModeHCombatTelemetry.cs",
    "rules": "ModeH/ModeHMatchRules.cs",
    "prepared": "ModeH/ModeHLoadoutKitRegistry_Prepared.cs",
    "applicator": "ModeH/ModeHLoadoutKitApplicator.cs",
    "command": "ModeH/ModeHCommandController.cs",
    "hurt_patch": "Patches/Combat/BossLethalHealthProtectionPatch.cs",
}


def read(rel):
    with open(os.path.join(REPO_ROOT, rel), "r", encoding="utf-8-sig") as handle:
        return handle.read()


def check(sources):
    errors = []
    src = dict((key, clean_source(value)) for key, value in sources.items())

    def body(key, signature):
        text = method_body(src[key], signature)
        if text is None:
            errors.append("[%s] 找不到唯一方法 %s" % (FILES[key], signature))
            return ""
        return text

    def need(text, token, why):
        if token not in squeeze(text):
            errors.append(why)

    def forbid(text, token, why):
        if token in squeeze(text):
            errors.append(why)

    def ordered(text, tokens, why):
        flat = squeeze(text)
        positions = [flat.find(t) for t in tokens]
        if -1 in positions or positions != sorted(positions):
            errors.append(why + "（顺序 %r）" % (positions,))

    # ---- 1. 每帧驱动异常不再整局关停 ----
    update = body("module", "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)")
    need(update, "HandleUpdateFailure(e);", "[update] 宿主 OnUpdate 的异常必须交给 HandleUpdateFailure 分级处理")
    forbid(update, 'RequestExit(ModeHExitReason.TechnicalAbort, "update_exception");',
           "[update] 任一帧异常直接整局关停会释放观战租约、镜头回到看台身体")
    handler = body("match", "private void HandleUpdateFailure(Exception e)")
    need(handler, "RequestTechnicalRetry(", "[update] 交战 / 生成相位的每帧异常按技术故障同场重开（不判负）")
    need(handler, "_updateFailureStreak <= UpdateFailureStreakLimit", "[update] 只有连续多帧失败才走关停兜底")

    # ---- 2. 危险边界伤害把宿主 Hurt 链路异常挡在比赛驱动之外 ----
    rules_tick = body("rules", "internal bool Tick(float deltaTime, out string reason)")
    need(rules_tick, "HurtAtEdge(participant.Health);", "[edge] 危险边界伤害必须走隔离入口")
    forbid(rules_tick, ".Hurt(_edgeDamage)", "[edge] Tick 里不得直接调 Health.Hurt")
    edge = body("rules", "private void HurtAtEdge(Health health)")
    need(edge, "catch (Exception e)", "[edge] Hurt 链路异常必须在这里接住并留原始栈")
    finalizer = body("hurt_patch", "private static Exception Finalizer(Exception __exception, bool __state)")
    need(finalizer, "LogHurtFaultLimited(__exception);", "[hurt] Hurt 的 Finalizer 必须留原始栈（重抛会截断栈）")
    need(finalizer, "return __exception;", "[hurt] 异常照常抛出，不吞")

    # ---- 3. 存活对账：死亡事件丢失也不能残留「还剩一个敌人」----
    tick = body("control", "public bool Tick(float deltaTime, ModeHBattleSnapshotContext snapshotContext)")
    ordered(tick, ["_telemetry.SweepDepartedParticipants(deltaTime);", "TryClaimVictoryIfCleared()",
                   "if (_telemetry.Tick(deltaTime)) return true;"],
            "[alive] 对账与胜利判定必须先于 180 秒超时")
    sweep = body("telemetry", "public void SweepDepartedParticipants(float deltaTime)")
    need(sweep, "OnParticipantDead(enemy, null);", "[alive] 已不在场的敌军必须走原有死亡出列（与迟到事件去重）")
    need(sweep, "OnParticipantDead(fighter, null);", "[alive] 已不在场的登场选手必须补倒地事实")
    departure = body("telemetry", "private static string ResolveDeparture(ModeHParticipantRef participant, float deltaTime)")
    need(departure, "if (health.IsDead) return", "[alive] 已判死（死亡事件被别的监听器打断）必须出列")
    need(departure, "DepartedInactiveGraceSeconds", "[alive] 失活按宽限计时，不能一失活就出列")

    # ---- 4. 预选配装兼容其它 Mod 的枪：模板弹匣判据把关，实例弹匣不可用降级而不重开 ----
    prepared = body("prepared", "internal static List<ModeHResolvedKit> BuildPreparedKits(long seed, string identity)")
    forbid(prepared, "TryGetDynamicEntry", "[kit] owner 要求兼容其它 Mod 的物品，预选配装不得按动态物品整体排除")
    need(prepared, "!ModeHLoadoutKitApplicator.HasMagazine(item, gun)) continue;", "[kit] 枪械槽必须有弹匣容器")
    has_magazine = body("applicator", "internal static bool HasMagazine(Item weapon, ItemSetting_Gun gun)")
    need(has_magazine, "weapon.Inventory != null", "[kit] 弹匣判据与装配器一致（容器）")
    need(has_magazine, "gun.Capacity > 0", "[kit] 弹匣判据与装配器一致（容量）")
    ammo = body("applicator", "private static bool TryApplyAmmo(")
    start = squeeze(ammo).find("gun.Capacity <= 0")
    fallback = squeeze(ammo)[start:squeeze(ammo).find("GunBulletCountCacheField == null")] if start >= 0 else ""
    if "kit_apply_magazine_missing" in fallback or "TryStoreAmmo(characterItem.Inventory, ammoTypeId, kit.Spec.AmmoCount" not in fallback:
        errors.append("[kit] 实例弹匣不可用必须降级为背包弹药，不得判 kit_apply_magazine_missing 重开")

    # ---- 5. 恢复壳不进玩家路径，且恒能关 ----
    route = body("ui_flow", "private void RouteUiForLifecycle(ModeHLifecycle lifecycle)")
    recovery_case = squeeze(route)
    start = recovery_case.find("case ModeHLifecycle.Recovering:")
    end = recovery_case.find("break;", start)
    segment = recovery_case[start:end] if start >= 0 and end > start else ""
    if "RouteRecoveryLifecycle(lifecycle);" not in segment or "OpenRecoveryShell(" in segment:
        errors.append("[recovery] 恢复通道三个相位不得弹恢复壳（技术重试静默）")
    update_internal = body("match", "partial void OnUpdateInternal(float deltaTime, float unscaledDeltaTime)")
    need(update_internal, "if (TryDriveSuspendedExit()) return;", "[recovery] 场内挂起必须送回基地而不是停在恢复壳")
    exit_drive = body("recovery", "private bool TryDriveSuspendedExit()")
    need(exit_drive, "RequestExit(ModeHExitReason.UserMapReturn, SuspendedExitReasonId);", "[recovery] 挂起离场走统一关停")
    shutdown = body("module", "internal void ShutdownRuntime(ModeHExitReason reason, string reasonId)")
    need(shutdown, "string.Equals(reasonId, SuspendedExitReasonId, StringComparison.Ordinal)",
         "[recovery] 挂起离场必须回基地，不能把玩家留在已解除隔离的出击图")
    entry = body("entry", "private static bool TryOpenRecoveryShellForEntry(ModBehaviour host, string reasonId)")
    need(entry, "runtime.ContinueSeasonFromEntry(reasonId);", "[recovery] 入口有可续赛季时直接续赛")
    cont = body("recovery", "internal void ContinueSeasonFromEntry(string reasonId)")
    need(cont, "EnsureRunOwnerForRecovery();", "[recovery] 关停后内存 owner 为空时必须按存档重建")
    need(cont, "ModBehaviour.DevModeEnabled", "[recovery] 正式构建不先弹恢复壳")
    need(body("recovery", "private void PresentRecoveryFailure(string reasonId)"), "ModBehaviour.DevModeEnabled",
         "[recovery] 续赛失败在正式构建给玩家可读的提示 / 放弃确认，不弹调试面板")
    show = body("panel", "public void Show(")
    need(show, "RebuildActions(EnsureCloseAction(actions), allowActions);", "[recovery] 恢复壳恒有「稍后处理」")
    lines = body("panel", "public static List<string> BuildLines(")
    need(lines, "!string.IsNullOrEmpty(technicalReasonId) && ModBehaviour.DevModeEnabled",
         "[recovery] 「技术中止」一组是开发诊断，只在 Dev 构建显示")

    # ---- 6. 拍铃给所有人用 ----
    bell = body("command", "public bool TryRingBell(")
    forbid(bell, "command_signature_owner_absent", "[bell] 拍铃不得按招牌持有者在场拒绝")

    # ---- 7. 押钱在战报耐久落盘后才结；下注前先对账 ----
    settle = body("combat_flow", "private void BeginMatchSettlement()")
    ordered(settle, ["UpsertMatchReport(report);", 'if (!TryPersistSeason("match_settling", true))',
                     "SettleCashBetForMatch(won);", "ReleaseCombatRuntimeObjects(); if (TryTransition("],
            "[bet] 押钱必须排在战报与奖励 operation 耐久落盘之后（否则回落重打会同一场结两次）")
    need(settle, "_lastSettlementReport = null;", "[bet] 上一场的战报引用不能带进本场结算的异常判断")
    reserve = body("bet", "private void ReserveStandingCashBet()")
    ordered(reserve, ["ReconcileCashBetOnRestore();", "ModeHCashBetRecord carried = CarriedBetForCurrentMatch();"],
            "[bet] 下注前先按已有战报补结上一场挂着的押注")
    return errors


PROBES = [
    ("module", "                HandleUpdateFailure(e);", '                RequestExit(ModeHExitReason.TechnicalAbort, "update_exception");'),
    ("match", '                        RequestTechnicalRetry("update_exception:" + e.GetType().Name);', ""),
    ("rules", "                HurtAtEdge(participant.Health);", "                participant.Health.Hurt(_edgeDamage);"),
    ("hurt_patch", "            if (__exception != null) LogHurtFaultLimited(__exception);", ""),
    ("control", "            _telemetry.SweepDepartedParticipants(deltaTime);\n", ""),
    ("telemetry", "                if (health.IsDead) return \"判死\";", ""),
    ("telemetry", "                OnParticipantDead(enemy, null);", ""),
    ("prepared", "                    Item item = ItemAssetsCollection.GetPrefab(id);",
     "                    ItemAssetsCollection.DynamicEntry dynamicEntry;\n"
     "                    if (ItemAssetsCollection.TryGetDynamicEntry(id, out dynamicEntry)) continue;\n"
     "                    Item item = ItemAssetsCollection.GetPrefab(id);"),
    ("applicator", " && gun.Capacity > 0; }", "; }"),
    ("applicator", "                    string fallbackReason;\n",
     "                    failureReasonId = \"kit_apply_magazine_missing:\" + kit.Spec.KitId; return false;\n"
     "                    string fallbackReason;\n"),
    ("prepared", "                    if (gunSlot && !ModeHLoadoutKitApplicator.HasMagazine(item, gun)) continue;", ""),
    ("ui_flow", "                    RouteRecoveryLifecycle(lifecycle);", "                    OpenRecoveryShell(_lastExitReasonId);"),
    ("match", "            if (TryDriveSuspendedExit()) return;", ""),
    ("entry", "                runtime.ContinueSeasonFromEntry(reasonId);", "                runtime.OpenRecoveryShell(reasonId);"),
    ("recovery", "                EnsureRunOwnerForRecovery();", ""),
    ("panel", "            RebuildActions(EnsureCloseAction(actions), allowActions);", "            RebuildActions(actions, allowActions);"),
    ("panel", "            if (!string.IsNullOrEmpty(technicalReasonId) && ModBehaviour.DevModeEnabled)",
     "            if (!string.IsNullOrEmpty(technicalReasonId))"),
    ("command", '                failureReasonId = "command_requires_relay";', '                failureReasonId = "command_signature_owner_absent";'),
    ("bet", "            ReconcileCashBetOnRestore();\n            // 账本只留最新一条", "            // 账本只留最新一条"),
]


def main():
    sources = {}
    for key, rel in FILES.items():
        sources[key] = read(rel).replace("\r\n", "\n")
    errors = check(sources)

    # 押钱顺序单独做一条变异：把结算挪回落盘之前
    probes = list(PROBES)
    settle_old = "                SettleCashBetForMatch(won);\n"
    probes.append(("combat_flow", settle_old, ""))

    for key, before, after in probes:
        if sources[key].count(before) != 1:
            errors.append("[probe] 变异锚点不唯一或失效: %s: %r" % (FILES[key], before[:60]))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        if key == "combat_flow" and before == settle_old:
            mutated[key] = mutated[key].replace(
                "                UpsertMatchReport(report);\n",
                "                SettleCashBetForMatch(won);\n                UpsertMatchReport(report);\n", 1)
        if not check(mutated):
            errors.append("[probe] 变异没被抓住: %s: %r" % (FILES[key], before[:60]))

    if errors:
        print("ModeHRuntimeResilienceGuard: FAIL (%d errors)" % len(errors))
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeHRuntimeResilienceGuard: PASS（每帧异常分级、边界伤害隔离、存活对账先于超时、预选配装兼容其它 Mod 的枪、"
          "恢复壳不进玩家路径且恒能关、拍铃不看持有者、押钱在战报落盘后结；%d 条变异探针全部转红）" % len(probes))
    return 0


if __name__ == "__main__":
    sys.exit(main())
