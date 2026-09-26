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

场景常驻曲（2026-09-26，天空岛）另守三条：
  6. Boss 曲停下时要先尝试接回场景曲，否则天空岛打完 Boss 就一直静音；
     宿主销毁的复位必须先撤场景租约，免得 StopBossBgm 在拆场时把曲子又接回来。
  7. 天空岛会话（SkyIslandAmbience）构造时获取租约、Dispose 时释放。
  8. 场景常驻曲要整趟循环，文件只许 ogg / wav：FMOD 读部分 mp3 会把时长估成两倍，
     循环时中间空白半首（ryw.mp3 实测 34.4 秒被读成 69.3 秒）。
"""
import json

from pathlib import Path
import re
import sys

COORDINATOR = Path("Audio/BossBgmCoordinator.cs")
MANAGER = Path("Audio/BossRushAudioManager.cs")
TABLE = Path("Audio/BossBgmTrackTable.cs")
AMBIENCE = Path("DebugAndTools/SkyIsland/SkyIslandAmbience.cs")
DATA = Path("Assets/Data/Audio/BgmTracks.json")


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
    for path in (COORDINATOR, MANAGER, TABLE):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    coord = strip_comments(COORDINATOR.read_text(encoding="utf-8", errors="ignore"))
    manager = strip_comments(MANAGER.read_text(encoding="utf-8", errors="ignore"))
    table = strip_comments(TABLE.read_text(encoding="utf-8"))
    if "BossBgmTrackTable.TryParse(json" not in coord or "JsonUtility.FromJson" in coord:
        return fail("曲目表必须经显式 token 数组解析，不能回退实机产出空表的 JsonUtility 路径")
    for token in ('BossRushJsonParser.TryParse', '"bossTracks"', '"stingers"', '"jukebox"',
                  "table.bossTracks = bosses.ToArray()", "table.stingers = events.ToArray()",
                  "table.jukebox = music.ToArray()", "bool loop = true"):
        if token not in table:
            return fail("BossBgmTrackTable 缺少解析契约: " + token)

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

    # ---- 6) 场景常驻曲：Boss 曲停下接回，复位先撤租约 ----
    if '"sceneTracks"' not in table or "table.sceneTracks = scenes.ToArray()" not in table:
        return fail(TABLE.as_posix() + " 缺少 sceneTracks 解析（场景常驻曲的数据契约）")
    stop_body = stop[stop.find("_playingBossKey = null;", stop.find("IsPlaybackFromCurrentScene()")):]
    if "if (!TryResumeSceneBgm()) InvokeStopBgm();" not in stop_body:
        return fail(COORDINATOR.as_posix() + " 的 StopBossBgm 没有先尝试接回场景曲：天空岛打完 Boss 会一直静音")
    reset = extract_method(coord, "internal static void ResetStaticCaches()")
    if not reset or reset.find("ClearSceneLease();") < 0 or reset.find("ClearSceneLease();") > reset.find("StopBossBgm();"):
        return fail(COORDINATOR.as_posix() + " 的 ResetStaticCaches 必须先 ClearSceneLease 再 StopBossBgm，否则拆场时场景曲会被接回")
    for token in ("internal static bool AcquireSceneBgm(string sceneKey, UnityEngine.Object owner)",
                  "internal static void ReleaseSceneBgm(UnityEngine.Object owner)",
                  "_sceneLeaseSceneHandle", "_sceneLeaseOwnerId != owner.GetInstanceID()"):
        if token not in coord:
            return fail(COORDINATOR.as_posix() + " 场景常驻曲租约缺少: " + token)

    # ---- 7) 天空岛会话获取 / 释放 ----
    if not AMBIENCE.is_file():
        return fail("找不到 " + AMBIENCE.as_posix())
    amb = strip_comments(AMBIENCE.read_text(encoding="utf-8", errors="ignore"))
    ctor = extract_method(amb, "internal SkyIslandAmbience(GameObject root)")
    dispose = extract_method(amb, "public void Dispose()")
    if "BossBgmCoordinator.AcquireSceneBgm(BossBgmScenes.SkyIsland" not in ctor:
        return fail(AMBIENCE.as_posix() + " 构造时没有获取天空岛场景常驻曲")
    if "BossBgmCoordinator.ReleaseSceneBgm(bgmOwner)" not in dispose:
        return fail(AMBIENCE.as_posix() + " Dispose 没有释放场景常驻曲：离岛后曲子会一直放")

    # ---- 8) 场景常驻曲只许 ogg / wav ----
    if not DATA.is_file():
        return fail("找不到 " + DATA.as_posix())
    data = json.loads(DATA.read_text(encoding="utf-8"))
    scenes = data.get("sceneTracks") or []
    if not any(r.get("sceneKey") == "SkyIsland" for r in scenes):
        return fail(DATA.as_posix() + " 缺少 sceneKey=SkyIsland 的场景常驻曲")
    for row in scenes:
        f = str(row.get("file", "")).lower()
        if not (f.endswith(".ogg") or f.endswith(".wav")):
            return fail(DATA.as_posix() + " 场景常驻曲 " + f + " 不是 ogg / wav：FMOD 读 mp3 可能把时长估成两倍，循环中间空白")
        if row.get("loop") is False:
            return fail(DATA.as_posix() + " 场景常驻曲 " + f + " 必须循环")

    print("BossBgmCoordinatorGuard: PASS（零素材零行为 + 按 Boss 甄别 + 跨局自愈 + 场景常驻曲）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
