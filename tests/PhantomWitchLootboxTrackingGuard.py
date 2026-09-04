"""
Guard: Phantom Witch custom spawn path must participate in shared BossRush lootbox tracking.

Reason:
- generic bosses call RegisterBossRandomLootTracking(), which marks reward-box defer state
- Phantom Witch uses a custom spawn path, so it must reuse the same shared helper and finalize path
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchBoss.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    spawn_block = extract_block(
        text,
        "public async UniTask<CharacterMainControl> SpawnPhantomWitch(",
    )
    if not spawn_block:
        return fail("PhantomWitchLootboxTrackingGuard: missing SpawnPhantomWitch block")

    if "RegisterBossRandomLootTracking(character, originalLootCount, 0f);" not in spawn_block:
        return fail("PhantomWitchLootboxTrackingGuard: SpawnPhantomWitch does not reuse RegisterBossRandomLootTracking(..., 0f)")

    # 真正的清理路径：没有掉落管线在跑，必须两件都做。
    cleanup_targets = [
        "private void CleanupFailedPhantomWitchSpawn(CharacterMainControl character)",
        "private void CleanupTrackedPhantomWitchCharacter(",
    ]

    # 死亡回调是**例外**：它在死亡帧同步执行，而四家 defer 掉落
    # （噬魂挽歌/遗种蛋/词缀熔石/寒霜长矛）的消费点在
    # AddBossSpecialLootToLootboxCoroutine 里、排在 `while (inv.Loading)
    # yield WaitForSeconds(0.1f)` 之后。在这里 Finalize 会把它们全部 CancelPending，
    # 女巫的特殊掉落在标准 BossRush 奖励箱路径上必丢。
    # 收尾由协程自己的 try/finally 与其余七个 Finalize 调用点负责。
    death_block = extract_block(
        text,
        "private void OnPhantomWitchDeath(CharacterMainControl deadWitch, DamageInfo damageInfo)",
    )
    if not death_block:
        return fail("PhantomWitchLootboxTrackingGuard: missing OnPhantomWitchDeath block")
    if "FinalizeBossRushLootboxPathTracking(" in death_block:
        return fail(
            "PhantomWitchLootboxTrackingGuard: OnPhantomWitchDeath 不得调用 "
            "FinalizeBossRushLootboxPathTracking——它会抢在掉落协程消费之前取消 pending")
    if "ClearBossRandomLootTracking(" not in death_block:
        return fail(
            "PhantomWitchLootboxTrackingGuard: OnPhantomWitchDeath 仍须 ClearBossRandomLootTracking")

    for signature in cleanup_targets:
        block = extract_block(text, signature)
        if not block:
            return fail(f"PhantomWitchLootboxTrackingGuard: missing cleanup block {signature}")
        if "FinalizeBossRushLootboxPathTracking(" not in block and "FinalizeBossRushLootboxPathTracking," not in block:
            return fail(f"PhantomWitchLootboxTrackingGuard: cleanup block missing FinalizeBossRushLootboxPathTracking -> {signature}")
        if "ClearBossRandomLootTracking(" not in block and "ClearBossRandomLootTracking," not in block:
            return fail(f"PhantomWitchLootboxTrackingGuard: cleanup block missing ClearBossRandomLootTracking -> {signature}")

    print("PhantomWitchLootboxTrackingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
