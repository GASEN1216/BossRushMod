"""AchievementWikiStatsGuard — 成就数量、隐藏数与总奖金必须与 Wiki 成就大全的统计行一致。

2026-11-25 文案优化方案 P2/P3-3 第 2 项：成就新增或改奖金时，Wiki 的「共有 N 个成就」「总奖励金额」
常常忘了跟着改，玩家照着 Wiki 算奖金会对不上。这里只比对统计数字，不比对每条描述（描述由人审）。

事实源：`Achievement/BossRushAchievementManager.cs` 的 `RegisterAllAchievements()` 里每一次 `Register(...)`：
  Register(id, nameCN, nameEN, descCN, descEN, AchievementCategory.X, cashReward, difficulty[, isHidden])
对照：`WikiContent/zh/system__achievements_list.md` 与 `WikiContent/en/system__achievements_list.md` 的
概述句与「统计」节。
"""
from pathlib import Path
import re
import sys

from cs_source_util import strip_comments

ROOT = Path(__file__).resolve().parents[1]
MANAGER = ROOT / "Achievement" / "BossRushAchievementManager.cs"
WIKI_ZH = ROOT / "WikiContent" / "zh" / "system__achievements_list.md"
WIKI_EN = ROOT / "WikiContent" / "en" / "system__achievements_list.md"
CODEX_TUNING = ROOT / "Integration" / "Codex" / "CodexTuning.cs"

STRING = r'"(?:[^"\\]|\\.)*"'
# id 可以是字符串字面量或常量（图鉴成就用 CodexTuning.AchievementXxx）；奖金可以是数字或 CodexTuning 常量。
REGISTER = re.compile(
    r'\bRegister\(\s*(' + STRING + r'|[A-Za-z_][\w.]*)\s*,'
    + r'(?:\s*' + STRING + r'\s*,){4}'
    + r'\s*AchievementCategory\.\w+\s*,\s*(\d+L?|[A-Za-z_][\w.]*)\s*,\s*(\d+)\s*(?:,\s*(true|false|isHidden\s*:\s*(?:true|false)))?\s*\)'
)


def codex_constants():
    """CodexTuning 里的 long 常量（图鉴里程碑奖金）。"""
    text = strip_comments(CODEX_TUNING.read_text(encoding="utf-8-sig"))
    return {m.group(1): int(m.group(2)) for m in re.finditer(r'const\s+long\s+(\w+)\s*=\s*(\d+)L?\s*;', text)}


def resolve_reward(token, constants):
    token = token.strip()
    if token.rstrip("L").isdigit():
        return int(token.rstrip("L"))
    name = token.split(".")[-1]
    if token.startswith("CodexTuning.") and name in constants:
        return constants[name]
    return None


def parse_code():
    source = strip_comments(MANAGER.read_text(encoding="utf-8-sig"))
    start = source.find("private static void RegisterAllAchievements()")
    end = source.find("private static void Register(string id", start)
    if start < 0 or end < 0:
        return None
    body = source[start:end]
    raw_calls = len(re.findall(r'\bRegister\(', body))
    rows = REGISTER.findall(body)
    if raw_calls != len(rows):
        return "RegisterAllAchievements 里有 %d 处 Register(，但只解析出 %d 条；参数格式变了就同步本守卫的解析" % (raw_calls, len(rows))
    constants = codex_constants()
    rewards = [resolve_reward(r[1], constants) for r in rows]
    unresolved = [r[1] for r, v in zip(rows, rewards) if v is None]
    if unresolved:
        return "解析不了这些奖金写法：" + ", ".join(unresolved) + "（新常量来源要同步本守卫）"
    count = len(rows)
    total = sum(rewards)
    hidden = sum(1 for r in rows if r[3] and r[3].replace(" ", "").endswith("true"))
    return count, hidden, total


def to_int(text):
    return int(text.replace(",", ""))


def check_wiki(path, patterns, expected, errors, label):
    text = path.read_text(encoding="utf-8-sig")
    count, hidden, total = expected
    m = re.search(patterns["overview"], text)
    if not m:
        errors.append("[%s] 找不到概述里的成就总数句" % label)
    elif int(m.group(1)) != count:
        errors.append("[%s] 概述写 %s 个成就，代码注册了 %d 个" % (label, m.group(1), count))
    m = re.search(patterns["stats"], text)
    if not m:
        errors.append("[%s] 找不到「统计」节的总成就数行" % label)
    else:
        if int(m.group(1)) != count:
            errors.append("[%s] 统计写 %s 个成就，代码注册了 %d 个" % (label, m.group(1), count))
        if int(m.group(2)) != hidden:
            errors.append("[%s] 统计写含 %s 个隐藏成就，代码里隐藏的有 %d 个" % (label, m.group(2), hidden))
    m = re.search(patterns["reward"], text)
    if not m:
        errors.append("[%s] 找不到「统计」节的总奖励金额行" % label)
    elif to_int(m.group(1)) != total:
        errors.append("[%s] 总奖励金额写 %s，代码奖金合计 %s" % (label, m.group(1), "{:,}".format(total)))


def main():
    parsed = parse_code()
    if parsed is None:
        print("AchievementWikiStatsGuard: FAIL")
        print("  - 找不到 RegisterAllAchievements() 或 Register 定义")
        return 1
    if isinstance(parsed, str):
        print("AchievementWikiStatsGuard: FAIL")
        print("  - " + parsed)
        return 1

    errors = []
    check_wiki(WIKI_ZH, {
        "overview": r"共有\s*(\d+)\s*个成就",
        "stats": r"总成就数：\s*(\d+)\s*个（含\s*(\d+)\s*个隐藏成就）",
        "reward": r"总奖励金额：\s*`￥([\d,]+)`",
    }, parsed, errors, "Wiki 中文")
    check_wiki(WIKI_EN, {
        "overview": r"contains\s+(\d+)\s+achievements",
        "stats": r"Total achievements:\s*(\d+)\s*\(including\s+(\d+)\s+hidden\)",
        "reward": r"Total reward amount:\s*`\$([\d,]+)`",
    }, parsed, errors, "Wiki 英文")

    if errors:
        print("AchievementWikiStatsGuard: FAIL")
        for e in errors:
            print("  - " + e)
        return 1
    print("AchievementWikiStatsGuard: PASS（%d 个成就，含 %d 个隐藏，奖金合计 %s）" % (parsed[0], parsed[1], "{:,}".format(parsed[2])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
