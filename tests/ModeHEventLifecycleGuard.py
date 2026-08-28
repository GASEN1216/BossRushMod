#!/usr/bin/env python3
"""
ModeHEventLifecycleGuard — Mode H 事件生命周期守卫（设计提案 §19.5、§26.1）。

不变式：
- 官方 Health.OnHurt / OnDead 是 static event，只在唯一 owner 上以**命名 handler**
  订阅一次；禁止匿名 lambda、禁止按每个选手对象重复订阅；
- 订阅与退订对称，且退订入口幂等，取消/切图/shutdown/Mod 销毁共用同一入口；
- 订阅失败必须回滚到未订阅状态（不留半订阅）；
- 所有适配器先过 runId + sceneGeneration + matchIndex 三比对，旧场事件丢弃；
- Health -> participant 是 O(1) 字典路由，不做每事件全表扫描；
- diagnostic 注册表查询**优先于**战斗 participant 注册表；命中只交认证，
  绝不进入战斗遥测、结果 CAS、伤病/战痕或奖励路径；
- 认证的诊断接收端在每条退出路径（正常结束 / Cancel）都解绑，正式开战期间为空；
- 死亡帧顺序为 OnDeadEvent -> OnDead -> SetActive(false) -> OnHurtEvent -> OnHurt，
  因此 router 不得读取 activeInHierarchy；
- 同次伤害多来源用 event token 去重；
- 不调用 MutatorManager.RollAndApply，不写共享 MutatorContext，不复用全局 callback list；
- ModeHFighterDownToken 每 participantId + matchIndex 至多一次；
- ModeHBattleResultToken 由 Interlocked CAS 保证唯一；
- 终局优先级与 180 秒规则、逃跑型胆怯立即判负、实际登场才消耗休息。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
ROUTER = os.path.join(MODEH_DIR, "ModeHEventRouter.cs")
TELEMETRY = os.path.join(MODEH_DIR, "ModeHCombatTelemetry.cs")
CERTIFICATION = os.path.join(MODEH_DIR, "ModeHProductionCertification.cs")

FORBIDDEN_SHARED = [
    "MutatorManager.RollAndApply",
    "MutatorContext",
    "EnemyKilledCallbacks",
]


def check_router(errors):
    source = read_text(ROUTER)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHEventRouter.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static bool Bind\(long ownerToken, IModeHTelemetrySink sink, out string failureReasonId\)",
         "唯一 owner 订阅入口"),
        (r"public static void Unbind\(\)", "幂等退订入口"),
        (r"Health\.OnHurt \+= _onHurtHandler;", "命名 handler 订阅 OnHurt"),
        (r"Health\.OnDead \+= _onDeadHandler;", "命名 handler 订阅 OnDead"),
        (r"Health\.OnHurt -= _onHurtHandler;", "对称退订 OnHurt"),
        (r"Health\.OnDead -= _onDeadHandler;", "对称退订 OnDead"),
        (r"_onHurtHandler = HandleHealthHurt;", "handler 是命名方法而不是匿名委托"),
        (r"_onDeadHandler = HandleHealthDead;", "handler 是命名方法而不是匿名委托"),
        (r'failureReasonId = "router_owner_conflict";', "换 owner 前必须先 Unbind"),
        (r"public static void SetContext\(ModeHRunState runState, int sceneGeneration, int matchIndex\)",
         "三比对上下文入口"),
        (r"private static bool IsContextValid\(\)", "统一事件门控"),
        (r"_runState\.IsCallbackValid\(_ownerToken, _runState\.RunId, _sceneGeneration\)",
         "owner token + runId + sceneGeneration 比对"),
        (r"return _runState\.MatchIndex == _matchIndex;", "matchIndex 比对"),
        (r"private static readonly Dictionary<int, ModeHParticipantRef> _participants",
         "Health -> participant 为 O(1) 字典"),
        (r"private static readonly Dictionary<int, string> _diagnostics",
         "diagnostic 注册表为 O(1) 字典"),
        (r"public static void ClearDiagnostics\(\)", "认证结束清空诊断注册表"),
        (r"private static bool TryConsumeToken\(", "event token 去重"),
        (r"public static void ResetStaticCaches\(\)", "静态缓存复位"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Router] 不满足: " + desc)

    # 订阅失败必须回滚
    bind = re.search(r"public static bool Bind\([\s\S]*?\n        \}", code)
    if bind and "TryUnsubscribe();" not in bind.group(0):
        errors.append("[Router] 订阅失败必须回滚到未订阅状态")

    # 诊断注册表必须先于战斗注册表查询
    for handler in ["HandleHealthHurt", "HandleHealthDead"]:
        body = re.search(
            r"private static void {}\([\s\S]*?\n        \}}".format(handler), code)
        if not body:
            errors.append("[Router] 缺少命名 handler: " + handler)
            continue
        text = body.group(0)
        diag = text.find("_diagnostics.TryGetValue")
        part = text.find("_participants.TryGetValue")
        if diag < 0 or part < 0:
            errors.append("[Router] {} 缺少两级注册表查询".format(handler))
        elif diag > part:
            errors.append("[Router] {} 的 diagnostic 查询必须优先于战斗 participant".format(handler))
        if "IsContextValid()" not in text:
            errors.append("[Router] {} 未做上下文门控".format(handler))

    # 死亡帧上 GameObject 已 inactive，禁止读 activeInHierarchy
    if contains_symbol(code, "activeInHierarchy"):
        errors.append("[Router] 死亡帧上目标已 inactive，不得依赖 activeInHierarchy")

    for forbidden in FORBIDDEN_SHARED:
        if contains_symbol(code, forbidden):
            errors.append("[Router] 不得复用共享 mutator 设施: " + forbidden)

    # 匿名订阅禁令
    if re.search(r"Health\.On(Hurt|Dead)\s*\+=\s*(\(|delegate)", code):
        errors.append("[Router] 禁止用匿名委托订阅 static 事件（无法对称退订）")


def check_telemetry(errors):
    source = read_text(TELEMETRY)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHCombatTelemetry.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"Interlocked\.CompareExchange\(ref _resultClaimed, 1, 0\) != 0", "结果 token 由 CAS 唯一"),
        (r"private string BuildDownToken\(string profileId\)", "倒地 token 构造"),
        (r'return "down\|" \+ _matchIndex \+ "\|"', "倒地 token 含 matchIndex + participantId"),
        (r"if \(!_downTokens\.Add\(downToken\)\) return;", "倒地 token 每场每人至多一次"),
        (r"public bool TryClaimVictory\(bool anyFighterAlive\)", "终局优先级 1"),
        (r"public bool TryClaimDefeatByDown\(\)", "终局优先级 2"),
        (r"public bool TryClaimDefeatByCowardice\(string cowardiceType\)", "终局优先级 5"),
        (r"_elapsedSeconds < ModeHConfig\.MatchDurationSeconds", "180 秒规则引用冻结常量"),
        (r"public bool HasRested\(string profileId\)", "休息判定"),
        (r"_enteredProfileIds\.Add\(fighter\.ProfileId\);", "实际登场才写入登场集合"),
        (r"public void FinalizeSpecialKill\(int lockedOdds, string survivingProfileId\)",
         "specialKillTag 结算"),
        (r"ModeHStableIds\.SpecialKillHighThreatCore", "core 标签"),
        (r"ModeHStableIds\.SpecialKillRelayFinisher", "relay 标签"),
        (r"ModeHStableIds\.SpecialKillLastStand", "last stand 标签"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Telemetry] 不满足: " + desc)

    # 180 秒必须判负而不是判胜
    tick = re.search(r"public bool Tick\(float deltaTime\)[\s\S]*?\n        \}", code)
    if tick and "PlayerDefeat" not in tick.group(0):
        errors.append("[Telemetry] 180 秒到时必须判玩家失败")
    if tick and ("SetHealth" in tick.group(0) or "Damage" in tick.group(0)):
        errors.append("[Telemetry] 180 秒到时不得自动补伤害或伪造击倒")

    # specialKillTag 优先级：core -> relay -> last stand -> x3+
    finalize = re.search(
        r"public void FinalizeSpecialKill\([\s\S]*?\n        \}", code)
    if finalize:
        body = finalize.group(0)
        order = [
            body.find("SpecialKillHighThreatCore"),
            body.find("SpecialKillRelayFinisher"),
            body.find("SpecialKillLastStand"),
            body.find("ScarOfferMinOdds"),
        ]
        if any(i < 0 for i in order) or order != sorted(order):
            errors.append("[Telemetry] specialKillTag 优先级必须是 core -> relay -> last stand -> x3+")

    # 遥测不得写存档或发奖励
    for forbidden in ["SavesSystem", "ModeHSaveFlushCoordinator", "ModeHSeasonRewardService",
                      "Inventory", "PlayerStorage"]:
        if contains_symbol(code, forbidden):
            errors.append("[Telemetry] 遥测只采集，不得引用: " + forbidden)


def check_certification_sink(errors):
    source = read_text(CERTIFICATION)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHProductionCertification.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"internal static void BindDiagnosticSink\(ModeHProductionCertification instance\)",
         "诊断接收端绑定"),
        (r"internal static void UnbindDiagnosticSink\(\)", "诊断接收端解绑"),
        (r"internal static void NotifyDiagnosticHurt\(string stableKey, float damageValue\)",
         "诊断受伤入口"),
        (r"internal static void NotifyDiagnosticDead\(string stableKey\)", "诊断死亡入口"),
        (r"internal static bool IsDiagnosticRegistryEmpty", "正式开战前注册表为空断言入口"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Certification] 不满足: " + desc)

    # 每条退出路径都要解绑
    cancel = re.search(r"internal void Cancel\(\)[\s\S]*?\n        \}", code)
    if cancel and "UnbindDiagnosticSink();" not in cancel.group(0):
        errors.append("[Certification] Cancel 必须解绑诊断接收端")
    if code.count("UnbindDiagnosticSink();") < 2:
        errors.append("[Certification] 正常结束与取消两条路径都必须解绑诊断接收端")

    # 诊断事件不得生成任何 Season 事实
    for name in ["NotifyDiagnosticHurt", "NotifyDiagnosticDead"]:
        body = re.search(
            r"internal static void {}\([\s\S]*?\n        \}}".format(name), code)
        if not body:
            continue
        text = body.group(0)
        for forbidden in ["FighterDown", "ModeHMatchReportDto", "Reward", "Injury", "Scar"]:
            if forbidden in text:
                errors.append(
                    "[Certification] {} 不得生成 Season 事实: {}".format(name, forbidden))


def main():
    errors = []
    check_router(errors)
    check_telemetry(errors)
    check_certification_sink(errors)

    if errors:
        print("ModeHEventLifecycleGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHEventLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
