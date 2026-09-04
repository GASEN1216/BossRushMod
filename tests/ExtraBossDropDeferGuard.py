#!/usr/bin/env python3
"""
ExtraBossDropDeferGuard — 额外 Boss 掉落的 defer 协议守卫。

背景（CR-2026-09-04）：
`RandomizeBossLoot_LootAndRewards` 会把 `bossMain.dropBoxOnDead = false` 并另建一个
带全新本地 Inventory 的箱子，官方那句 `if (dropBoxOnDead) CreateFromItem(characterItem)`
于是不再执行。任何在 `BeforeCharacterSpawnLootOnDead` 里直接写
`boss.CharacterItem.Inventory` 的额外掉落都会被静默丢掉。
遗种蛋与词缀熔石就是这么 100% 作废的（它们的注册点与主 handler 在同一个函数体内，
必然配对生效）。

不变式：
1. 四个 integration（寒霜长矛 / 女巫镰刀 / 遗种蛋 / 词缀熔石）都必须走 defer 协议，
   四处接线（判定 / 登记 / 进箱消费 / 撤销）一处都不能少；
2. defer 判定统一走 `ShouldDeferExtraBossDropToModPath`，
   该判定必须显式并上 `infiniteHellMode`——无间炼狱同样关掉官方箱且不建新箱；
3. 无间炼狱分支必须在 `FinalizeBossRushLootboxPathTracking` **之前**
   调用 `DropPendingExtraLootIntoWorld`（Finalize 会撤销 pending，顺序反了等于没接）；
4. 龙王手动注册路径必须补 `MarkBossRushLootboxPathTracking`，否则 defer 对龙王恒假。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GUARD = "ExtraBossDropDeferGuard"

SPECIAL_LOOT = os.path.join(REPO_ROOT, "LootAndRewards", "LootAndRewardsSpecialLoot.cs")
RANDOM_BOSS_LOOT = os.path.join(REPO_ROOT, "LootAndRewards", "LootAndRewardsRandomBossLoot.cs")
LOOT_CORE = os.path.join(REPO_ROOT, "LootAndRewards", "LootAndRewards.cs")
DRAGON_KING = os.path.join(REPO_ROOT, "Integration", "DragonKing", "DragonKingBoss.cs")

# integration 名 -> (源文件, 消费者类名)
INTEGRATIONS = {
    "Frostmourne": (
        os.path.join(REPO_ROOT, "Integration", "Frostmourne", "FrostmourneBootstrap.cs"),
        "FrostmourneBlueBossDropHandler",
    ),
    "PhantomWitch": (
        os.path.join(REPO_ROOT, "Integration", "PhantomWitch", "PhantomWitchScytheBootstrap.cs"),
        "PhantomWitchScytheBossDropHandler",
    ),
    "PetNest": (
        os.path.join(REPO_ROOT, "PetNest", "PetNestDropService.cs"),
        "PetNestDropService",
    ),
    "AffixForge": (
        os.path.join(REPO_ROOT, "Integration", "AffixForge", "AffixForgeStoneDropService.cs"),
        "AffixForgeStoneDropService",
    ),
}


def read_text(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return handle.read()
    except OSError:
        return None


def strip_cs_comments(code):
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    code = re.sub(r"//[^\n]*", "", code)
    return code


def load(path, errors):
    text = read_text(path)
    if text is None:
        errors.append("[File] 缺少 " + os.path.relpath(path, REPO_ROOT))
        return None
    return strip_cs_comments(text)


def check_defer_predicate(errors):
    """判定必须存在，且显式覆盖无间炼狱。"""
    code = load(SPECIAL_LOOT, errors)
    if code is None:
        return

    if "internal bool ShouldDeferExtraBossDropToModPath(" not in code:
        errors.append(
            "[判定] LootAndRewardsSpecialLoot 必须提供 ShouldDeferExtraBossDropToModPath")
        return

    body = re.search(
        r"internal bool ShouldDeferExtraBossDropToModPath\([^)]*\)\s*\{(.*?)\n        \}",
        code, flags=re.S)
    if body is None:
        errors.append("[判定] 无法解析 ShouldDeferExtraBossDropToModPath 方法体")
        return
    inner = body.group(1)

    if "infiniteHellMode" not in inner:
        errors.append(
            "[判定] ShouldDeferExtraBossDropToModPath 必须显式并上 infiniteHellMode："
            "MarkBossRushLootboxPathTracking 在无间炼狱下刻意不登记，"
            "只查集合会让额外掉落在无间炼狱全丢")
    if "bossRushLootboxPathBosses" not in inner:
        errors.append(
            "[判定] ShouldDeferExtraBossDropToModPath 必须仍然检查 bossRushLootboxPathBosses")


def check_integrations_wired(errors):
    """四个 integration 的四处接线齐全。"""
    special = load(SPECIAL_LOOT, errors)
    core = load(LOOT_CORE, errors)
    if special is None or core is None:
        return

    for name, (path, handler) in sorted(INTEGRATIONS.items()):
        code = load(path, errors)
        if code is None:
            continue

        # 1) 判定：必须走统一入口，不得再直连旧的 lootbox-only 判定
        if "ShouldDeferExtraBossDropToModPath" not in code:
            errors.append(
                "[判定] {} 必须经 ShouldDeferExtraBossDropToModPath 决定是否 defer".format(name))
        if "ShouldDeferBlueBossExtraDropToBossRushLootbox" in code:
            errors.append(
                "[判定] {} 仍在用只覆盖奖励箱路径的旧判定，"
                "无间炼狱下会漏掉".format(name))

        # 2) 登记
        if not re.search(r"_?pendingBossRushLootboxDrops|_pendingLootboxDrops", code):
            errors.append("[登记] {} 缺少 pending 表".format(name))

        # 3) 进箱消费 + 4) 世界掉落消费 + 撤销：三个入口都要暴露
        for member in ("TryConsumePendingBossRushLootboxDrop",
                       "TryConsumePendingAsWorldDrop",
                       "CancelPendingBossRushLootboxDrop"):
            if member not in code:
                errors.append("[接口] {} 缺少 {}".format(name, member))

        # 进箱消费必须被 AddBossSpecialLootToLootboxCoroutine 调用
        if not re.search(
                re.escape(handler) + r"\.TryConsumePendingBossRushLootboxDrop\s*\(\s*bossMain\s*,\s*inv\s*\)",
                special):
            errors.append(
                "[接线] AddBossSpecialLootToLootboxCoroutine 未消费 {} 的 pending".format(name))

        # 撤销必须挂在 FinalizeBossRushLootboxPathTracking 的 fan-out 上
        if not re.search(
                re.escape(handler) + r"\.CancelPendingBossRushLootboxDrop\s*\(\s*character\s*\)",
                core):
            errors.append(
                "[接线] FinalizeBossRushLootboxPathTracking 未撤销 {} 的 pending".format(name))

        # 世界掉落必须挂在无间炼狱 fan-out 上
        if not re.search(
                re.escape(handler) + r"\.TryConsumePendingAsWorldDrop\s*\(\s*bossMain\s*,\s*position\s*\)",
                special):
            errors.append(
                "[接线] DropPendingExtraLootIntoWorld 未投放 {} 的 pending".format(name))


def check_infinite_hell_order(errors):
    """无间炼狱分支：世界掉落必须排在 Finalize 之前，否则 pending 已被撤销。"""
    code = load(RANDOM_BOSS_LOOT, errors)
    if code is None:
        return

    if "DropPendingExtraLootIntoWorld(bossMain);" not in code:
        errors.append(
            "[无间炼狱] 该分支不建任何箱子，必须调用 DropPendingExtraLootIntoWorld "
            "把额外掉落投到世界里，否则遗种蛋/熔石/长矛/镰刀在无间炼狱全丢")
        return

    branch = re.search(
        r"if \(infiniteHellMode\)\s*\{(.*?)\n                \}", code, flags=re.S)
    if branch is None:
        errors.append("[无间炼狱] 无法解析 infiniteHellMode 分支")
        return
    inner = branch.group(1)

    drop_at = inner.find("DropPendingExtraLootIntoWorld")
    finalize_at = inner.find("FinalizeBossRushLootboxPathTracking")
    if drop_at < 0:
        errors.append("[无间炼狱] DropPendingExtraLootIntoWorld 不在 infiniteHellMode 分支内")
        return
    if finalize_at >= 0 and drop_at > finalize_at:
        errors.append(
            "[无间炼狱] DropPendingExtraLootIntoWorld 必须排在 "
            "FinalizeBossRushLootboxPathTracking 之前——后者会撤销 pending")


def check_prefab_fallback_returns_pending(errors):
    """
    找不到 Lootbox 模板的回退分支：官方箱照建（未置 dropBoxOnDead=false），
    但 pending 会被随后的 Finalize 撤销。必须先把额外掉落还回 characterItem。
    """
    code = load(RANDOM_BOSS_LOOT, errors)
    special = load(SPECIAL_LOOT, errors)
    if code is None or special is None:
        return

    if "ReturnPendingExtraLootToCharacterItem" not in special:
        errors.append(
            "[模板回退] LootAndRewardsSpecialLoot 必须提供 "
            "ReturnPendingExtraLootToCharacterItem")
        return

    # 源码里有两个 `if (prefab == null)`：前一个只是换通用模板再试一次，
    # 后一个才是"彻底找不到、回退原版掉落"的那条。按是否含 Finalize 认后者。
    inner = None
    for match in re.finditer(
            r"if \(prefab == null\)\s*\{(.*?)\n                \}", code, flags=re.S):
        if "FinalizeBossRushLootboxPathTracking" in match.group(1):
            inner = match.group(1)
            break
    if inner is None:
        errors.append("[模板回退] 无法解析回退原版掉落的 `prefab == null` 分支")
        return

    ret_at = inner.find("ReturnPendingExtraLootToCharacterItem")
    finalize_at = inner.find("FinalizeBossRushLootboxPathTracking")
    if ret_at < 0:
        errors.append(
            "[模板回退] 找不到模板时官方箱照建，必须先 ReturnPendingExtraLootToCharacterItem "
            "把额外掉落还回 characterItem，否则 Finalize 撤销 pending 后箱里什么都没有")
        return
    if finalize_at >= 0 and ret_at > finalize_at:
        errors.append(
            "[模板回退] ReturnPendingExtraLootToCharacterItem 必须排在 "
            "FinalizeBossRushLootboxPathTracking 之前")

    # 四个 integration 一个都不能漏
    for name, (_path, handler) in sorted(INTEGRATIONS.items()):
        body = re.search(
            r"private void ReturnPendingExtraLootToCharacterItem\([^)]*\)\s*\{(.*?)\n        \}",
            special, flags=re.S)
        if body is None:
            errors.append("[模板回退] 无法解析 ReturnPendingExtraLootToCharacterItem 方法体")
            return
        if handler + ".TryConsumePendingBossRushLootboxDrop" not in body.group(1):
            errors.append(
                "[模板回退] ReturnPendingExtraLootToCharacterItem 漏了 {}".format(name))


def check_every_finalize_has_a_sink(errors):
    """
    总不变式：`FinalizeBossRushLootboxPathTracking` 会撤销 pending，
    所以在 `LootAndRewardsRandomBossLoot.cs` 里**每一处** Finalize 之前，
    都必须先给 pending 一个去处——要么还回 characterItem（官方箱还会建），
    要么世界掉落（官方箱不会建）。
    唯一豁免：`AddBossSpecialLootToLootboxCoroutine` 的 finally——
    那里进箱消费刚刚执行过，pending 已经空了。

    这条比逐个分支列白名单更耐改：将来谁再加一条早返，忘了给 pending 出路就会红。
    """
    code = load(RANDOM_BOSS_LOOT, errors)
    if code is None:
        return

    lines = code.splitlines()
    sinks = ("ReturnPendingExtraLootToCharacterItem", "DropPendingExtraLootIntoWorld")
    for idx, line in enumerate(lines):
        if "FinalizeBossRushLootboxPathTracking(" not in line:
            continue
        if "private void Finalize" in line:
            continue

        # 只在**同一个语句块**内往上找：遇到 `{` 或 `}` 就停。
        # 用固定行数的窗口不行——那会把上一条分支里的 sink 误当成本分支的，
        # 于是新加一条没出路的早返照样是绿的（本守卫自己踩过这个坑）。
        found = False
        j = idx - 1
        while j >= 0:
            probe = lines[j].strip()
            if probe == "":
                j -= 1
                continue
            if "{" in probe or "}" in probe:
                break
            if any(sink in probe for sink in sinks):
                found = True
                break
            j -= 1
        if found:
            continue

        errors.append(
            "[出路] LootAndRewardsRandomBossLoot.cs 第 {} 行的 Finalize 之前没有给 pending "
            "安排去处（还回 characterItem 或世界掉落）；Finalize 会直接撤销 pending，"
            "额外掉落会静默消失：{}".format(idx + 1, line.strip()))


def check_dragonking_marked(errors):
    """龙王手动路径必须补登记，否则 defer 判定对龙王恒假。"""
    code = load(DRAGON_KING, errors)
    if code is None:
        return
    if "MarkBossRushLootboxPathTracking(character);" not in code:
        errors.append(
            "[龙王] 龙王绕过 RegisterBossRandomLootTracking，必须自行调用 "
            "MarkBossRushLootboxPathTracking，否则额外掉落判定不到奖励箱路径、全部作废")


def main():
    errors = []
    check_defer_predicate(errors)
    check_integrations_wired(errors)
    check_infinite_hell_order(errors)
    check_prefab_fallback_returns_pending(errors)
    check_every_finalize_has_a_sink(errors)
    check_dragonking_marked(errors)

    if errors:
        print("{}: FAIL ({} errors)".format(GUARD, len(errors)))
        for line in errors:
            print("  - " + line)
        return 1
    print(GUARD + ": PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
