"""Guard: HandleBossDeath 的「本波成员」分界线不变式。

背景（CR-2026-09-03-009）：
  HandleBossDeath 有三个调用点。OnEnemyDiedWithDamageInfo 里的两个已经先证明了成员身份，
  但 LootAndRewards 的掉落漏斗（OnBossBeforeSpawnLoot_LootAndRewards）走的是逐角色的
  BeforeCharacterSpawnLootOnDead 钩子，只验了「在不在 bossSpawnTimes 里」——任何走共享
  刷怪核心的 Boss 都满足。于是随机事件乱入 Boss 与 Mode D Boss 的死亡会被当成本波 Boss
  死亡，凭空推进波次（跳波），最后一波还会提前触发通关演出。

守卫的不变式：
  1. HandleBossDeath 内存在成员判定 IsCurrentWaveBossMember，且它是一道 return 闸。
  2. 分界线**之上**只有与「这只 Boss 死了」绑定的记账（去重 / 恢复退订 / 击杀成就），
     分界线**之下**才是与「本波进度」绑定的记账。顺序反了等于守卫失效。
  3. 成员判定 helper 必须同时看 modeDActive、currentWaveBosses、currentBoss、bossesPerWave。
     modeDActive 必须判在 helper 里而不是 HandleBossDeath 开头——Mode D 的 Boss 击杀成就
     只经 HandleBossDeath 的 CheckBossKillAchievementsOnce 计数，顶部早返会把它整条掐掉。
  4. 成员判定必须 fail-closed：catch 里不许 return true。少推一波有
     TryFixStuckWaveIfNoBossAlive 自愈，多推一波没有任何补救手段。
"""

from pathlib import Path
import re
import sys

ARENA = Path("WavesArena/WavesArena.cs")
LOOT = Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs")

GATE = "IsCurrentWaveBossMember(bossMain)"

# 分界线之上：与单次击杀绑定，任何来源的 Boss 都要做
ABOVE_GATE = (
    "countedDeadBosses.Add(bossMain);",
    "CheckBossKillAchievementsOnce(bossMain);",
)

# 分界线之下：与本波进度绑定，只有成员才允许触碰
BELOW_GATE = (
    "infiniteHellCashPool",
    "currentWaveBosses.RemoveAt",
    "defeatedEnemies++",
    "bossesInCurrentWaveRemaining",
    "ProceedAfterWaveFinished();",
)


def fail(message):
    print("WavesArenaBossMembershipGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    """去掉注释。所有断言都必须在去注释后的代码上做——本文件的方法体带有大段说明性注释，
    里面本就写着 modeDActive / ProceedAfterWaveFinished 这些符号名，
    带注释比会把「注释提到它」误判成「代码用了它」（反向验证实测漏判）。"""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def extract_method(text, signature):
    """按大括号配平截出方法体。找不到返回 None。"""
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
    for path in (ARENA, LOOT):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    arena = ARENA.read_text(encoding="utf-8", errors="ignore")

    # ---- 1) 分界线存在且是 return 闸 ----
    body = extract_method(arena, "private void HandleBossDeath(")
    if not body:
        return fail(ARENA.as_posix() + " 找不到 HandleBossDeath 方法体")
    body = strip_comments(body)

    gate_at = body.find(GATE)
    if gate_at < 0:
        return fail(
            ARENA.as_posix() + " 的 HandleBossDeath 缺少本波成员判定 " + GATE
            + "。掉落漏斗那条调用点没有成员证明，去掉它等于放任乱入 Boss / Mode D Boss 推波。")
    if not re.search(r"if\s*\(\s*!\s*IsCurrentWaveBossMember\s*\(\s*bossMain\s*\)\s*\)", body):
        return fail(
            ARENA.as_posix() + " 的成员判定不是 if (!IsCurrentWaveBossMember(bossMain)) 形态，"
            "无法确认它仍是一道 return 闸。")

    # ---- 2) 分界线上下的语句顺序 ----
    for token in ABOVE_GATE:
        at = body.find(token)
        if at < 0:
            return fail(ARENA.as_posix() + " 的 HandleBossDeath 缺少 " + token)
        if at > gate_at:
            return fail(
                "「" + token + "」跑到了成员判定之后。它与单次击杀绑定，必须在分界线之上，"
                "否则乱入 Boss / Mode D Boss 的击杀成就与去重会被一并掐掉"
                "（Mode D 的 Boss 击杀成就只经这里计数）。")

    for token in BELOW_GATE:
        at = body.find(token)
        if at < 0:
            return fail(ARENA.as_posix() + " 的 HandleBossDeath 缺少波次记账 " + token)
        if at < gate_at:
            return fail(
                "「" + token + "」跑到了成员判定之前。它与本波进度绑定，必须在分界线之下，"
                "否则非本波 Boss 仍会污染波次账。")

    # ---- 3) 成员判定 helper 的真相来源 ----
    helper = extract_method(arena, "private bool IsCurrentWaveBossMember(")
    if not helper:
        return fail(ARENA.as_posix() + " 找不到 IsCurrentWaveBossMember 方法体")
    helper = strip_comments(helper)
    for symbol, why in (
        ("modeDActive", "Mode D 有独立波次系统，且它会 SetBossRushRuntimeActive(true)，"
                        "必须在这里挡掉（放 HandleBossDeath 开头会掐掉 Mode D 的击杀成就）"),
        ("currentWaveBosses", "多 Boss 档的成员真相来源"),
        ("currentBoss", "单 Boss 档的成员真相来源；多 Boss 档也用作登记失败时的回落"),
        ("bossesPerWave", "两档的分流判据"),
    ):
        if symbol not in helper:
            return fail(
                "IsCurrentWaveBossMember 没有引用 " + symbol + "。" + why + "。")

    # ---- 4) fail-closed：两个 helper 的 catch 里都不许 return true ----
    # 逐层比对实际发生在 IsSameWaveBossInstance 里，只查 IsCurrentWaveBossMember 会漏
    #（反向验证实测：把它 catch 里的 DevLog 换成 return true，旧版守卫没转红）。
    matcher = extract_method(arena, "private bool IsSameWaveBossInstance(")
    if not matcher:
        return fail(ARENA.as_posix() + " 找不到 IsSameWaveBossInstance 方法体")
    matcher = strip_comments(matcher)

    for name, scope in (("IsCurrentWaveBossMember", helper), ("IsSameWaveBossInstance", matcher)):
        for catch_body in re.findall(r"catch\s*\([^)]*\)\s*\{(.*?)\}", scope, flags=re.S):
            if re.search(r"\breturn\s+true\s*;", catch_body):
                return fail(
                    name + " 的 catch 里出现 return true。比对异常必须 fail-closed："
                    "少推一波有 TryFixStuckWaveIfNoBossAlive 自愈，多推一波（跳波）没有补救手段。")

    # ---- 5) 掉落漏斗不得再靠名字启发式当唯一判据 ----
    loot = strip_comments(LOOT.read_text(encoding="utf-8", errors="ignore"))
    if "HandleBossDeath(bossMain, dmgInfo);" not in loot:
        return fail(
            LOOT.as_posix() + " 不再调用 HandleBossDeath，本守卫的前提已变，请同步更新守卫。")

    print("WavesArenaBossMembershipGuard: PASS（分界线就位，"
          + str(len(ABOVE_GATE)) + " 项在上，" + str(len(BELOW_GATE)) + " 项在下）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
