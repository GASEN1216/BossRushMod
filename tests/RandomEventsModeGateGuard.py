"""Guard: 局内随机事件的模式门控必须 fail-closed 且只走公开门面。

背景：Mod 有 9 个玩法入口，随机事件只在「标准 BossRush / 无间炼狱 / Mode D」
三个波次结构简单的入口启用。其余入口各有不可打扰的理由：
  - Mode E 多阵营 + 扫箱令，乱入 Boss 的阵营归属语义不清；
  - Mode F 悬赏雷达只认注册过的 Boss，且阶段播报已很密集；
  - Mode G 是固定九波编排 + 严格奖励事务，运行期连 Legacy 波次 tick 都被整体冻结；
  - Mode H 是观战 + arena isolation lease 独占清场；
  - 丧尸模式有独立生命周期与奖励体系（AGENTS.md 4.11 的边界）；
  - 普通撤离图上官方 spawner / 任务 / 天气都活跃。

门控一旦漏判，后果是在别人的状态机里凭空生成 Boss 或强制改天气，属于跨模式事故。

守卫内容：
  1. 门控判定必须**先**排除禁入模式，再看允许名单（顺序反了会让共存标志漏网）。
  2. 禁入名单里的每个模式都必须被显式判到，一个都不能少。
  3. 只能经公开门面判定，禁止引用别的模式的内部闸门符号——那些是私有实现，
     直接引用会把两个模式的生命周期焊死。
  4. 判定必须 no-throw：任何异常都要落到「不启用」，绝不能因为读状态失败而放行。
"""

from pathlib import Path
import re
import sys

GATE = Path("RandomEvents/RandomEventModeGate.cs")

# 必须被显式排除的模式（门面名 -> 说明）
REQUIRED_EXCLUSIONS = (
    ("IsModeEActive", "Mode E 划地为营：多阵营 + 扫箱令，乱入阵营语义不清"),
    ("IsModeFActive", "Mode F 血猎追击：悬赏雷达只认注册 Boss，播报会打架"),
    ("IsModeGRunInProgressSafe", "Mode G 宿命回响：固定九波编排 + 严格奖励事务"),
    ("IsModeHRunInProgressSafe", "Mode H 斗蛐蛐：观战 + arena isolation 独占清场"),
    ("IsZombieModeActive", "丧尸模式：独立生命周期与奖励体系（4.11）"),
)

# 别的模式的内部闸门：门控只能走公开门面，不得引用这些
FORBIDDEN_INTERNALS = (
    "ModeGRuntimeGates",
    "ModeHRuntimeGates",
    "ZombieModePhaseGuards",
)


def fail(message):
    print("RandomEventsModeGateGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    if not GATE.is_file():
        return fail("找不到模式门控 " + GATE.as_posix())
    code = strip_comments(GATE.read_text(encoding="utf-8", errors="ignore"))

    # ---- 1) 禁入名单完整 ----
    for facade, why in REQUIRED_EXCLUSIONS:
        if facade not in code:
            return fail(
                "门控没有排除 " + facade + "（" + why + "）。"
                "漏一个就会在别人的状态机里生成 Boss 或强制改天气。")

    # ---- 2) 禁入判定必须在允许判定之前 ----
    exclusion_positions = [code.index(f) for f, _ in REQUIRED_EXCLUSIONS if f in code]
    allow_match = re.search(r"\bIsModeDActive\b", code)
    if allow_match and exclusion_positions and allow_match.start() < min(exclusion_positions):
        return fail(
            "允许名单（IsModeDActive）出现在禁入名单之前。顺序必须是"
            "「先排除后允许」，否则多个模式标志共存时会漏网放行。")

    # ---- 3) 不得引用别的模式的内部符号 ----
    for symbol in FORBIDDEN_INTERNALS:
        if symbol in code:
            return fail(
                "门控引用了内部闸门 " + symbol + "。只能经公开门面判定，"
                "直接引用会把两个模式的生命周期焊死。")

    # ---- 4) no-throw / fail-closed ----
    if "catch" not in code:
        return fail(
            "门控没有任何 catch。判定必须 no-throw：读状态失败要落到「不启用」，"
            "绝不能因为一次异常就把事件放进 Mode G/H 或丧尸局里。")
    if not re.search(r"catch[^{]*\{[^}]*return\s+false", code, flags=re.S):
        return fail("门控的 catch 分支没有 return false，未做到 fail-closed")

    print("RandomEventsModeGateGuard: PASS（禁入 "
          + str(len(REQUIRED_EXCLUSIONS)) + " 个模式，fail-closed）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
