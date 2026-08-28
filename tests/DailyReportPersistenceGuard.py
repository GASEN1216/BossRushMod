"""Guard: 鸭科夫日报的存档管线与计时口径不变式。

背景：日报的「一天」是自算的（不跟随官方 GameClock.Day），跨天会结算签到、
断签与悬赏并发奖。这些不变式一旦被破坏，玩家会丢签到进度或重复领奖，
而且症状要等一个游戏日（≈24 现实分钟）之后才显现，人工冒烟很难抓。

守卫内容：
  1. 计时口径：只累计宿主 deltaTime * clockTimeScale，禁止订阅 GameClock.OnGameClockStep
     或改读 GameClock.Day（那会把睡觉/newBoot 的时间跳变算进来）。
  2. 一天的秒数必须与官方 GameClock.SecondsPerDay 一致（86300，不是 86400）。
  3. 存档走 Save<string> 整存 + schemaVersion 前置校验 + 回读核对 + 写屏障 fail-closed。
  4. SaveFile 只能出现在协调器里，且每批至多一次。
  5. 里程碑发奖必须先发后标记，且用位掩码做幂等。
  6. 事件订阅必须成对退订（AGENTS.md 4.6）。
  7. 新增 .cs 必须进编译清单（AGENTS.md 4.1）。
"""

from pathlib import Path
import re
import sys


TUNING = Path("Integration/DailyReport/DailyReportTuning.cs")
SERVICE = Path("Integration/DailyReport/DailyReportService.cs")
PERSISTENCE = Path("Integration/DailyReport/DailyReportPersistence.cs")
COORDINATOR = Path("Integration/DailyReport/DailyReportSaveCoordinator.cs")
REWARDS = Path("Integration/DailyReport/DailyReportRewards.cs")
COLLECTOR = Path("Integration/DailyReport/DailyReportStatsCollector.cs")
MODULE = Path("Integration/DailyReport/DailyReportRuntimeModule.cs")
COMPILE_LIST = Path("compile_official.bat")

REQUIRED_SOURCES = [
    TUNING, SERVICE, PERSISTENCE, COORDINATOR, REWARDS, COLLECTOR, MODULE,
    Path("Integration/DailyReport/DailyReportModels.cs"),
    Path("Integration/DailyReport/DailyReportCodec.cs"),
    Path("Integration/DailyReport/DailyReportContent.cs"),
    Path("Integration/DailyReport/DailyReportBounty.cs"),
    Path("Integration/DailyReport/DailyReportInteractable.cs"),
    Path("Integration/DailyReport/DailyReportUI.cs"),
    Path("Integration/DailyReport/DailyReportUIBridge.cs"),
    Path("Integration/DailyReport/DailyReportMailboxBuilder.cs"),
    Path("Integration/DailyReport/DailyReportMailboxRuntime.cs"),
    Path("Config/ConfigDailyReport.cs"),
    Path("Localization/DailyReportLocalization.cs"),
]


