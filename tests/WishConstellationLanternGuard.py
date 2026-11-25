#!/usr/bin/env python3
"""WishConstellationLanternGuard — 许愿台星宿祈向与星灯（2026-11-24 玩家许愿第 3 项）的接线不变式。

1. 最少字数 4，界面计数与提交校验同一口径：都数标准化后的有效字（字母、数字、汉字）。
2. 十二宿与许愿奖励的十二个类别一一对应；点亮的一宿按「写中该类别关键词」并进 matchedCategoryIds，
   抽奖前先确保奖池已建，再匹配、再并入祈向。祈向只在本地用，不进发送管线。
3. 星灯上墙前过 FilterLanternWishes：重跑联系方式 / 敏感词 / 字符多样性三道检查、压连续重复片段、
   有效字数下限、规范化去重；星灯层的 PrepareContents 必须经它。同屏最多 12 盏。
4. 「星灯」开关熄了以后不拉取、不预热：RefreshDanmakuDisplay 在发请求前、ScheduleDanmakuWarmup 在预热前都要先看开关。

文本守卫防不住「保留 token、杀掉执行路径」，行为正确性仍要靠实机清单（M_WISH_*）。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WISH = ("Integration", "WishFountain")


def read(name):
    path = os.path.join(ROOT, *(WISH + (name,)))
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
        return clean_source(fh.read())


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body_of(source, signature):
    if source is None:
        return None
    at = source.find(signature)
    if at < 0 or source.find(signature, at + 1) >= 0:
        return None
    brace = source.find("{", at)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def require(errors, ok, message):
    if not ok:
        errors.append(message)


def check_min_chars(errors):
    service = compact(read("WishFountainService.cs"))
    require(errors, "internalconstintMIN_CHARS=4;" in service, "[MinChars] MIN_CHARS 必须是 4（owner 拍板：心意长短不计较）")

    validation = read("WishFountainConfigAndValidation.cs")
    validate = compact(body_of(validation, "public static bool ValidateWishText("))
    require(errors, "inteffectiveChars=CountEffectiveWishChars(text);if(effectiveChars<MIN_CHARS)" in validate,
            "[MinChars] ValidateWishText 必须按有效字数对照 MIN_CHARS")
    require(errors, "text.Length<MIN_CHARS" not in validate and "effectiveChars<5" not in validate,
            "[MinChars] ValidateWishText 又出现了按原始长度或写死 5 的下限，界面计数会和提交对不上")
    count = compact(body_of(validation, "internal static int CountWishChars(string raw)"))
    require(errors, "returnCountEffectiveWishChars(StandardizeText(raw));" in count,
            "[MinChars] CountWishChars 必须先标准化再数有效字（与提交校验同一口径）")

    ui = read("WishFountainUI.cs")
    state = compact(body_of(ui, "private void RefreshUIState()"))
    require(errors, "intcharCount=WishFountainService.CountWishChars(inputField.text);" in state,
            "[MinChars] 界面计数必须用 WishFountainService.CountWishChars，不能再数 inputField.text.Length")
    require(errors, "SetConstellationsInteractable(!sending&&!successDisplayed);" in state,
            "[Constellation] 发送中 / 已送出时星宿必须锁住")


def constellation_ids(source):
    return re.findall(r'Make\("([a-z_]+)"', source or "")


def category_ids(source):
    block = source[source.find("WishRewardCategories ="):] if source else ""
    block = block[:block.find("};")]
    return re.findall(r'CreateWishRewardCategory\(\s*"([a-z_]+)"', block)


def check_constellations(errors):
    data = read("WishFountainConstellations.cs")
    service = read("WishFountainService.cs")
    ids = constellation_ids(data)
    cats = category_ids(service)
    require(errors, len(ids) == 12 and len(set(ids)) == 12, "[Constellation] 星宿必须正好十二宿且不重复：%s" % ids)
    require(errors, set(ids) == set(cats),
            "[Constellation] 十二宿与许愿奖励类别不一致：多出 %s，缺少 %s" % (sorted(set(ids) - set(cats)), sorted(set(cats) - set(ids))))

    add = compact(body_of(data, "private static void AddChosenWishCategory("))
    require(errors, "WishRewardCategories" in add and "match.matchedCategoryIds.Add(category.categoryId);" in add,
            "[Constellation] 祈向必须先对照奖励类别表，再按关键词命中并进 matchedCategoryIds")

    selection = read("WishFountainRewardSelection.cs")
    start = compact(body_of(selection, "internal static void TryStartWishRewardAnimationAfterSuccessfulSend("))
    ensure_at = start.find("EnsureWishRewardPoolInitialized();")
    match_at = start.find("WishRewardMatchResultmatch=MatchWishRewardKeywords(wishText);")
    add_at = start.find("AddChosenWishCategory(match,chosenCategoryId);")
    roll_at = start.find("RollWishRewardTypeIdCore(")
    require(errors, 0 <= ensure_at < match_at < add_at < roll_at,
            "[Constellation] 抽奖必须按 建奖池 → 关键词匹配 → 并入祈向 → 抽取 的顺序")

    ui = compact(read("WishFountainUI.cs"))
    require(errors, "TryStartWishRewardAnimationAfterSuccessfulSend(standardized,chosenConstellationId);" in ui,
            "[Constellation] 关面板开抽时必须带上点亮的那一宿")
    open_body = compact(body_of(read("WishFountainUI.cs"), "public void ResetAndOpen()"))
    release_at = open_body.find("ReleaseSuccessBeatOnClose();")
    reset_at = open_body.find("ResetConstellations();")
    require(errors, 0 <= release_at < reset_at,
            "[Constellation] 重开面板必须先把上一次的抽奖发出去，再熄掉点亮的一宿")

    send = read("WishFountainSendPipeline.cs") or ""
    require(errors, not re.search(r"Constellation|chosenCategory", send),
            "[Constellation] 祈向只在本地用，不能进发送管线（飞书表结构不变）")


def check_lanterns(errors):
    flt = read("WishFountainLanternFilter.cs")
    clean = compact(body_of(flt, "private static string CleanLanternWish(string raw)"))
    for token, why in (("CheckNoContactInfo(", "联系方式"), ("CheckNoProfanity(", "敏感词"), ("CheckCharDiversity(", "字符多样性"),
                       ("LanternRepeatRegex.Replace(oneLine,\"$1$1\")", "连续重复片段压到两份"),
                       ("CountEffectiveWishChars(oneLine)<MIN_CHARS", "有效字数下限")):
        require(errors, token in clean, "[Lantern] CleanLanternWish 缺少%s" % why)
    require(errors, compact(r'new Regex(@"(.{1,6}?)\1{2,}"') in compact(flt or ""),
            "[Lantern] 重复片段的正则应为 (.{1,6}?)\\1{2,}（三份以上才压，正常叠字不动）")
    filt = compact(body_of(flt, "internal static List<string> FilterLanternWishes(List<string> raw)"))
    require(errors, "!seen.Add(key)" in filt, "[Lantern] FilterLanternWishes 必须按规范化文本去重")

    view = read("WishFountainDanmakuView.cs")
    prepare = compact(body_of(view, "private void PrepareContents(List<string> wishContents)"))
    require(errors, "WishFountainService.FilterLanternWishes(wishContents);" in prepare,
            "[Lantern] 星灯层的 PrepareContents 必须先经 FilterLanternWishes 清洗去重")
    require(errors, "privateconstintMAX_LANTERNS=12;" in compact(view), "[Lantern] 同屏星灯上限应为 12")

    ui = read("WishFountainUI.cs")
    refresh = compact(body_of(ui, "private void RefreshDanmakuDisplay()"))
    gate_at = refresh.find("if(!IsStarRiverEnabled()){CancelDanmakuFetch();HideDanmaku();return;}")
    request_at = refresh.find("WishFountainService.RequestRecentWishes(")
    require(errors, 0 <= gate_at < request_at, "[Toggle] 星灯熄了以后 RefreshDanmakuDisplay 不能再拉取心愿")
    warm = compact(body_of(ui, "private void ScheduleDanmakuWarmup()"))
    require(errors, "!IsStarRiverEnabled())" in warm and warm.find("!IsStarRiverEnabled()") < warm.find("WarmupPoolImmediate"),
            "[Toggle] 星灯熄了以后 ScheduleDanmakuWarmup 不能再预热")
    toggle = compact(body_of(read("WishFountainUI_Constellations.cs"), "private static bool IsStarRiverEnabled()"))
    require(errors, "BossRushLocalPrefs.GetBool(StarRiverPrefKey,true)" in toggle,
            "[Toggle] 星灯开关记在本机 BossRushLocalPrefs，默认亮")


def main():
    errors = []
    check_min_chars(errors)
    check_constellations(errors)
    check_lanterns(errors)
    if errors:
        print("WishConstellationLanternGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1
    print("WishConstellationLanternGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
