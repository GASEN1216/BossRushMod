#!/usr/bin/env python3
"""NonWaveBossSpawnGuard — 非本波生成的自定义 Boss 不得登记波次身份。

三个自定义 Boss 有各自的专用生成器，而 `EnemySpawnCore` 会把对应 preset 路由过去。
生成器里 `currentBoss = character` 与 `currentWaveBosses.Add` 是本波身份的**唯一**来源，
`WavesArena.IsCurrentWaveBossMember` 就信这两个容器。

随机事件「Boss 乱入」从 `GetFilteredEnemyPresets()` 取池，池里**含**三个自定义 Boss。
若生成器无条件写身份容器，乱入者会顶掉本波真 Boss：
  - 单 Boss 档：真 Boss 击杀不再推波；
  - 乱入者被销毁后 `TryFixStuckWaveIfNoBossAlive` 读到「无存活 Boss」，反而主动推波。
两条都是玩家侧的永久卡波 / 跳波。

龙裔早有同语义的 `isChildProtectionSummon` 门控先例，本守卫要求三者齐备，
并要求随机事件桥确实把标志透传下去。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SPAWNERS = [
    (os.path.join(ROOT, "Integration", "DragonKing", "DragonKingBoss.cs"),
     r"public async UniTask<CharacterMainControl> SpawnDragonKing\(", "SpawnDragonKing"),
    (os.path.join(ROOT, "Integration", "PhantomWitch", "PhantomWitchBoss.cs"),
     r"public async UniTask<CharacterMainControl> SpawnPhantomWitch\(", "SpawnPhantomWitch"),
    (os.path.join(ROOT, "Integration", "DragonDescendant", "DragonDescendantBoss.cs"),
     r"public async UniTask<CharacterMainControl> SpawnDragonDescendant\(", "SpawnDragonDescendant"),
]

CORE = os.path.join(ROOT, "Utilities", "EnemySpawnCore.cs")
BRIDGE = os.path.join(ROOT, "RandomEvents", "RandomEventEffectsBridge_Spawn.cs")


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def main():
    errors = []

    for path, sig, name in SPAWNERS:
        src = read(path)
        if src is None:
            errors.append("[Spawner] 缺少 " + os.path.basename(path))
            continue

        m = re.search(sig + r"[\s\S]{0,600}?\)", src)
        if m is None:
            errors.append("[Spawner] 找不到 " + name + " 的签名")
        elif not re.search(r"\bisNonWaveSpawn\b", m.group(0)):
            errors.append("[Spawner] " + name + " 必须有 isNonWaveSpawn 参数（非本波生成门控）")

        # 身份写入必须被门控住：currentBoss 赋值所在的行之前要出现门控条件
        assign = re.search(r"currentBoss = character;", src)
        if assign is None:
            errors.append("[Spawner] " + name + " 找不到 currentBoss 赋值")
            continue
        head = src[:assign.start()]
        # 取赋值前最近的 600 字符，检查门控存在
        window = head[-600:]
        if (not re.search(r"\bisNonWaveSpawn\b", window)
                and not re.search(r"\bisChildProtectionSummon\b", window)):
            errors.append(
                "[Spawner] " + name + " 的 currentBoss 赋值必须置于非本波门控之内")

    core = read(CORE)
    if core is None:
        errors.append("[Core] 缺少 EnemySpawnCore.cs")
    else:
        if "SuppressWaveBossRegistration" not in core:
            errors.append("[Core] EnemySpawnCoreOptions 必须提供 SuppressWaveBossRegistration")
        # 三处路由都要透传
        routed = len(re.findall(
            r"isNonWaveSpawn: options != null && options\.SuppressWaveBossRegistration", core))
        if routed < 3:
            errors.append(
                "[Core] 三个自定义 Boss 的路由都必须透传 isNonWaveSpawn，当前只有 {0} 处".format(routed))

    bridge = read(BRIDGE)
    if bridge is None:
        errors.append("[Bridge] 缺少 RandomEventEffectsBridge_Spawn.cs")
    elif "SuppressWaveBossRegistration = true" not in bridge:
        errors.append(
            "[Bridge] 随机事件乱入必须设 SuppressWaveBossRegistration = true，"
            "否则抽中自定义 Boss 时会顶掉本波身份")

    if errors:
        print("NonWaveBossSpawnGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("NonWaveBossSpawnGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
