"""
ModeHPrematchDeclutterGuard — 鸭王杯赛前各页不再挂警告 / 说明文字、地图预览与 BossRush 同源（2026-09-29 owner）。

owner 原话整理：「选人 UI 最上面的警告 / 提示文字全部删掉，包括『最终庄家赢……』；只保留必要的交互元素，
不要再自己往 UI 上加说明文字」；「鸭王杯地图选择器里的预览图要和 BossRush 相同」。

本守卫钉住：
  - 选人页不写页头说明（page.Body）；任何 Mode H 页都不再打开顶部 / 页脚风险横幅（ShowRealStakeNotice、CompactRiskNotice）；
  - 押注行与押物品页不再附「输了归庄家」一类说明；本场规则小字不再附「对面有狠角色」提醒；
  - 地图选择条目的预览图不按入口种类分叉：鸭王杯与 BossRush 走同一句 UpdateEntryThumbnailWithImage，不再清空横幅字段。
只证明代码里没有这些字 / 分叉，不证明画面：页面观感与预览图要实机看。
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
    "match": "ModeH/ModeHRuntimeModule_MatchFlow.cs",
    "match_pages": "ModeH/ModeHRuntimeModule_MatchPages.cs",
    "bet": "ModeH/ModeHRuntimeModule_BetFlow.cs",
    "editing": "ModeH/ModeHRuntimeModule_LoadoutEditing.cs",
    "map": "MapSelection/BossRushMapSelectionHelper.cs",
}
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
# 结构定义与渲染在这两个文件里（字段声明 / 读取），不算「某页打开了横幅」
RENDER_FILES = {"ModeHUIPages.cs"}


def read(path):
    with open(path, "r", encoding="utf-8-sig") as handle:
        return handle.read().replace("\r\n", "\n")


def check(sources, modeh_sources):
    errors = []
    src = dict((key, clean_source(value)) for key, value in sources.items())

    def body(key, signature):
        text = method_body(src[key], signature)
        if text is None:
            errors.append("[%s] 找不到唯一的方法 %s" % (FILES[key], signature))
            return ""
        return text

    draft = body("match", "private ModeHPageContent BuildDraftPageContent()")
    if "page.Body =" in draft:
        errors.append("选人页页头不放提示 / 说明文字（page.Body），该选首发还是接力由卡上按钮与角标说清")

    for name, text in sorted(modeh_sources.items()):
        if name in RENDER_FILES:
            continue
        cleaned = clean_source(text)
        for flag in ("ShowRealStakeNotice", "CompactRiskNotice"):
            if re.search(r"\b" + flag + r"\s*=(?!=)", cleaned):
                errors.append("ModeH/%s 打开了风险横幅 %s：赛前各页不再挂「庄家总是赢」一类警告" % (name, flag))

    row = body("bet", "private void AppendCashBetRow(ModeHPageContent page)")
    picker = body("bet", "private ModeHPageContent BuildItemBetPickerPage()")
    for label, text in (("押注行", row), ("押物品页", picker)):
        for word in ("归庄家", "the house", "赔率越冷门", "只管下一场"):
            if word in text:
                errors.append("%s 不再附输赢说明 / 免责式文字（出现 %r）" % (label, word))
    note = body("editing", "private string DescribeMatchNote()")
    if "hasHighThreatCore" in note:
        errors.append("本场规则小字只写规则本身，不再附「对面有狠角色」提醒")

    created = body("map", "private static void OnBossRushEntryCreated(")
    if "UpdateEntryThumbnailWithImage(uiEntry, mapConfig.previewImageName)" not in created:
        errors.append("地图条目必须按配置换上预览图（鸭王杯与 BossRush 同一句）")
    if "BossRushPendingEntryKind.ModeH" in created:
        errors.append("地图条目的预览图不得按入口种类分叉（鸭王杯要与 BossRush 相同）")
    if "ClearEntryFullScreenImage" in src["map"]:
        errors.append("不得再清空鸭王杯地图条目的预览横幅字段")
    return errors


PROBES = [
    ("match", "            // 2026-09-29 owner：选人页顶部不放任何提示 / 说明文字，只留卡片与按钮；\n",
     "            page.Body = L10n.T(\"先选一名首发，再选一名接力。\", \"Choose a starter, then a relay.\");\n"),
    ("match_pages", "            if (!page.RealStakeSelectorEnabled)\n            {\n                page.RealStakeDisabledReason",
     "            page.ShowRealStakeNotice = page.RealStakeSelectorEnabled;\n"
     "            if (!page.RealStakeSelectorEnabled)\n            {\n                page.RealStakeDisabledReason"),
    ("bet", "row.Caption = L10n.T(\"余额 \", \"Balance \")",
     "row.Caption = L10n.T(\"输了押金归庄家。余额 \", \"Balance \")"),
    ("editing", "            // 2026-09-29 owner：不再附「对面有狠角色」一类提醒，只留本场规则本身\n",
     "            if (summary.hasHighThreatCore) return null;\n"),
    ("map", "            if (!string.IsNullOrEmpty(mapConfig.previewImageName))\n            {\n                UpdateEntryThumbnailWithImage",
     "            if (pendingEntryKind != BossRushPendingEntryKind.ModeH && !string.IsNullOrEmpty(mapConfig.previewImageName))\n"
     "            {\n                UpdateEntryThumbnailWithImage"),
]


def main():
    sources = dict((key, read(os.path.join(REPO_ROOT, rel))) for key, rel in FILES.items())
    modeh = dict((name, read(os.path.join(MODEH_DIR, name)))
                 for name in os.listdir(MODEH_DIR) if name.endswith(".cs"))
    errors = check(sources, modeh)
    for key, before, after in PROBES:
        if sources[key].count(before) != 1:
            errors.append("[probe] 变异锚点不唯一或失效: %s: %r" % (FILES[key], before[:70]))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        mutated_modeh = dict(modeh)
        name = os.path.basename(FILES[key])
        if name in mutated_modeh:
            mutated_modeh[name] = mutated[key]
        if not check(mutated, mutated_modeh):
            errors.append("[probe] 变异没被抓住: %s: %r" % (FILES[key], before[:70]))
    if errors:
        print("ModeHPrematchDeclutterGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeHPrematchDeclutterGuard: PASS (%d probes)" % len(PROBES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
