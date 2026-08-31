"""Guard: Boss BGM 协调器的「零素材零行为」承诺与三条易碎不变式。

这套系统的核心承诺是**代码一次到位、正式曲目后补零改动**，因此守的不是功能，
而是「没有素材时不能有任何行为变化」以及三个具体的踩坑点：

  1. **龙王旧路径不能被无条件夺走**。龙王原本用 dragonking.mp3 走 PostCustomSFX
     （一次性音效，不占 bgmSource）。只有曲目表里真的配了 DragonKing 条目时才接管；
     没配就必须原样保留旧行为，否则「没做素材」反而让龙王战变哑。

  2. **停止必须按 bossKey 甄别**。多 Boss 波次里 A 的死亡回调如果无条件 StopBGM，
     会掐掉 B 刚起播的曲子。StopBossBgm 必须先比对当前在放的是不是它。

  3. **播放记账要能跨局自愈**。玩家中途弃局时 Boss 不会死、死亡回调不会走，
     _playingBossKey 就残留了；下一局同一个 Boss 会被误判成「已经在放」而永不起播，
     表现为战斗静音。靠比对场景 handle 自愈，不新增全局订阅。

另外 PlayCustomBGM 的返回类型是 FMOD 类型，本程序集没引用 FMOD，
只能 MethodInfo.Invoke 并忽略返回值——不许改成 CreateDelegate（编译期就会炸）。
"""

from pathlib import Path
import re
import sys

COORDINATOR = Path("Audio/BossBgmCoordinator.cs")
MANAGER = Path("Audio/BossRushAudioManager.cs")


def fail(message):
    print("BossBgmCoordinatorGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def extract_method(text, signature):
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return ""


def main():
    for path in (COORDINATOR, MANAGER):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    coord = strip_comments(COORDINATOR.read_text(encoding="utf-8", errors="ignore"))
    manager = strip_comments(MANAGER.read_text(encoding="utf-8", errors="ignore"))

    # ---- 1) 龙王旧路径保留 ----
    play_dk = extract_method(manager, "public void PlayDragonKingBGM()")
    if not play_dk:
        return fail(MANAGER.as_posix() + " 找不到 PlayDragonKingBGM 方法体")
    if "BossBgmCoordinator.PlayBossBgm" not in play_dk:
        return fail(
            MANAGER.as_posix() + " 的 PlayDragonKingBGM 没有先尝试曲目表路径。")
    if "dragonking.mp3" not in play_dk:
        return fail(
            MANAGER.as_posix() + " 的 PlayDragonKingBGM 丢掉了 dragonking.mp3 旧路径。"
            "曲目表没有 DragonKing 条目时必须维持旧行为，"
            "否则「还没做素材」会让龙王战直接变哑——这是本系统零素材零行为承诺的反例。")

    reset_dk = extract_method(manager, "public void ResetDragonKingBGMState()")
    if not reset_dk or "StopBossBgm" not in reset_dk:
        return fail(
            MANAGER.as_posix() + " 的 ResetDragonKingBGMState 没有停止协调器 BGM，"
            "接管后龙王死了曲子还在放。")

    # ---- 2) 停止按 bossKey 甄别 ----
    stop = extract_method(coord, "internal static void StopBossBgm(")
    if not stop:
        return fail(COORDINATOR.as_posix() + " 找不到 StopBossBgm 方法体")
    if "_playingBossKey" not in stop or "string.Equals(_playingBossKey" not in stop:
        return fail(
            COORDINATOR.as_posix() + " 的 StopBossBgm 没有比对当前在放的 Boss。"
            "多 Boss 波次里，A 的死亡回调会掐掉 B 刚起播的曲子。")

    # ---- 3) 跨局自愈 ----
    if "_playingSceneHandle" not in coord:
        return fail(
            COORDINATOR.as_posix() + " 缺少 _playingSceneHandle 跨局自愈机制。"
            "玩家中途弃局时 Boss 不死、死亡回调不走，播放记账会残留，"
            "下一局同一个 Boss 被误判为「已在放」而永不起播（战斗静音）。")
    play = extract_method(coord, "internal static bool PlayBossBgm(")
    if not play:
        return fail(COORDINATOR.as_posix() + " 找不到 PlayBossBgm 方法体")
    if "IsPlaybackFromCurrentScene()" not in play:
        return fail(
            COORDINATOR.as_posix() + " 的 PlayBossBgm 去重没有校验场景，"
            "跨局残留记账会让下一局静音。")

    # ---- 4) 反射调用不许改成 CreateDelegate ----
    if "CreateDelegate" in coord and "PlayCustomBGM" in coord:
        return fail(
            COORDINATOR.as_posix() + " 疑似把 PlayCustomBGM 改成了 CreateDelegate。"
            "它的返回类型是 FMOD.Studio.EventInstance?，本程序集没有引用 FMOD，"
            "委托类型写不出来；必须 MethodInfo.Invoke 并忽略返回值。")
    if "new Type[] { typeof(string), typeof(bool) }" not in coord:
        return fail(
            COORDINATOR.as_posix() + " 没有按 (string, bool) 精确匹配 PlayCustomBGM。"
            "该方法第二参带默认值，反射 Invoke 必须显式补齐两个实参。")

    # ---- 5) 素材缺失必须静默跳过 ----
    for token in ("FileExists", "HasBossTrack", "ResetStaticCaches"):
        if token not in coord:
            return fail(COORDINATOR.as_posix() + " 缺少 " + token)

    print("BossBgmCoordinatorGuard: PASS（零素材零行为 + 按 Boss 甄别 + 跨局自愈）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
