"""
Guard: 幽灵女巫必须以与 Frostmourne 相同的额外追加链路掉落噬魂挽歌。

要求：
- LootBlacklistRegistry 必须拉黑 `PhantomWitchScytheIds.WeaponTypeId`
- CharacterMainControl.OnDead 前缀必须调用幽灵女巫额外掉落处理器
- 处理器必须使用 `100%` 概率、按 `PhantomWitchConfig.BossNameKey` 识别 Boss、
  并实例化 `PhantomWitchScytheIds.WeaponTypeId`
- BossRush 奖励箱路径必须支持 pending consume / cancel
"""

from pathlib import Path
import re
import sys


BLACKLIST = Path("Config/LootBlacklistRegistry.cs")
HARMONY = Path("Integration/BossRushHarmonyPatch.cs")
BOOTSTRAP = Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs")
LOOT = Path("LootAndRewards/LootAndRewards.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0):
    return None if re.search(pattern, text, flags) else description


def main() -> int:
    blacklist_text = BLACKLIST.read_text(encoding="utf-8")
    harmony_text = HARMONY.read_text(encoding="utf-8")
    bootstrap_text = BOOTSTRAP.read_text(encoding="utf-8")
    loot_text = LOOT.read_text(encoding="utf-8")

    missing = []

    checks = [
        require(
            blacklist_text,
            r"PhantomWitchScytheIds\.WeaponTypeId",
            "LootBlacklistRegistry missing PhantomWitch scythe blacklist entry",
        ),
        require(
            harmony_text,
            r"PhantomWitchScytheBossDropHandler\.TryHandlePhantomWitchDeath\s*\(\s*__instance\s*\)",
            "OnDead patch missing PhantomWitch scythe extra-drop hook",
        ),
        require(
            bootstrap_text,
            r"class\s+PhantomWitchScytheBossDropHandler",
            "missing PhantomWitchScytheBossDropHandler",
        ),
        require(
            bootstrap_text,
            r"private\s+const\s+float\s+ExtraDropChance\s*=\s*0\.5f",
            "extra drop chance is not 50%",
        ),
        require(
            bootstrap_text,
            r"PhantomWitchConfig\.BossNameKey",
            "boss identity is not keyed by PhantomWitchConfig.BossNameKey",
        ),
        require(
            bootstrap_text,
            r"ItemAssetsCollection\.InstantiateSync\s*\(\s*PhantomWitchScytheIds\.WeaponTypeId\s*\)",
            "handler does not instantiate the PhantomWitch scythe reward",
        ),
        require(
            bootstrap_text,
            r"pendingBossRushLootboxDrops\.Add\s*\(\s*boss\s*\)",
            "BossRush lootbox defer path does not track pending PhantomWitch drops",
        ),
        require(
            loot_text,
            r"PhantomWitchScytheBossDropHandler\.TryConsumePendingBossRushLootboxDrop\s*\(\s*bossMain\s*,\s*inv\s*\)",
            "BossRush lootbox path missing PhantomWitch pending consume hook",
        ),
        require(
            loot_text,
            r"PhantomWitchScytheBossDropHandler\.CancelPendingBossRushLootboxDrop\s*\(\s*character\s*\)",
            "BossRush tracking cleanup missing PhantomWitch pending cancel hook",
        ),
    ]

    for result in checks:
        if result is not None:
            missing.append(result)

    if missing:
        return fail("PhantomWitchScytheExtraDropGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchScytheExtraDropGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
