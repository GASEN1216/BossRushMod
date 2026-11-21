"""Guard: 许愿抽奖的品质概率与游玩时长封顶（2026-11-21 玩家反馈「许愿概率太高」后削弱）。

锁住三件事，任何一件退回去都会让许愿重新变成稳定出高档的提款机：
1. 品质权重表与奖池下限：Q3 45 / Q4 33 / Q5 15 / Q6 5.5 / Q7 1.2 / Q8 0.3（合计 100），下限 3 档。
2. 按本档游玩时长封顶：不满 5 / 15 / 30 / 60 小时最高 4 / 5 / 6 / 7 档；时长读不出按最低一档封顶。
3. 品质对整个奖池掷、并带封顶：愿望关键词筛出的池子只决定同品质里抽哪件，
   不能用筛过的池子掷品质（池里只有高档时等于保底高档）。
"""

from pathlib import Path
import re
import sys


SERVICE = Path("Integration/WishFountain/WishFountainService.cs")
SELECTION = Path("Integration/WishFountain/WishFountainRewardSelection.cs")
# 品质掷骰与时长封顶从 Selection 原样提到这里（行数预算），两份一起查。
ODDS = Path("Integration/WishFountain/WishFountainRewardOdds.cs")


def fail(message: str) -> int:
    print("WishRewardOddsGuard: FAIL - " + message)
    return 1


def strip_line_comments(text: str) -> str:
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def method_body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
    return ""


def float_array(text: str, name: str):
    m = re.search(r"\b" + re.escape(name) + r"\s*=\s*(?:new\s+float\s*\[\s*\]\s*)?\{([^}]*)\}", text)
    if not m:
        return None
    return [float(v.strip().rstrip("fF")) for v in m.group(1).split(",") if v.strip()]


def int_array(text: str, name: str):
    m = re.search(r"\b" + re.escape(name) + r"\s*=\s*(?:new\s+int\s*\[\s*\]\s*)?\{([^}]*)\}", text)
    if not m:
        return None
    return [int(v.strip()) for v in m.group(1).split(",") if v.strip()]


def main() -> int:
    service = strip_line_comments(SERVICE.read_text(encoding="utf-8-sig"))
    if not ODDS.exists():
        return fail("缺少 " + str(ODDS) + "（品质掷骰与时长封顶所在的 partial）")
    selection = strip_line_comments(SELECTION.read_text(encoding="utf-8-sig")) + "\n" \
        + strip_line_comments(ODDS.read_text(encoding="utf-8-sig"))

    # 1. 权重表与下限
    if not re.search(r"private const int WISH_REWARD_MIN_QUALITY = 3;", service):
        return fail("奖池下限必须是 3 档（WISH_REWARD_MIN_QUALITY = 3）")
    weights = float_array(service, "WishRewardBaseQualityWeights")
    if weights != [0.0, 0.0, 45.0, 33.0, 15.0, 5.5, 1.2, 0.3]:
        return fail("品质权重必须是 Q1..Q8 = 0/0/45/33/15/5.5/1.2/0.3，实际 " + repr(weights))
    if abs(sum(weights) - 100.0) > 1e-6:
        return fail("品质权重合计必须是 100（按百分比读）")

    # 2. 游玩时长封顶
    hours = float_array(service, "WishRewardPlayHoursThresholds")
    caps = int_array(service, "WishRewardQualityCapBelowThreshold")
    if hours != [5.0, 15.0, 30.0, 60.0] or caps != [4, 5, 6, 7]:
        return fail("游玩时长封顶必须是 不满 5/15/30/60 小时 → 最高 4/5/6/7 档，实际 " + repr(hours) + " " + repr(caps))
    cap_body = compact(method_body(selection, "private static int GetWishRewardQualityCapByPlayTime(out double playedHours)"))
    if "GameClock.GetRealTimePlayedOfSaveSlot(SavesSystem.CurrentSlot).TotalHours" not in cap_body:
        return fail("游玩时长要读官方 GameClock.GetRealTimePlayedOfSaveSlot(当前槽)")
    if not re.search(r"catch \(Exception e\) \{.*?return WishRewardQualityCapBelowThreshold\[0\];", cap_body):
        return fail("读不出游玩时长时必须按最低一档封顶，不能放开")
    if "return 8;" not in cap_body:
        return fail("满最后一个阈值才放开到 8 档")

    # 3. 品质对整个奖池掷、带封顶
    roll = compact(method_body(selection, "private static int RollWishRewardQuality(WishRewardMatchResult match, Dictionary<int, List<int>> availableBuckets, int maxQuality)"))
    if "if (i + 1 > maxQuality || !HasWishRewardBucket(availableBuckets, i + 1))" not in roll:
        return fail("RollWishRewardQuality 必须把高于封顶的品质权重清零")
    core = compact(method_body(selection, "private static int RollWishRewardTypeIdCore("))
    if "RollWishRewardQuality(effectiveMatch, wishRewardQualityBuckets, qualityCap)" not in core:
        return fail("品质必须对整个奖池（wishRewardQualityBuckets）掷并带时长封顶，不能用关键词筛过的池子")
    if "RollWishRewardQuality(effectiveMatch, activeBuckets" in core:
        return fail("不能再用关键词筛过的 activeBuckets 掷品质")
    if "HasWishRewardBucket(activeBuckets, rolledQuality) ? activeBuckets : wishRewardQualityBuckets" not in core:
        return fail("筛过的池子在掷出的品质里有货才从里面挑，没有就退回整池同品质")
    if selection.count("RollWishRewardQuality(") != 2:
        return fail("RollWishRewardQuality 只能有定义与 RollWishRewardTypeIdCore 一处调用")

    print("WishRewardOddsGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
