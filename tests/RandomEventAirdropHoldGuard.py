"""Guard: E1 空投补给「翻箱保护」的有界性与热路径纪律。

背景（CR-2026-09-03-010）：
  空投箱由 ctx.Scope 托管，事件到时（45s）即销毁，不看玩家是否正开着它。
  一场 Boss 战常超过 45 秒，权重最高的事件因此经常连带里面的东西一起凭空消失；
  玩家正开着界面时还会留下一个指着已销毁目标的面板。

  修法的关键是**延到期而不是延清理**：RandomEventDirector.EndActiveEvent 在
  evt.OnCleanup 之后无条件再跑一次 ctx.Scope.Clear，在 OnCleanup 里放行毫无意义。

守卫的不变式：
  1. 续期宽限与硬帽两个常量落在 RandomEventsTuning（随机事件数值唯一落点），
     且 0 < grace < cap <= 300——挡住「把上限调成天文数字」这种等价于取消上限的改法。
  2. OnTick 真的读了硬帽。并发恒 1，没有上限玩家挂着界面就能把整局事件钉死在 EventActive。
  3. OnTick 是每帧热路径：无日志、无分配、无组件查找、不读 crate.Inventory（非纯 getter）。
  4. OnCleanup 里「关界面」必须排在 ClearScope 之前——ClearScope 会 Destroy 箱子。
  5. EndActiveEvent 的兜底 ctx.Scope.Clear 仍然无条件执行：
     只有 Expired 一条路径能被延后，其余六种结束原因必须照旧强制销毁。
"""

from pathlib import Path
import re
import sys

TUNING = Path("RandomEvents/RandomEventsTuning.cs")
HOLD = Path("RandomEvents/RandomEventAirdropHold.cs")
CATALOG = Path("RandomEvents/RandomEventCatalog.cs")
DIRECTOR = Path("RandomEvents/RandomEventDirector.cs")
COMPILE_LIST = Path("compile_official.bat")

GRACE = "AirdropHoldOpenGraceSeconds"
CAP = "AirdropHoldOpenMaxSeconds"

# OnTick 每帧执行，这些一个都不许出现
HOT_PATH_FORBIDDEN = (
    ("DevLog", "每帧日志会刷爆 Player.log（AGENTS 4.7）"),
    ("Debug.Log", "同上"),
    ("GetComponent", "每帧组件查找（AGENTS 4.12）"),
    ("FindObjectsOfType", "每帧全场扫描"),
    ("GameObject.Find", "每帧全场扫描"),
    (".Inventory", "InteractableLootbox.Inventory 不是纯 getter（惰性建仓 + PopText + LogError）"),
    ("foreach", "每帧枚举会产生 GC 分配"),
    ("new ", "每帧分配"),
)


