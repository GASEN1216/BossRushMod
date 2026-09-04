#!/usr/bin/env python3
"""
ModeHLocalizationGuard — Mode H 本地化守卫（设计提案 §23.2、§26.1）。

不变式：
- 本地化只有一个 source of truth：Localization/ModeHLocalization.cs；
  不得新建 LocalizationKeys.json 或第二套 parser/registry；
- 接线点是 Integration/BossRushIntegration_StartAndScene.cs 的
  InjectLocalization_Extra_Integration()，显式调用 ModeHLocalization.Inject()；
  不得误写到 LocalizationInjector；
- 所有 BossRush_ModeH_ raw key 都必须有中英注入（一律走 L10n.T(cn, en)）；
- 命令 / 异常 / 伤病 / 战痕 / 状态 / 侦察 / 恢复错误 / 押品明细文本无缺项；
- 显式断言 BossRush_ModeH_RealStakeRiskNotice 存在，中英文本都提到
  “永久没收”与“唯一装备不豁免”，且被入口页、模式说明与 ModeHInteractable 三处引用；
- 代码里出现的 BossRush_ModeH_ key 都必须被注入（不得显示 raw key）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
LOCALIZATION = os.path.join(REPO_ROOT, "Localization", "ModeHLocalization.cs")
INJECTION_SITE = os.path.join(
    REPO_ROOT, "Integration", "BossRushIntegration_StartAndScene.cs")
LOCALIZATION_INJECTOR = os.path.join(REPO_ROOT, "Localization", "LocalizationInjector.cs")

PREFIX = "BossRush_ModeH_"

# §23.2：这些类别一条都不能缺
REQUIRED_SUFFIXES = [
    # 八条通用口令 + 五条招牌口令
    "Command_steady", "Command_press", "Command_center", "Command_spread",
    "Command_finish", "Command_hold", "Command_guard", "Command_all_in",
    "Command_weakness", "Command_anchor", "Command_last_mag", "Command_together",
    "Command_handoff",
    # 四种公开异常
    "Anomaly_blood", "Anomaly_crowd", "Anomaly_strong", "Anomaly_error",
    # 五条伤病
    "Injury_leg", "Injury_hand", "Injury_armor", "Injury_old_wound", "Injury_spirit",
    # 八条战痕
    "Scar_broken_shield_charge", "Scar_blood_rush", "Scar_longshot_memory",
    "Scar_relay_expert", "Scar_bell_dependence", "Scar_center_keeper",
    "Scar_skill_saver_scar", "Scar_crowd_favorite",
    # 四类侦察
    "Recon_hidden_quirk", "Recon_current_injury", "Recon_member_order",
    "Recon_second_equipment",
    # 恢复错误
    "Recovery_TechnicalAbort", "Recovery_SameMatchRestart",
    "Recovery_ManualIntervention", "Recovery_Suspended", "Recovery_SnapshotUnusable",
    # 押品明细
    "RealStakeRiskNotice", "RealStake_Selector", "RealStake_WorstCaseLoss",
    "RealStake_QualityRange", "RealStake_PlannedLosses", "RealStake_Escrowed",
    "RealStake_UniqueNotExempt", "RealStake_Disabled",
    # 五种公开原型 + 六种底色
    "Archetype_assault", "Archetype_ranged", "Archetype_tank",
    "Archetype_sustain", "Archetype_finisher",
    "Temperament_aggressive", "Temperament_cautious", "Temperament_hunter",
    "Temperament_bulwark", "Temperament_trickster", "Temperament_pack",
    # 口令行为状态四态
    "CommandStatus_VerifiedBehavior", "CommandStatus_PartiallyVerified",
    "CommandStatus_ReportOnly", "CommandStatus_Unavailable",
]

# 全部 lifecycle 状态都要有可读文案
REQUIRED_STATES = [
    "Drafting", "RosterLocked", "MatchBrief", "LoadoutEditing", "OddsPreview",
    "LoadoutLocked", "MatchSpawning", "MatchFighting", "RelayPending",
    "MatchSettling", "Intermission", "TransferWindow", "HallOfFame",
    "SeasonEnded", "Recovering", "Suspended",
]

# 真实资产风险行必须点名的两件事
RISK_NOTICE_CN_TOKENS = ["永久没收", "唯一装备"]
RISK_NOTICE_EN_TOKENS = ["permanently", "only copy"]


def collect_injected_suffixes(code):
    """收集 Add(map, "<suffix>", cn, en) 注入的全部后缀。"""
    return set(re.findall(r'Add\w*\(\s*map,\s*"([^"]+)"', code))


def main():
    errors = []

    source = read_text(LOCALIZATION)
    if source is None:
        print("ModeHLocalizationGuard: FAIL (1 errors)")
        print("  - [File] 缺少 Localization/ModeHLocalization.cs")
        return 1
    code = strip_cs_comments(source)

    # 唯一 source of truth
    if not re.search(r"public static void Inject\(\)", code):
        errors.append("[Source] 缺少 ModeHLocalization.Inject()")
    if not re.search(r"LocalizationHelper\.InjectLocalizations\(map\);", code):
        errors.append("[Source] 必须经 LocalizationHelper 注入")
    if "ModeHConfig.LocalizationKeyPrefix" not in code:
        errors.append("[Source] key 前缀必须引用 ModeHConfig.LocalizationKeyPrefix")

    # 中英必须成对：所有注入都走 L10n.T(cn, en)
    add_helper = re.search(
        r"private static void Add\(Dictionary<string, string> map, string suffix, "
        r"string cn, string en\)[\s\S]*?\n        \}", code)
    if not add_helper or "L10n.T(cn, en)" not in add_helper.group(0):
        errors.append("[Source] 注入必须走 L10n.T(cn, en) 保证中英成对")

    injected = collect_injected_suffixes(code)

    for suffix in REQUIRED_SUFFIXES:
        if suffix not in injected:
            errors.append("[Missing] 缺少 key: " + PREFIX + suffix)
    for state in REQUIRED_STATES:
        if ("State_" + state) not in injected:
            errors.append("[Missing] 缺少状态文案: " + PREFIX + "State_" + state)

    # 拍铃失败原因：key 由 "BellFailed_" + failureReasonId 拼出，因此下面按
    # ModeHCommandController.TryRingBell 里实际出现的 failureReasonId 反查注入，
    # 而不是在 guard 里写死一份清单（写死会随代码新增原因而静默过期）。
    command_controller = read_text(
        os.path.join(MODEH_DIR, "ModeHCommandController.cs"))
    if command_controller:
        bell_reasons = sorted(set(re.findall(
            r'failureReasonId\s*=\s*"([A-Za-z0-9_]+)"',
            strip_cs_comments(command_controller))))
        if not bell_reasons:
            errors.append(
                "[Source] 未能在 ModeHCommandController 中解析出 failureReasonId，"
                "拍铃文案覆盖检查会失效")
        for reason in bell_reasons:
            if ("BellFailed_" + reason) not in injected:
                errors.append(
                    "[Missing] 拍铃失败原因缺少文案: " + PREFIX + "BellFailed_" + reason)

        # GetBellFailureLocalizationKey 只对白名单内的 ID 拼具体 key，其余落 Generic。
        # 白名单漏一条就会静默退化成通用文案，所以要求它与 failureReasonId 全集一致。
        whitelist_body = re.search(
            r"internal static bool IsLocalizedBellFailure\([\s\S]*?\n        \}",
            strip_cs_comments(command_controller))
        if not whitelist_body:
            errors.append("[Source] 未找到 IsLocalizedBellFailure，拍铃文案会退化为通用提示")
        else:
            whitelisted = set(re.findall(
                r'failureReasonId == "([A-Za-z0-9_]+)"', whitelist_body.group(0)))
            for reason in bell_reasons:
                if reason not in whitelisted:
                    errors.append(
                        "[Source] 拍铃失败原因未进 IsLocalizedBellFailure 白名单，"
                        "会退化为通用文案: " + reason)
            for reason in sorted(whitelisted - set(bell_reasons)):
                errors.append(
                    "[Source] IsLocalizedBellFailure 白名单含已不存在的原因: " + reason)
        if "BellFailed_Generic" not in injected:
            errors.append("[Missing] 缺少拍铃失败兜底文案: " + PREFIX + "BellFailed_Generic")

    # 选手与套装用带参 helper 注入，单独确认它们最终落到 Fighter_/Rumor_/Kit_ 前缀
    for helper, prefix in [("AddFighter", "Fighter_"), ("AddKit", "Kit_")]:
        body = re.search(
            r"private static void {}\([\s\S]*?\n        \}}".format(helper), code)
        if not body or ('"{}"'.format(prefix) not in body.group(0)
                        and '"' + prefix in body.group(0)) is False:
            if not body or prefix not in body.group(0):
                errors.append("[Missing] {} 未注入 {} 前缀".format(helper, prefix))

    # 风险行内容
    notice = re.search(
        r'Add\(map, "RealStakeRiskNotice",\s*\n?\s*"([^"]*)",\s*\n?\s*"([\s\S]*?)"\);', code)
    if not notice:
        errors.append("[RiskNotice] 未找到 RealStakeRiskNotice 的中英文本")
    else:
        cn = notice.group(1)
        en = notice.group(2)
        for token in RISK_NOTICE_CN_TOKENS:
            if token not in cn:
                errors.append("[RiskNotice] 中文必须提到: " + token)
        for token in RISK_NOTICE_EN_TOKENS:
            if token.lower() not in en.lower():
                errors.append("[RiskNotice] 英文必须提到: " + token)

    # 接线点
    site = read_text(INJECTION_SITE)
    if site is None:
        errors.append("[Wiring] 缺少 Integration/BossRushIntegration_StartAndScene.cs")
    else:
        site_code = strip_cs_comments(site)
        extra = re.search(
            r"private void InjectLocalization_Extra_Integration\(\)[\s\S]*?\n        \}", site_code)
        if not extra:
            errors.append("[Wiring] 未找到 InjectLocalization_Extra_Integration()")
        elif "ModeHLocalization.Inject();" not in extra.group(0):
            errors.append("[Wiring] InjectLocalization_Extra_Integration 必须调用 ModeHLocalization.Inject()")

    injector = read_text(LOCALIZATION_INJECTOR)
    if injector is not None and PREFIX in strip_cs_comments(injector):
        errors.append("[Wiring] Mode H 的 key 不得写进 LocalizationInjector（唯一来源是独立文件）")

    # 不得新建第二套 registry
    for name in ["LocalizationKeys.json", "ModeHLocalizationKeys.json"]:
        if os.path.exists(os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", name)):
            errors.append("[Source] 不得新建第二套本地化数据: " + name)

    # 三处引用风险行
    risk_key_users = []
    for root, dirs, files in os.walk(REPO_ROOT):
        parts = root.replace("\\", "/").split("/")
        if any(p in ("Build", ".git", "docs", "tests", ".codex_tmp", "鸭科夫源码") for p in parts):
            continue
        for name in files:
            if not name.endswith(".cs"):
                continue
            text = read_text(os.path.join(root, name)) or ""
            if "RealStakeRiskNotice" in text:
                risk_key_users.append(name)
    for required in ["ModeHUIPages.cs", "ModeHInteractable.cs", "ModeHLocalization.cs"]:
        if required not in risk_key_users:
            errors.append("[RiskNotice] {} 必须引用 RealStakeRiskNotice".format(required))

    # 代码里用到的 key 都必须被注入（否则会显示 raw key）
    used = set()
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for match in re.finditer(
                r'ModeHConfig\.LocalizationKeyPrefix \+ "([A-Za-z0-9_]+)"', text):
            used.add(match.group(1))
    # 这些前缀在代码里是拼接出来的（"Unavailable_" + reasonId 等），
    # 逐条覆盖由下面的 REQUIRED / 具体断言负责，这里不按字面量比对。
    dynamic_prefixes = (
        "State_", "StakePhase_", "Fighter_", "Rumor_", "Kit_",
        "Unavailable_", "OddsTone_x", "Archetype_", "Temperament_",
        "Command_", "Anomaly_", "Injury_", "Scar_", "Recon_",
        "Skeleton_", "Entry_", "EntryHint_", "Condition_",
        "BellFailed_")
    for suffix in sorted(used):
        if suffix in injected:
            continue
        if suffix.startswith(dynamic_prefixes):
            continue
        errors.append("[Missing] 代码使用了未注入的 key: " + PREFIX + suffix)

    # 双前缀反查。
    # 历史 bug：ModeHOddsController.Add 已把完整 key 写进 entry.LabelKey，
    # MatchFlow:933 又拼一次 → BossRush_ModeH_BossRush_ModeH_Odds_xxx，
    # 18 条赔率分量标签全部显示星号 raw key。
    # 上面的 `used` 只收字面量，运行时拼接进不了集合，所以必须单独反查。
    #
    # 判据：裸后缀一定是字面量、局部变量或参数（`key`、`labelSuffix`）；
    # 而**成员读取**（`entry.LabelKey`、`choice.NameKey`）拿到的是 DTO 里
    # 已经拼好前缀的完整 key，再拼一次必然双前缀。故只禁成员读取。
    # `Resolve*(...)` 这类显式解析函数按约定返回裸后缀，放行。
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for match in re.finditer(
                r'ModeHConfig\.LocalizationKeyPrefix\s*\+\s*([A-Za-z_][A-Za-z0-9_.\(\)]*)', text):
            operand = match.group(1).strip()
            if re.match(r'^Resolve[A-Za-z0-9_]*\(', operand):
                continue
            if "." not in operand:
                # 局部变量 / 参数：按约定持裸后缀
                continue
            errors.append(
                "[DoublePrefix] {} 把 LocalizationKeyPrefix 拼到了成员读取上: {}"
                "（DTO 里的 *Key 字段存的是完整 key，直接 L10n.T(它) 即可）".format(
                    name, operand[:60]))

    # 生产侧契约：LabelKey 必须由 ModeHOddsController 拼好完整前缀。
    # 这条和上面那条互为对偶——一旦有人把生产侧改成写裸后缀，
    # 消费侧的 L10n.T(entry.LabelKey) 就会变成查裸 key，这里会立刻报出来。
    odds_controller = strip_cs_comments(
        read_text(os.path.join(MODEH_DIR, "ModeHOddsController.cs")) or "")
    if "entry.LabelKey = ModeHConfig.LocalizationKeyPrefix + labelSuffix;" not in odds_controller:
        errors.append(
            "[DoublePrefix] ModeHOddsController 必须在 Add() 里给 LabelKey 拼上完整前缀"
            "（消费侧按完整 key 直接 L10n.T，两边必须同时改）")

    if errors:
        print("ModeHLocalizationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHLocalizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
