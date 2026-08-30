"""Guard: 随机事件运行时模块的单实例纪律与 dormant 契约。

背景：仓库对新模块有一套固化范式（Mode H / 遗种巢 / 日报三次踩坑后定下来的）：
模块实例只能有一份，由 ModBehaviour 持有字段、再把**同一个引用**注册给 host；
调度器状态（冷却、单局计数、当前事件）全在这份实例上，二次 new 会让
「已触发次数」之类的计数分裂，出现一局刷出双倍事件。

另外总开关是运行时可变的（ModConfig 单键回调），所以：
  - 关闭时必须整体 dormant：不计时、不抽取、不生成、不订阅；
  - 关掉再打开要能当帧复活，不能等到下次切图；
  - 关闭状态下每帧成本必须是 O(1) 早返（AGENTS.md 4.12）。

守卫内容：
  1. 模块在注册表里走「先存字段、再注册同一引用」，且暴露只读门面。
  2. 全仓库对 RandomEventsRuntimeModule 只有一次 new。
  3. 模块具备幂等 bootstrap 与开关关闭时的 shutdown 回落。
  4. 每帧 tick 路径上没有日志（热路径零噪声）。
"""

from pathlib import Path
import re
import sys

MODULE = Path("RandomEvents/RandomEventsRuntimeModule.cs")
REGISTRATION = Path("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
DIRECTOR = Path("RandomEvents/RandomEventDirector.cs")


def fail(message):
    print("RandomEventsRuntimeModuleGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    for path in (MODULE, REGISTRATION, DIRECTOR):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    module = strip_comments(MODULE.read_text(encoding="utf-8", errors="ignore"))
    reg = strip_comments(REGISTRATION.read_text(encoding="utf-8", errors="ignore"))

    # ---- 1) 单实例纪律：先存字段再注册同一引用 ----
    if not re.search(r"randomEventsRuntime\s*=\s*new\s+RandomEventsRuntimeModule\s*\(", reg):
        return fail(
            REGISTRATION.as_posix() + " 没有把实例先存进字段。"
            "必须「先存字段、再注册同一引用」，否则调度器状态会分裂。")
    if not re.search(r"Register\s*\(\s*randomEventsRuntime\s*\)", reg):
        return fail(
            REGISTRATION.as_posix() + " 注册给 host 的不是字段里那份实例。"
            "直接 Register(new ...) 会产生第二份调度器状态。")
    if not re.search(r"RandomEventsRuntimeModule\s+RandomEventsRuntime\s*\{\s*get", reg):
        return fail(REGISTRATION.as_posix() + " 缺少只读门面 RandomEventsRuntime")

    # ---- 2) 全仓库只有一次 new ----
    news = []
    for path in Path(".").rglob("*.cs"):
        if any(part in {".git", "Build", "鸭科夫源码"} for part in path.parts):
            continue
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        news += [path.as_posix()] * len(
            re.findall(r"new\s+RandomEventsRuntimeModule\s*\(", text))
    if len(news) != 1:
        return fail(
            "RandomEventsRuntimeModule 被 new 了 " + str(len(news)) + " 次（"
            + ", ".join(sorted(set(news))) + "）。单实例纪律要求全仓库只有一次。")

    # ---- 3) dormant 契约 ----
    if "EnsureBootstrapped" not in module:
        return fail(MODULE.as_posix() + " 缺少幂等 bootstrap 入口 EnsureBootstrapped")
    if not re.search(r"Shutdown\w*\(", module):
        return fail(
            MODULE.as_posix() + " 缺少开关关闭时的 shutdown 回落。"
            "总开关运行时可关，关掉后必须整体 dormant：不计时、不抽取、不生成。")
    if "IsRandomEventsConfiguredEnabled" not in module:
        return fail(
            MODULE.as_posix() + " 没有经唯一只读入口 IsRandomEventsConfiguredEnabled 读开关")

    # ---- 4) 每帧路径零日志 ----
    update = re.search(r"public\s+override\s+void\s+OnUpdate\s*\([^)]*\)\s*\{(.*?)\n        \}",
                       module, flags=re.S)
    if update and re.search(r"DevLog|Debug\.Log", update.group(1)):
        return fail(
            MODULE.as_posix() + " 的 OnUpdate 里有日志调用。"
            "这是每帧路径，日志会在长局里刷爆 Player.log（AGENTS.md 4.7）。")

    print("RandomEventsRuntimeModuleGuard: PASS（单实例 + dormant + 热路径零日志）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
