#!/usr/bin/env python3
"""PetNestSoulFeedGuard — 遗种巢「献魂」（2026-11-24 玩家许愿第 5 项）的接线不变式。

1. 规则：2 遗魂换 1 经验，一次补满这一级（花费 = 这一级还差的经验 × 2），满级为 0。
2. 只扣这只崽自己血脉的遗魂：交易里按 pet.lineageKey 找账本条目，不走「任意血脉」的通用扣魂。
3. 每只崽每个游戏日一次：印记是 GameClock.Day + 1（0 留给「从没献过」），没有时钟就拒绝，
   不能把 0 当今天写进去；预检与事务内复核都要看印记。
4. 事务：BeginCandidate → 候选包里重新解析崽与账本并复核 → 扣魂、加经验、写印记 → CommitCandidate；
   复核失败与异常都 AbortTransaction。
5. 存档：lastSoulFeedDay 编码进崽的记录，解码缺字段读成 0（旧档 = 从没献过），Clone 带上它。
6. 界面：按钮挂在「成长」区下面的内嵌行，不进底栏；点了先过共享确认框（非危险样式），确认后才调服务；
   远征中不挂；今天献过 / 没有时钟只留一句话，不挂灰按钮。演出在详情头部建好后触发。
7. 执行回归 ContentTransactions 的 SoulFeedRegression 已登记并在 Main 里调用。

文本守卫防不住「保留 token、杀掉执行路径」，规则本身由 SoulFeedRegression 执行验证，
界面与演出靠实机清单（M_PETNEST_SOULFEED_01）。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(*parts):
    path = os.path.join(ROOT, *parts)
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
        text = fh.read()
    return clean_source(text) if path.endswith(".cs") else text


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


def ordered(text, tokens):
    """tokens 依次出现在 text 里（每个都在上一个之后）。"""
    at = 0
    for token in tokens:
        found = text.find(token, at)
        if found < 0:
            return False
        at = found + len(token)
    return True


def check_rules(errors):
    tuning = compact(read("PetNest", "PetNestTuning.cs"))
    require(errors, "internalconstintSoulFeedSoulsPerExp=2;" in tuning,
            "[规则] PetNestTuning.SoulFeedSoulsPerExp 必须是 2（2 遗魂换 1 经验）")

    service = read("PetNest", "PetNestService.cs")
    cost = compact(body_of(service, "internal static int SoulFeedCost(PetNestPetRecord pet)"))
    require(errors, "PetNestTuning.PetExpPerLevel-pet.exp" in cost and "*PetNestTuning.SoulFeedSoulsPerExp" in cost
            and "pet.level>=PetNestTuning.PetMaxLevel" in cost,
            "[规则] SoulFeedCost 必须是（这一级还差的经验）× SoulFeedSoulsPerExp，满级为 0")

    stamp = compact(body_of(service, "internal static long SoulFeedDayStamp()"))
    require(errors, "GameClock.Instance!=null?GameClock.Day+1L:0L" in stamp,
            "[每日] SoulFeedDayStamp 必须是 GameClock.Day + 1，没有时钟返回 0（0 留给从没献过）")

    feed = compact(body_of(service, "internal static bool TryFeedSouls(string petId, out string failureReasonId)"))
    if not feed:
        errors.append("[事务] 缺少 PetNestService.TryFeedSouls（或签名重复）")
        return
    require(errors, ordered(feed, ["pet.level>=PetNestTuning.PetMaxLevel", "\"pet_max_level\"",
                                   "today<=0L", "\"soul_feed_clock_missing\"",
                                   "pet.lastSoulFeedDay==today", "\"soul_fed_today\"",
                                   "GetSouls(pet.lineageKey)<SoulFeedCost(pet)", "\"souls_insufficient\"",
                                   "BeginCandidate(outfailureReasonId)"]),
            "[事务] 预检顺序：满级 → 没有时钟 → 今天献过 → 同脉遗魂不够，全部在 BeginCandidate 之前")
    require(errors, "PetNestPetState.OnExpedition" in feed and "\"pet_locked_by_expedition\"" in feed,
            "[事务] 远征中的崽不能献魂")
    require(errors, ordered(feed, ["BeginCandidate(outfailureReasonId)", "pet=TryGetPet(petId);",
                                   "entries[i].lineageKey,pet.lineageKey", "pet.lastSoulFeedDay==today",
                                   "PetNestPersistenceAccess.AbortTransaction();",
                                   "ledger.souls-=cost;", "PetNestProgressionService.AddExp(pet,cost/PetNestTuning.SoulFeedSoulsPerExp);",
                                   "pet.lastSoulFeedDay=today;", "returnCommitCandidate(outfailureReasonId);"]),
            "[事务] 候选包里重新解析崽与同脉账本并复核（不过就 Abort），再扣魂、加经验、写印记、提交")
    require(errors, "TrySpendSouls(" not in feed and "TryHealScar(" not in feed,
            "[同脉] 献魂只扣这只崽自己血脉的账本，不借用其他扣魂入口")
    catch = feed[feed.rfind("catch(Exceptione)"):] if "catch(Exceptione)" in feed else ""
    require(errors, "PetNestPersistenceAccess.AbortTransaction();" in catch and "\"soul_feed_failed:\"" in catch,
            "[事务] 异常路径必须 AbortTransaction 并回 soul_feed_failed")


def check_persistence(errors):
    codec = compact(read("PetNest", "PetNestPersistenceCodec.cs"))
    require(errors, ".Long(\"lastSoulFeedDay\",pet.lastSoulFeedDay)" in codec,
            "[存档] 编码器必须写 lastSoulFeedDay")
    require(errors, "pet.lastSoulFeedDay=node.GetLong(\"lastSoulFeedDay\",0L);" in codec,
            "[存档] 解码缺字段必须读成 0（旧档 = 从没献过）")
    models = compact(read("PetNest", "PetNestModels.cs"))
    require(errors, "publiclonglastSoulFeedDay;" in models and "clone.lastSoulFeedDay=lastSoulFeedDay;" in models,
            "[存档] PetNestPetRecord 要有 lastSoulFeedDay，Clone 要带上它（候选包是克隆出来的）")
    loc = read("Localization", "PetNestLocalization.cs") or ""
    for key in ("Fail_pet_max_level", "Fail_soul_fed_today", "Fail_soul_feed_clock_missing", "Fail_soul_feed_failed"):
        require(errors, re.search(r'Add\(map,\s*"' + key + r'",\s*"[^"]+",\s*"[^"]+"\)', loc) is not None,
                "[本地化] 献魂失败原因缺中英文: " + key)


def check_ui(errors):
    nest = read("PetNest", "PetNestUINestPage.cs")
    detail = compact(body_of(nest, "private static PetNestDetailData BuildPetDetail("))
    require(errors, ordered(detail, ["L10n.T(\"成长\",\"Growth\")", "if(!onExpedition)AddSoulFeedRow(detail,pet,ctx);",
                                     "L10n.T(\"性格\",\"Temperament\")"]),
            "[界面] 献魂一行紧跟「成长」区，远征中不挂")
    row = compact(body_of(nest, "private static void AddSoulFeedRow("))
    require(errors, bool(row) and "detail.Actions" not in row,
            "[界面] 献魂按钮不进底栏（底栏已有放生 / 治疤 / 远征 / 出战）")
    require(errors, ordered(row, ["PetNestProgressionService.IsAdult(pet)", "detail.InlineAfterSection=detail.Sections.Count-1;",
                                  "SoulFeedDayStamp()<=0L", "\"soul_feed_clock_missing\"",
                                  "PetNestService.SoulFedToday(pet)", "\"soul_fed_today\"",
                                  "detail.InlineAction=newPetNestActionData", "feed(petId);"]),
            "[界面] 满级不挂；没有时钟 / 今天献过只写一句话；否则挂内嵌按钮，点了交给 ctx.FeedSouls")
    require(errors, "TryFeedSouls(" not in row and "Interactable=false" not in row,
            "[界面] 内嵌按钮不直接落档、不挂灰按钮（先确认，再由确认框调服务）")
    note = compact(body_of(nest, "internal static bool TryFeedSoulsAndNote(string petId, out int level)"))
    require(errors, "PetNestService.TryFeedSouls(petId,outfailure)" in note and "NoteResult(ok,failure," in note,
            "[界面] 确认后经 TryFeedSoulsAndNote 调服务并把失败原因记回面板")

    soul = read("PetNest", "PetNestUISoulFeed.cs")
    request = compact(body_of(soul, "private void RequestSoulFeed(string petId)"))
    require(errors, ordered(request, ["newBossRushConfirmDialog.Options", "Danger=false",
                                      "PetNestUIPages.TryFeedSoulsAndNote(petId,outlevel)", "ShowConfirm(options,Refresh);"]),
            "[确认] 献魂先过共享确认框（非危险样式），确认后才落档")
    require(errors, "PetNestCompanionRuntime.RefreshProgression(" in request,
            "[表现] 献魂升级后刷新正跟着出战那只的成长表现")
    fx = compact(body_of(soul, "private void PlayPendingSoulFeedFx(Transform header)"))
    require(errors, "_pendingSoulFeedPetId=null;" in fx and "header.Find(\"Portrait\")" in fx,
            "[演出] 魂火只播一次，挂在立绘框下")

    ui = compact(read("PetNest", "PetNestUI.cs"))
    require(errors, "context.FeedSouls=RequestSoulFeed;" in ui, "[界面] 巢页上下文要接上 RequestSoulFeed")
    layout = read("PetNest", "PetNestUILayout.cs")
    render = compact(body_of(layout, "private void RenderDetail(PetNestDetailData detail)"))
    require(errors, "if(i==detail.InlineAfterSection)RenderInlineAction(detail);" in render,
            "[界面] RenderDetail 要在对应分区后画内嵌行")
    header = compact(body_of(layout, "private void SpawnDetailHeader(PetNestDetailData detail)"))
    require(errors, header.endswith("PlayPendingSoulFeedFx(header.transform);"),
            "[演出] 详情头部建好（立绘就位）后再触发魂火")


def check_regression(errors):
    run = read("tests", "fixtures", "ContentTransactions", "run.py") or ""
    program = compact(read("tests", "fixtures", "ContentTransactions", "Program.cs"))
    regression = read("tests", "fixtures", "ContentTransactions", "SoulFeedRegression.cs")
    require(errors, 'HERE / "SoulFeedRegression.cs"' in run and regression is not None,
            "[回归] ContentTransactions 要编入 SoulFeedRegression.cs")
    main = compact(body_of(read("tests", "fixtures", "ContentTransactions", "Program.cs"), "static void Main(string[] args)"))
    require(errors, "PetNestSoulFeed();" in main, "[回归] ContentTransactions 的 Main 要调用 PetNestSoulFeed()")
    require(errors, "classGameClock" in program + compact(read("tests", "fixtures", "ContentTransactions", "Stubs.cs")),
            "[回归] ContentTransactions 要有 GameClock 替身（PetNestService 读 GameClock.Day）")


def main():
    errors = []
    check_rules(errors)
    check_persistence(errors)
    check_ui(errors)
    check_regression(errors)
    if errors:
        print("FAIL PetNestSoulFeedGuard")
        for line in errors:
            print("  " + line)
        return 1
    print("PASS PetNestSoulFeedGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
