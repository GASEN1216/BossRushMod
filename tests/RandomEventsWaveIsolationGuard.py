"""Guard: 局内随机事件与波次状态机的隔离不变式。

背景：标准 BossRush / 无间炼狱靠「当前波次 Boss 实例集合」推进波次
（WavesArena 的 currentWaveBosses / currentBoss，外加 TryFixStuckWaveIfNoBossAlive
这条自愈路径）；Mode D 则靠逐实例 OnDeadEvent 监听 + modeDCurrentWaveEnemies。

「不速之客（Boss 乱入）」事件会在局中额外生成一只 Boss。它必须对波次状态机
**完全不可见**：不写进上述任何容器、不注册波次死亡回调。否则会出现两类事故——
乱入 Boss 被计入本波，玩家清完波次却不推进（卡波）；或乱入 Boss 死亡被当成
波次 Boss 死亡，波次提前推进（跳波）。两者都只在实机长局里偶发，人工冒烟抓不到。

守卫内容：
  1. RandomEvents/ 下严禁出现波次状态机的真相来源符号。
  2. 乱入生成路径必须保留敌对性安全网 SetTeam(Teams.wolf)（AGENTS.md 4.5）——
     官方 preset 队伍可能是中立，漏了就会生成一只不攻击也打不死的雕像。
  3. 乱入生成物必须带 RndEvt_ 命名前缀，便于实机 log 排查与残留识别。
  4. RandomEventId 每个成员都要有对应事件类，且 OnCleanup 不得为空壳——
     空的清理等于没有清理，局末会留下敌人增益/强制天气/商人 NPC/遮罩 Canvas。
"""

from pathlib import Path
import re
import sys

EVENTS_DIR = Path("RandomEvents")
SPAWN_BRIDGE = EVENTS_DIR / "RandomEventEffectsBridge_Spawn.cs"

FORBIDDEN_WAVE_SYMBOLS = (
    ("currentWaveBosses", "本波 Boss 集合：写入会让乱入 Boss 被计入波次进度"),
    ("bossesInCurrentWaveRemaining", "本波剩余计数：写入会直接错算推波条件"),
    ("modeDCurrentWaveEnemies", "Mode D 本波敌人表：写入会污染 Mode D 波次判定"),
    ("RegisterModeDEnemyDeath", "Mode D 死亡登记：调用会把乱入死亡当成波次死亡"),
    ("ProceedAfterWaveFinished", "推波入口：随机事件永远不该主动推进波次"),
    ("TryFixStuckWaveIfNoBossAlive", "波次自愈：随机事件不得参与波次完整性自检"),
)


def fail(message):
    print("RandomEventsWaveIsolationGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    if not EVENTS_DIR.is_dir():
        return fail("找不到 " + EVENTS_DIR.as_posix() + " 目录")

    sources = sorted(EVENTS_DIR.rglob("*.cs"))
    if not sources:
        return fail(EVENTS_DIR.as_posix() + " 下没有任何 .cs")

    # ---- 1) 波次符号隔离 ----
    for path in sources:
        code = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        for symbol, why in FORBIDDEN_WAVE_SYMBOLS:
            if re.search(r"\b" + re.escape(symbol) + r"\b", code):
                return fail(
                    path.as_posix() + " 触碰了波次状态机符号 " + symbol
                    + "。" + why + "。随机事件生成物必须对波次状态机完全不可见。")

    # ---- 2) 敌对性安全网 ----
    if not SPAWN_BRIDGE.is_file():
        return fail("找不到乱入生成桥 " + SPAWN_BRIDGE.as_posix())
    spawn_code = strip_comments(SPAWN_BRIDGE.read_text(encoding="utf-8", errors="ignore"))
    if "SetTeam(Teams.wolf)" not in spawn_code:
        return fail(
            SPAWN_BRIDGE.as_posix() + " 缺少敌对性安全网 SetTeam(Teams.wolf)"
            "（AGENTS.md 4.5）。官方 preset 队伍可能是中立，漏掉会生成不攻击、"
            "不可击杀、还会拖到事件超时的 Boss。")

    # ---- 3) 生成物命名前缀 ----
    if "RndEvt_" not in spawn_code:
        return fail(
            SPAWN_BRIDGE.as_posix() + " 的生成物缺少 RndEvt_ 命名前缀，"
            "实机排查残留与区分波次 Boss 全靠它。")

    # ---- 4) 事件实现与清理出口 ----
    models = (EVENTS_DIR / "RandomEventModels.cs").read_text(encoding="utf-8", errors="ignore")
    enum_block = re.search(r"enum\s+RandomEventId\s*\{(.*?)\}", models, flags=re.S)
    if not enum_block:
        return fail("RandomEventModels.cs 里找不到 RandomEventId 枚举")
    members = [m for m in re.findall(
        r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^,\n]*)?,?\s*$",
        strip_comments(enum_block.group(1)), flags=re.M) if m != "None"]

    catalog_sources = [p for p in sources if p.name.startswith("RandomEventCatalog")]
    if not catalog_sources:
        return fail("找不到事件目录 RandomEventCatalog*.cs")
    catalog = strip_comments(
        "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in catalog_sources))

    subclasses = re.findall(r"class\s+([A-Za-z0-9_]+)\s*:\s*RandomEventBase", catalog)
    if len(subclasses) < len(members):
        return fail(
            "RandomEventId 有 " + str(len(members)) + " 个事件（" + ", ".join(members)
            + "），但只找到 " + str(len(subclasses)) + " 个 RandomEventBase 子类。"
            "只加枚举不加实现，调度器抽中它就会空转掉一次触发机会。")

    if re.search(r"override\s+void\s+OnCleanup\s*\([^)]*\)\s*\{\s*\}", catalog):
        return fail("存在空的 OnCleanup 实现，事件结束后世界状态不会被还原")

    cleanups = len(re.findall(r"override\s+void\s+OnCleanup\b", catalog))
    if cleanups < len(subclasses):
        return fail(
            "事件子类 " + str(len(subclasses)) + " 个，OnCleanup 实现只有 "
            + str(cleanups) + " 个。每个改动世界状态的事件都必须能还原。")

    print("RandomEventsWaveIsolationGuard: PASS（" + str(len(subclasses))
          + " 个事件，波次符号 0 触碰）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
