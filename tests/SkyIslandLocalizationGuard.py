"""天空岛玩家可见文案必须中英双语（CR-2026-09-09-011）。

背景：这张图的叙事层一度**整层**只有中文——剧情面板、选项、HUD 目标行、桥口木牌、
居民服务回话全是硬编码中文串，而 `SkyIslandBounty.NameEn` 写好了却零调用。
全 Mod 有约 2700 处 `L10n.T`（Campaign 的叙事同样走它），`WikiContent/en/` 也按英文正文
维护，英文玩家进岛却一句都读不懂。

**本守卫的口径**：以下文件里，任何**中文字符串字面量**都必须落在
`L10n.T("中文", "English")` 这个调用里，唯二例外：

1. 诊断文本 —— `Debug.Log*` / `DevLog` / `CriticalLog` / `throw new ...Exception(...)` /
   `lease.Abort(...)`。这些是给维护者看的，按 `AGENTS.md` 的「维护语言中文」保留中文；
   它们只在 Mod 部署损坏或官方契约变更时才会露面，且外层提示语（如「天空岛创建失败：」）
   本身已经双语。
2. 中英对照表的**中文那一半** —— 例如 `NameCn(...)`、`L10n.IsChinese ? new[] { ... }`、
   `ApplyStoryChampion(created, "zheling", "折翎", "Zheling")`：它们本来就是成对的。
   这类必须在同一条语句里出现英文对照，守卫会核对。

反向验证：把任意一句玩家可见文案的 `L10n.T("中文", "English")` 换回裸中文串，本守卫必红。
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = ROOT / "DebugAndTools" / "SkyIsland"

# 玩家可见文案所在的文件。新增有玩家文案的文件必须登记进来。
FILES = [
    "SkyIslandWorldStory.cs",
    "SkyIslandStoryRules.cs",
    "SkyIslandStoryService.cs",
    "SkyIslandServices.cs",
    "SkyIslandBounty.cs",
    "SkyIslandGates.cs",
    "SkyIslandSession.cs",
    "SkyIslandEncounters.cs",
    "SkyIslandScavenging.cs",
    "SkyIslandStoryPresentation.cs",
    "SkyIslandRuntimeModule.cs",
    "SkyIslandGroundRing.cs",
    "SkyIslandControls.cs",
    "SkyIslandGuideInteractable.cs",
    "SkyIslandSearchPoint.cs",
    "SkyIslandResidentInteractable.cs",
    "SkyIslandLighting.cs",
    "SkyIslandStormBoss.cs",
    "SkyIslandEnemyTiers.cs",
    "SkyIslandMapMarkers.cs",
    "SkyIslandLetters.cs",
    "SkyIslandPuzzles.cs",
    "SkyIslandCrew.cs",
    "SkyIslandJournal.cs",
    "SkyIslandItemRules.cs",
    # 内容批次三：采集点、合成台、局内耗材与夜风。
    "SkyIslandFieldcraftRules.cs",
    "SkyIslandGathering.cs",
    "SkyIslandFieldcraft.cs",
    # 串联：岛上的灯（七盏风晶灯与三处灶火）。
    "SkyIslandLights.cs",
]

# 严格两个字符串字面量的 L10n.T 调用：中文那一半必须有英文对照。
L10N_PAIR = re.compile(r'L10n\.T\(\s*"(?:[^"\\]|\\.)*"\s*,\s*"(?:[^"\\]|\\.)*"\s*\)', re.S)
# 任何「中文串 , 纯 ASCII 串」的相邻实参：Announce(cn, en)、new Preset(cn, en, ...)、
# ApplyStoryChampion(id, cn, en) 都是这个形状，同样属于已配好英文对照。
ARG_PAIR = re.compile(
    r'"(?:[^"\\\n]|\\.)*[一-鿿](?:[^"\\\n]|\\.)*"\s*,\s*"(?:[^"一-鿿\\\n]|\\.)*"', re.S)
LITERAL = re.compile(r'"(?:[^"\\\n]|\\.)*"')
HAN = re.compile(r"[一-鿿]")

# 诊断上下文：命中其一即视为维护者文本。语句可能跨行，所以按「本行 + 前两行」判定。
DIAGNOSTIC = (
    "Debug.Log", "Debug.LogWarning", "Debug.LogError",
    "ModBehaviour.DevLog", "ModBehaviour.CriticalLog", "DevLog(", "CriticalLog(",
    "throw new", "Exception(", "lease.Abort(",
)


def statement_context(lines, index):
    return "\n".join(lines[max(0, index - 2): index + 1])


def paired_accessor_spans(source):
    """`XxxCn(...)` 方法体的行区间，前提是同文件里存在配套的 `XxxEn(...)`。

    这类方法本来就是中英对照表的两半（`NameCn` / `NameEn`、`TierNameCn` / `TierNameEn`），
    中文那一半必须是裸串。没有配套 En 的 `Cn` 方法**不**豁免——那正是漏译的形状。
    """
    lines = source.splitlines()
    heads = {}
    for index, line in enumerate(lines):
        # 只认方法**声明**（`string XxxCn(`）。不加 `string\s+` 前缀的话，
        # `Name() { return L10n.T(NameCn(kind), NameEn(kind)); }` 这样的转发一行会先被匹配上，
        # 于是真正的 NameCn 方法体反而不在豁免区间里。
        match = re.search(r"\bstring\s+(\w+)(Cn|En)\s*\(", line)
        if match:
            heads.setdefault(match.group(1) + match.group(2), index)
    spans = []
    for name, start in heads.items():
        if not name.endswith("Cn") or name[:-2] + "En" not in heads:
            continue
        depth = 0
        opened = False
        for index in range(start, len(lines)):
            depth += lines[index].count("{") - lines[index].count("}")
            opened = opened or "{" in lines[index]
            if opened and depth <= 0:
                spans.append((start, index))
                break
    return spans


def main():
    errors = []
    localized_total = 0

    for name in FILES:
        path = SKY / name
        if not path.exists():
            errors.append("%s 不存在：文件清单与磁盘不一致" % name)
            continue
        source = clean_source(path.read_text(encoding="utf-8"))
        localized_total += len(L10N_PAIR.findall(source))
        # 先把已配好英文对照的形状整体抹掉，剩下的中文字面量才是嫌疑对象。
        # 换行会被抹平，所以逐字符替换成等长占位，行号不受影响。
        stripped = L10N_PAIR.sub(lambda m: re.sub(r"[^\n]", "_", m.group(0)), source)
        stripped = ARG_PAIR.sub(lambda m: re.sub(r"[^\n]", "_", m.group(0)), stripped)
        lines = stripped.splitlines()
        exempt = paired_accessor_spans(source)
        for index, line in enumerate(lines):
            for literal in LITERAL.findall(line):
                if not HAN.search(literal):
                    continue
                if any(start <= index <= end for start, end in exempt):
                    continue
                # 显式的中英分支：L10n.IsChinese ? new[] { 中文… } : new[] { English… }
                if "L10n.IsChinese" in "\n".join(lines[max(0, index - 6): index + 1]):
                    continue
                # 存档门面的 DisplayName 只出现在 BossRushSlotJsonStore 的 DevLog 里，不进 UI。
                if "DisplayName =" in line:
                    continue
                context = statement_context(lines, index)
                if any(token in context for token in DIAGNOSTIC):
                    continue
                errors.append("%s:%d 玩家可见文案未走 L10n.T：%s" % (name, index + 1, literal))

    if localized_total < 150:
        errors.append("L10n.T 成对文案只剩 %d 条，远低于本轮落地的量，疑似被整体回退" % localized_total)

    # NameEn 曾经写好却零调用，英文玩家在派单与 HUD 上仍看到中文。唯一取用点必须是 Name()。
    bounty = clean_source((SKY / "SkyIslandBounty.cs").read_text(encoding="utf-8"))
    if "internal static string Name(SkyIslandBountyKind kind)" not in bounty:
        errors.append("SkyIslandBounty 必须提供中英合一的 Name()，不能让调用方直接取 NameCn")
    if "L10n.T(NameCn(kind), NameEn(kind))" not in bounty:
        errors.append("SkyIslandBounty.Name() 必须同时用上 NameCn 与 NameEn")
    world = clean_source((SKY / "SkyIslandWorldStory.cs").read_text(encoding="utf-8"))
    if "SkyIslandBounty.NameCn(" in world:
        errors.append("派单面板不得绕过 SkyIslandBounty.Name() 直接取中文名")
    if "SkyIslandWorldStory.ResidentName(" not in clean_source(
            (SKY / "SkyIslandResidents.cs").read_text(encoding="utf-8")):
        errors.append("居民显示名必须复用 SkyIslandWorldStory.ResidentName 的中英对照")

    print("SkyIslandLocalizationGuard: " + (
        "FAIL\n" + "\n".join(errors) if errors else "PASS (%d 条成对文案)" % localized_total))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