def fail(message):
    print("DailyReportPersistenceGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    """去掉 // 行注释与 /* */ 块注释。

    本 guard 断言的是**代码**，不是散文：源码注释里会成段解释
    "为什么不订阅 GameClock.OnGameClockStep"，不剥注释就会把这些解释
    误判成违规。
    """
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    for path in REQUIRED_SOURCES:
        if not path.exists():
            return fail("缺少源文件 " + path.as_posix())

    tuning = TUNING.read_text(encoding="utf-8")
    service = SERVICE.read_text(encoding="utf-8")
    persistence = PERSISTENCE.read_text(encoding="utf-8")
    coordinator = COORDINATOR.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    collector = COLLECTOR.read_text(encoding="utf-8")
    compile_list = COMPILE_LIST.read_text(encoding="utf-8", errors="ignore")

    # ---- 1) 一天的秒数必须镜像官方值 ----
    if "GameSecondsPerDay = 86300d" not in tuning:
        return fail(
            "一天必须是 86300 游戏秒（镜像官方 GameClock.SecondsPerDay），不是 86400")

    # ---- 2) 计时口径：不得订阅 OnGameClockStep，也不得改读 GameClock.Day ----
    service_code = strip_comments(service)
    if re.search(r"OnGameClockStep\s*[-+]=", service_code):
        return fail(
            "计时不得订阅 GameClock.OnGameClockStep：睡觉与 newBoot 走 StepTimeTil "
            "不经 Update，订阅它会把时间跳变算成玩家的游玩时长")
    if re.search(r"GameClock\.Day", service_code):
        return fail(
            "计时不得读 GameClock.Day：日报的天号是自算的，跟随官方 Day 会被睡觉跳变污染")
    if "clockTimeScale" not in service_code:
        return fail("计时必须乘官方 clockTimeScale，才能跟随玩家改过的时钟倍率")

    # ---- 3) 存档整存 + 版本前置 + 回读核对 + 写屏障 ----
    for needle, message in (
        ("SavesSystem.Save<string>", "必须 Save<string> 整存 JSON，不用 typed Save<T>"),
        ("SavesSystem.KeyExisits", "必须用 KeyExisits 前置分类新档与老档"),
        ("ReadSchemaVersion", "必须在解码前校验 schemaVersion"),
        ("_writeBarrier = true", "未知版本 / 不可读 payload 必须进写屏障"),
        ("readback mismatch", "写入后必须回读核对"),
    ):
        if needle not in persistence:
            return fail(message + " -> " + needle)

    # 写屏障必须真的挡住写入
    if "if (HasWriteBarrier) return false;" not in persistence:
        return fail("Store 必须在写屏障时拒写，否则会覆盖更高版本的存档")

    # ---- 4) SaveFile 只能在协调器里，且每批至多一次 ----
    for path, text in (
        (SERVICE, service_code),
        (PERSISTENCE, strip_comments(persistence)),
        (REWARDS, strip_comments(rewards)),
        (COLLECTOR, strip_comments(collector)),
    ):
        if "SaveFile(" in text:
            return fail(
                path.as_posix() + " 不得直接调 SavesSystem.SaveFile，"
                "物理落盘唯一入口是 DailyReportSaveCoordinator")

    if strip_comments(coordinator).count("SavesSystem.SaveFile(") != 1:
        return fail("协调器里 SaveFile 必须有且只有一次调用（每批至多一次物理落盘）")

    if "SavesSystem.IsSaving" not in coordinator:
        return fail("协调器必须在 IsSaving 时改走 deferred，不得强写")

    # ---- 5) 里程碑幂等：位掩码 + 先发后标记 ----
    if "PeriodClaimedMask" not in service:
        return fail("里程碑领取必须用位掩码做幂等")
    grant_index = service.find("TryGrantMilestone")
    mark_index = service.find("MarkMilestoneClaimed(result.PeriodSlot)")
    if grant_index < 0 or mark_index < 0 or grant_index > mark_index:
        return fail("必须先发奖成功再置领取掩码（先发后标记），否则发放失败会吞掉奖励")

    # ---- 6) 事件订阅成对退订 ----
    for text, path in ((persistence, PERSISTENCE), (collector, COLLECTOR)):
        adds = len(re.findall(r"\+=\s*Handle|\+=\s*On", text))
        removes = len(re.findall(r"-=\s*Handle|-=\s*On", text))
        if adds == 0:
            continue
        if adds != removes:
            return fail(
                path.as_posix() + " 的事件订阅与退订不成对（订阅 "
                + str(adds) + " 处，退订 " + str(removes) + " 处）")

    # Health 的两个静态事件由全局 hooks 统一注册，必须成对
    hooks = Path("Utilities/PlayerLifecycleRuntimeHooks.cs").read_text(encoding="utf-8")
    for handler in ("DailyReportStatsCollector.OnGlobalDead",
                    "DailyReportStatsCollector.OnGlobalHurt"):
        if hooks.count("+= " + handler) != 1 or hooks.count("-= " + handler) != 1:
            return fail("PlayerLifecycleRuntimeHooks 中 " + handler + " 必须成对订阅/退订")

    # ---- 7) 编译清单 ----
    for path in REQUIRED_SOURCES:
        entry = path.as_posix().replace("/", "\\")
        if entry not in compile_list:
            return fail(entry + " 未登记进 compile_official.bat")

    print("DailyReportPersistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
