#!/usr/bin/env python3
"""AffinityGiftJournalGuard — 好感页「心意簿」（2026-11-24 玩家许愿第 6 项）的接线不变式。

1. 一份规则：NPCGiftSystem.ResolveGiftPreference 决定「这件礼物命中哪条心意规则」，反应类型（GetGiftReactionType）
   与心意簿都只读它，顺序是 厌恶物品 → 喜欢物品 → 收集品 LikedBy → 喜欢的标签；普通反应（类型基础值）不记。
2. 记录点：GiveGift 的普通礼物路径用同一次解析的结果记心意簿（TryRecordGiftPreference），第一次记下才给浮字挂纸笺；
   纸笺旗标包在 AddPoints 前后、finally 清掉，不会留给下一次好感变化。
3. 存档：knownLikes / knownDislikes 随好感存档序列化，解码缺字段读成空串（旧档 = 什么都还没摸清），
   声明了却不是字符串的拒收整份记录；Clone 带上它们。
4. 显示：好感页按当前配置对照着列（与解析同一判定顺序），配置里没有的旧令牌不显示。
5. 执行回归 NpcAuditFixes 的 Affinity 组编入 AffinityManagerGiftJournal.cs 并跑心意簿用例。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AFF = ("Integration", "Affinity")


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


def ordered(text, tokens):
    at = 0
    for token in tokens:
        found = text.find(token, at)
        if found < 0:
            return False
        at = found + len(token)
    return True


def main():
    errors = []

    def require(ok, message):
        if not ok:
            errors.append(message)

    gift_src = read(*(AFF + ("Systems", "NPCGiftSystem.cs")))
    resolve = compact(body_of(gift_src, "internal static string ResolveGiftPreference(string npcId, Item item, out bool liked)"))
    require(ordered(resolve, ["giftConfig.NegativeItems.ContainsKey(typeId)", "returnAffinityManager.GiftItemToken(typeId);",
                              "liked=true;", "giftConfig.PositiveItems.ContainsKey(typeId)", "returnAffinityManager.GiftItemToken(typeId);",
                              "CollectibleTuning.LikedGiftAffinity(npcId,typeId)>0", "returnAffinityManager.GiftItemToken(typeId);",
                              "FindPositiveTag(npcId,item)", "returnAffinityManager.GiftTagToken(tag);",
                              "liked=false;", "returnnull;"]),
            "[规则] ResolveGiftPreference 的顺序：厌恶物品 → 喜欢物品 → 收集品 LikedBy → 喜欢的标签，其余返回 null")
    reaction = compact(body_of(gift_src, "public static GiftReactionType GetGiftReactionType(string npcId, Item item)"))
    require("returnReactionOf(ResolveGiftPreference(npcId,item,outliked),liked);" in reaction,
            "[规则] 反应类型只读 ResolveGiftPreference，不另写一份判定")
    give = compact(body_of(gift_src, "public static bool GiveGift("))
    require(ordered(give, ["stringpreferenceToken=ResolveGiftPreference(npcId,item,outliked);",
                           "GiftReactionTypereactionType=ReactionOf(preferenceToken,liked);",
                           "preferenceToken!=null&&AffinityManager.TryRecordGiftPreference(npcId,liked,preferenceToken);",
                           "if(journalAdded)AffinityUIManager.NoteGiftJournalEntry(npcId);",
                           "try{AffinityManager.AddPoints(npcId,giftValue);}finally{AffinityUIManager.NoteGiftJournalEntry(null);}"]),
            "[记录] 普通礼物路径用同一次解析记心意簿；纸笺旗标包住 AddPoints，finally 清掉")
    ring = give.find("if(isDiamondRingGift)")
    drawing = give.find("DingdangDrawingConfig.TYPE_ID")
    record = give.find("TryRecordGiftPreference")
    require(0 <= ring < record and 0 <= drawing < record,
            "[记录] 戒指与叮当涂鸦的特殊分支不记心意簿（记录只在普通礼物路径）")

    ui = read(*(AFF + ("AffinityUIManager.cs",)))
    show = compact(body_of(ui, "public static void ShowAffinityChange(string npcId, int delta)"))
    require(ordered(show, ["booljournal=_journalNoteNpcId!=null&&string.Equals(_journalNoteNpcId,npcId,StringComparison.Ordinal);",
                           "_journalNoteNpcId=null;", "AddJournalNote(popup.transform);"]),
            "[提示] 纸笺只挂在同一位 NPC 的这次浮字上，挂完即清")
    require("_journalNoteNpcId=null;" in compact(body_of(ui, "public static void Cleanup()")), "[提示] Cleanup 清掉纸笺旗标")

    ser = compact(read(*(AFF + ("AffinityJsonSerializer.cs",))))
    for key in ("knownLikes", "knownDislikes"):
        require('SimpleJsonHelper.AppendString(sb,"' + key + '",data.' + key + '??string.Empty);' in ser,
                "[存档] 序列化缺少 " + key)
        require(key + '=entry.GetString("' + key + '",string.Empty)' in ser, "[存档] 解码缺字段要读成空串: " + key)
        require('"' + key + '"' in ser.split("foreach(stringkeyinnew[]{\"npcId\"")[-1].split("}")[0],
                "[存档] " + key + " 要进字符串类型校验（声明了却损坏就拒收）")
    data = compact(read(*(AFF + ("AffinityData.cs",))))
    require('publicstringknownLikes="";' in data and 'publicstringknownDislikes="";' in data
            and "knownLikes=this.knownLikes" in data and "knownDislikes=this.knownDislikes" in data,
            "[存档] AffinityData 要有两个心意字段（默认空），Clone 带上")

    tab_src = read(*(AFF + ("UI", "AffinityTabData.cs")))
    fill = compact(body_of(tab_src, "private static void FillGiftJournal(AffinityTabEntry entry, string npcId, INPCGiftConfig gift)"))
    require(ordered(fill, ["dislikes.Add(AffinityManager.GiftItemToken(typeId));", "!negative.ContainsKey(typeId)",
                           "CollectibleCatalog.At(i)", "negative.ContainsKey(def.TypeId)", "positive.ContainsKey(def.TypeId)",
                           "likes.Add(AffinityManager.GiftTagToken(tag));"]),
            "[显示] 好感页按与解析同一判定顺序列出心意规则（被厌恶表压住的不算喜欢）")
    side = compact(body_of(tab_src, "private static int FillJournalSide("))
    require("if(!configured.Contains(known[i]))continue;" in side, "[显示] 配置里已经没有的旧令牌不显示、不算已摸清")

    run = read("tests", "fixtures", "NpcAuditFixes", "run.py") or ""
    fixture = read("tests", "fixtures", "NpcAuditFixes", "Affinity.cs") or ""
    require('"AffinityManagerGiftJournal.cs"' in run and "TryRecordGiftPreference" in fixture
            and "legacy save without journal fields loads with an empty journal" in fixture,
            "[回归] NpcAuditFixes 的 Affinity 组要编入心意簿并跑往返 / 旧档用例")

    if errors:
        print("FAIL AffinityGiftJournalGuard")
        for line in errors:
            print("  " + line)
        return 1
    print("PASS AffinityGiftJournalGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
