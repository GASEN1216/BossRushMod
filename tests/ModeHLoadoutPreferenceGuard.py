"""
ModeHLoadoutPreferenceGuard — 鸭王杯「调整」页列出两名选手身上全部装备，玩家穿上的选择跨场 / 跨赛季沿用（2026-09-29 owner）。

owner 原话整理：「调整页应该放那两个人身上的所有装备：选人界面显示很多件，一到调整页只剩几件；
而且每次选择穿上后，下一把回来又全部显示成没穿上」。

根因与改法：
  - 配装分成「首发配装」「接力配装」两个页签，一页只有一个人；改成一个「配装」页签，左列首发、右列接力，
    每列 = AddKitOptions（基础全套逐槽 + 可换上的整备套装），√ 已带上的就是赛前对照页人物卡上的那一排；
  - 穿上的整备只记在本场阵容里，同场重开（技术重试 / 退游戏重进）把阵容清空、换赛季更是从空白开始；
    改为每次在调整页点选后按选手身份（stableKey）写进本槽独立账本 ModeHKitPreferenceLedger，
    新建阵容时（BuildDefaultKitSelection）没有上一场可沿用就读它，只取本季已解锁、可用、兼容、同槽一件的。
只证明接线；过滤规则的执行由 ModeHPreparedEquipment / ModeHMarketAudit 回归覆盖，页面排版要实机看。
每条断言都有内置变异探针，探针必须让 check() 转红，否则本守卫自己判失败。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from cs_source_util import clean_source  # noqa: E402
from ModeHOneClickFlowGuard import method_body  # noqa: E402

FILES = {
    "editing": "ModeH/ModeHRuntimeModule_LoadoutEditing.cs",
    "profiles": "ModeH/ModeHRuntimeModule_CombatProfiles.cs",
    "module": "ModeH/ModeHRuntimeModule.cs",
    "ledger": "ModeH/ModeHKitPreferenceLedger.cs",
}


def read(rel):
    with open(os.path.join(REPO_ROOT, rel), "r", encoding="utf-8-sig") as handle:
        return handle.read().replace("\r\n", "\n")


def squeeze(text):
    return re.sub(r"\s+", " ", text or "")


def check(sources):
    errors = []
    src = dict((key, clean_source(value)) for key, value in sources.items())

    def body(key, signature):
        text = method_body(src[key], signature)
        if text is None:
            errors.append("[%s] 找不到唯一的方法 %s" % (FILES[key], signature))
            return ""
        return squeeze(text)

    def need(text, token, why):
        if token not in text:
            errors.append(why + "（缺少 %r）" % token)

    def ordered(text, tokens, why):
        positions = [text.find(t) for t in tokens]
        if -1 in positions or positions != sorted(positions):
            errors.append(why + "（顺序 %r）" % (positions,))

    editor = body("editing", "private ModeHPageContent BuildLoadoutEditorPage()")
    need(editor, "else if (_loadoutSection == 2) AddGearOptions(page, roster);", "配装页签一页放两名选手")
    if editor.count("AddSectionTab(tabs, roster,") != 3:
        errors.append("整备页只有三个页签：阵容 / 配装 / 口令（不再分首发配装、接力配装两页）")
    gear = body("editing", "private void AddGearOptions(ModeHPageContent page, ModeHMatchRosterDto roster)")
    ordered(gear, ["AddKitOptions(left, starter, roster.starterKitIds);",
                   "if (relay != null) AddKitOptions(right, relay, roster.relayKitIds);",
                   "page.PreparationColumns = 2;"],
            "左列首发、右列接力，每列都是这名选手的全部装备")

    option = body("editing", "private ModeHActionData MakePreparationOption(")
    ordered(option, ["edit();", "RecordKitPreferences(owner);", 'TryPersistSeason("loadout_edited");'],
            "调整页每次点选都把两名选手现在穿上的记进选手配装账本")
    record = body("editing", "private void RecordKitPreferences(ModeHMatchRosterDto roster)")
    need(record, "ModeHKitPreferenceLedger.Record(starter.stableKey, roster.starterKitIds);", "按首发身份记")
    need(record, "ModeHKitPreferenceLedger.Record(relay.stableKey, roster.relayKitIds);", "按接力身份记")
    load = body("editing", "private List<string> LoadKitPreference(ModeHProfileDto profile)")
    ordered(load, ["ModeHKitPreferenceLedger.Find(profile.stableKey)", "ModeHLoadoutKitRegistry.GetSelectableKits(",
                   "_season.unlockedKitIds", "if (!slots.Add(kit.Spec.ReplaceSlot)) continue;"],
            "沿用时只取本季已解锁、可选的整备，同槽只留一件")
    need(load, "if (result.Count >= ModeHConfig.MaxKitsPerFighter) break;", "沿用不得超过每人整备上限")

    defaults = body("profiles", "private List<string> BuildDefaultKitSelection(ModeHProfileDto profile)")
    ordered(defaults, ["if (selected != null) return new List<string>(selected);", "return LoadKitPreference(profile);"],
            "新建阵容先沿用上一场，没有才读选手配装账本（同场重开清空阵容、换赛季时生效）")
    need(body("module", "internal static void ResetModeHStaticCaches()"), "ModeHKitPreferenceLedger.ResetStaticCaches();",
         "Mod 销毁时账本退订存档事件（§4.6）")
    need(src["ledger"], 'internal const string StorageKey = "BossRush_ModeHKitPreference_v1";',
         "选手配装账本是本槽独立 key（赛季 DTO 进摘要，不能加字段）")
    need(src["ledger"], "new BossRushSlotJsonStore<Data>(", "账本走共享 BossRushSlotJsonStore（写屏障 / 回读核对 / 换槽复位）")
    return errors


PROBES = [
    ("editing", "            else if (_loadoutSection == 2) AddGearOptions(page, roster);\n", ""),
    ("editing", "            if (relay != null) AddKitOptions(right, relay, roster.relayKitIds);\n", ""),
    ("editing", "                    RecordKitPreferences(owner);\n", ""),
    ("editing", "                if (!slots.Add(kit.Spec.ReplaceSlot)) continue;\n", ""),
    ("profiles", "            return LoadKitPreference(profile);", "            return new List<string>();"),
    ("module", "            ModeHKitPreferenceLedger.ResetStaticCaches();\n", ""),
]


def main():
    sources = dict((key, read(rel)) for key, rel in FILES.items())
    errors = check(sources)
    for key, before, after in PROBES:
        if sources[key].count(before) != 1:
            errors.append("[probe] 变异锚点不唯一或失效: %s: %r" % (FILES[key], before[:70]))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        if not check(mutated):
            errors.append("[probe] 变异没被抓住: %s: %r" % (FILES[key], before[:70]))
    if errors:
        print("ModeHLoadoutPreferenceGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeHLoadoutPreferenceGuard: PASS (%d probes)" % len(PROBES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