def fail(message):
    print("RandomEventAirdropHoldGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def extract_method(text, signature):
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def main():
    for path in (TUNING, HOLD, CATALOG, DIRECTOR, COMPILE_LIST):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    tuning = TUNING.read_text(encoding="utf-8", errors="ignore")
    hold = HOLD.read_text(encoding="utf-8", errors="ignore")
    catalog = CATALOG.read_text(encoding="utf-8", errors="ignore")
    director = DIRECTOR.read_text(encoding="utf-8", errors="ignore")

    # ---- 1) 两个常量在 tuning 里，且数值有界 ----
    values = {}
    for name in (GRACE, CAP):
        match = re.search(
            r"internal\s+const\s+float\s+" + name + r"\s*=\s*([0-9]*\.?[0-9]+)f?\s*;", tuning)
        if not match:
            return fail(
                TUNING.as_posix() + " 缺少 internal const float " + name
                + "。随机事件的全部数值必须落在这个文件，禁止在事件层写魔法数字。")
        values[name] = float(match.group(1))

    if not (0.0 < values[GRACE] < values[CAP] <= 300.0):
        return fail(
            "续期常量越界：" + GRACE + "=" + str(values[GRACE])
            + ", " + CAP + "=" + str(values[CAP])
            + "。要求 0 < grace < cap <= 300——上限过大等价于取消上限，"
            "玩家挂着战利品界面就能把整局随机事件钉死在 EventActive。")

    # ---- 2) OnTick 读了硬帽 ----
    tick = extract_method(hold, "internal override void OnTick(")
    if not tick:
        return fail(HOLD.as_posix() + " 找不到 OnTick 覆写")
    if CAP not in tick:
        return fail(
            "OnTick 没有引用 " + CAP + "，续期就是无界的。并发恒 1，"
            "无界续期会让本局后续事件全部不再触发。")
    # 必须是「额度耗尽就早返」这一形态，不能只在别处提一句常量名。
    # 只查 CAP 是否出现太弱：把早返换成 if(false) 后常量仍出现在下面的 budget 计算里，
    # 旧版守卫没转红（反向验证实测）。
    if not re.search(
            r"if\s*\(\s*_holdExtendedSeconds\s*>=\s*RandomEventsTuning\." + CAP + r"\s*\)\s*return\s*;",
            tick):
        return fail(
            "OnTick 缺少「额度耗尽即早返」的硬帽判定 "
            "if (_holdExtendedSeconds >= RandomEventsTuning." + CAP + ") return;。"
            "少了它，budget 会被算成 0 或负数并加进 ctx.DurationSeconds，反而把事件提前掐断。")
    if not re.search(r"catch\s*\(", tick):
        return fail(
            "OnTick 缺少 try/catch。RandomEventDirector._activeTickFaulted 一旦置位就永久"
            "停调本事件的 OnTick，抛出一次等于整个保护失效。")

    # ---- 3) 热路径纪律 ----
    tick_code = strip_comments(tick)
    for token, why in HOT_PATH_FORBIDDEN:
        if token in tick_code:
            return fail("OnTick 出现 " + repr(token) + "：" + why + "。")

    # ---- 4) 关界面必须先于 ClearScope ----
    cleanup = extract_method(catalog, "internal override void OnCleanup(")
    if not cleanup:
        return fail(CATALOG.as_posix() + " 找不到空投事件的 OnCleanup")
    # 必须先去注释再比位置：方法体里的说明性注释本身就写着 ClearScope，
    # 带注释比会把「注释提到它」误判成「调用它」。
    cleanup = strip_comments(cleanup)
    close_at = cleanup.find("CloseAirdropLootViewIfOpen()")
    clear_at = cleanup.find("ClearScope")
    if close_at < 0:
        return fail(
            "空投 OnCleanup 没有关界面。ClearScope 会 Destroy 箱子，"
            "界面会留在屏幕上指着一个已销毁的目标。")
    if clear_at < 0 or close_at > clear_at:
        return fail(
            "空投 OnCleanup 里 CloseAirdropLootViewIfOpen 必须排在 ClearScope 之前，"
            "否则箱子已经被销毁才去关界面。")

    # ---- 5) 兜底销毁仍然无条件 ----
    end_active = extract_method(director, "private void EndActiveEvent(")
    if not end_active:
        return fail(DIRECTOR.as_posix() + " 找不到 EndActiveEvent")
    if "ctx.Scope.Clear(reason.ToString())" not in end_active:
        return fail(
            "EndActiveEvent 的兜底 ctx.Scope.Clear 不见了。只有 Expired 一条路径允许被延后，"
            "RunEnded / SceneChanged / SwitchDisabled / HostDestroyed / DebugForced / "
            "TriggerFailed 必须照旧强制销毁，否则会跨图跨局泄漏生成物。")
    if re.search(r"if\s*\(\s*reason\s*[=!]=", end_active):
        return fail(
            "EndActiveEvent 出现按 reason 分支的清理。清理必须对全部结束原因一视同仁，"
            "延后只能发生在到期判据那一侧。")

    # ---- 6) 新文件已登记进编译清单（AGENTS 4.1）----
    compile_list = COMPILE_LIST.read_text(encoding="utf-8", errors="ignore")
    if "RandomEventAirdropHold.cs" not in compile_list:
        return fail(
            COMPILE_LIST.as_posix() + " 没有登记 RandomEvents\\RandomEventAirdropHold.cs。"
            "源码存在但不会被编译进 DLL，且不会报错。")

    print("RandomEventAirdropHoldGuard: PASS（grace=" + str(values[GRACE])
          + "s, cap=" + str(values[CAP]) + "s，热路径干净，关界面先于销毁）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
