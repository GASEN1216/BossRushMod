# -*- coding: utf-8 -*-
u"""天空岛敌人武器配装（owner 2026-09-30：岛上敌人至少品质 3，头目 / 岛主至少品质 5）。

天空岛所有敌人都从官方普通拾荒者 preset 克隆，官方随机装配常给品质 1 的斧头；Forge 只换护甲。
`SkyIslandEnemyArmory` 在官方创建完成后按档次换主武器与近战。遭遇组与序章的逐位档次由执行回归
`tests/fixtures/SkyIslandEncounters/ArmoryRegression.cs` 证明；这里钉住回归够不着的结构：

1. 品质表：普通档下限 3、头目档（Champion / Chief / Storm / Lord）下限 5，巡守高等级换精英档。
2. 三条刷怪路径都配枪：遭遇组在身份层之后、序章头目在 Forge 之后、巡守在固定外形之后。
3. 官方与其它 Mod 的武器都进池、口径不限（owner 2026-09-30）；只排除本 Mod 的 500xxx，过全局黑名单、官方掉落排除标签、
   岛上物资池价值上限，排除控心武器与粘手物品。
4. 缓存只在模块 OnDestroy 复位；武器池经物资池预热逐帧透传，不进每帧路径。
5. 两份新源码登记正式编译清单。

反向检查在内存里恢复错误写法，确认每条都抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

FILES = {
    "rules": "SkyIsland/SkyIslandEnemyArmoryRules.cs",
    "armory": "SkyIsland/SkyIslandEnemyArmory.cs",
    "encounters": "SkyIsland/SkyIslandEncounters.cs",
    "patrols": "SkyIsland/SkyIslandPatrols.cs",
    "prelude": "SkyIsland/SkyIslandPreludeFlow.cs",
    "pools": "SkyIsland/SkyIslandLootPools.cs",
    "module": "SkyIsland/SkyIslandRuntimeModule.cs",
}
PER_FRAME = re.compile(r"void\s+(?:Update|LateUpdate|FixedUpdate|Tick|OnUpdate)\s*\(")


def squash(text):
    return re.sub(r"\s+", "", text or "")


def band(rules, case):
    m = re.search(case + r"\s*return\s+new\s+SkyIslandWeaponBand\((\d+),\s*(\d+),\s*(\w+),\s*(\d+)\)", rules)
    return m


def check(src, bat):
    errors = []
    rules = src["rules"]
    floors = {"IslandFloor": None, "BossFloor": None}
    for name in floors:
        m = re.search(r"const\s+int\s+" + name + r"\s*=\s*(\d+)\s*;", rules)
        floors[name] = int(m.group(1)) if m else None
    if floors["IslandFloor"] is None or floors["IslandFloor"] < 3:
        errors.append("IslandFloor 必须 ≥ 3")
    if floors["BossFloor"] is None or floors["BossFloor"] < 5:
        errors.append("BossFloor 必须 ≥ 5")
    cases = {
        "Elite": r"case\s+SkyIslandEnemyTier\.Elite\s*:",
        "Chief": r"case\s+SkyIslandEnemyTier\.Champion\s*:\s*case\s+SkyIslandEnemyTier\.Chief\s*:",
        "Lord": r"case\s+SkyIslandEnemyTier\.Storm\s*:\s*case\s+SkyIslandEnemyTier\.Lord\s*:",
        "Scav": r"default\s*:",
    }
    for name, case in cases.items():
        m = band(rules, case)
        if not m:
            errors.append("品质表缺少档次：" + name)
            continue
        low, floor = int(m.group(1)), floors.get(m.group(3))
        need = 5 if name in ("Chief", "Lord") else 3
        if low < need or floor is None or floor < need:
            errors.append("%s 档武器下限低于 %d" % (name, need))
    if "IsIslandCaliber" in rules or "IsIslandCaliber" in src["armory"] or "TryGetDynamicEntry" in src["armory"]:
        errors.append("owner 要求口径不过滤、Mod 武器也进池：不得恢复口径白名单或只发官方物品")
    if not re.search(r"OwnItemMin\s*=\s*500001\s*;", rules) or not re.search(r"OwnItemMax\s*=\s*500999\s*;", rules):
        errors.append("本 Mod 物品号段必须是 500001-500999")
    if "rank >= PatrolEliteRank ? SkyIslandEnemyTier.Elite : SkyIslandEnemyTier.Scav" not in rules:
        errors.append("巡守高等级必须换精英档")

    enc = squash(src["encounters"])
    spawn = enc.split(squash("private async void Spawn(Encounter encounter)"), 1)[-1].split(squash("private void ApplyIdentity("), 1)[0]
    identity, arm = spawn.find(squash("ApplyIdentity(created, encounter, i, tier);")), spawn.find(squash("SkyIslandEnemyArmory.Arm(created, tier);"))
    if identity < 0 or arm < identity:
        errors.append("遭遇组必须在身份层之后按档次配枪")
    pre = squash(src["prelude"])
    forge, arm = pre.find(squash('SkyIslandBossForge.TryApply(created, "K3_Relay", 0, context)')), pre.find(squash("SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Chief);"))
    if forge < 0 or arm < forge:
        errors.append("序章头目必须在 Forge 之后按头目档配枪")
    pat = squash(src["patrols"])
    # 2026-10-01 起巡守的外形就是抽到的那位官方 Boss（不再换岛区鸭模）：配枪排在官方创建之后、导航接线之前。
    made, arm, nav = pat.find(squash("created = await clone.CreateCharacterAsync(")), pat.find(squash("SkyIslandEnemyArmory.ArmPatrol(created, cell.Profile.Rank);")), pat.find(squash("ConfigureNavigation(created);"))
    if made < 0 or arm < made or nav < arm:
        errors.append("巡守必须在官方创建之后、接导航之前按岛区等级配枪")

    armory = src["armory"]
    for token, why in (
        ("SkyIslandEnemyArmoryRules.IsOwnModItem(id)", "必须排除本 Mod 自己的 500xxx 物品"),
        ("LootBlacklistRegistry.Contains(id)", "必须过全局黑名单"),
        ("LootExcludeTagPolicy.BuildExcludeTags(tags, true, true)", "必须排除官方掉落排除标签"),
        ("SkyIslandLootTables.AllowedInPool(prefab.Value)", "必须过岛上物资池价值上限"),
        ("ControlMindTypeHash", "必须排除控心武器"),
        ("prefab.Sticky", "必须排除粘手物品"),
        ("band.Keeps(current.Quality)", "达标原装不换"),
        ("slot.CanPlug(created)", "拆旧之前先校验新武器能插进槽"),
        ("character.SwitchToFirstAvailableWeapon()", "换完要重新拿起武器"),
        # owner 2026-10-01「Boss 一直说没子弹了」：达标留下的官方原装与副武器也要补备弹，不能只给换上的那把。
        ("EnsureAmmo(body, PrimarySlot, band, random, fromKey, label);", "主武器（含达标留下的原装）必须补备弹"),
        ("EnsureAmmo(body, SecondarySlot, band, random, fromKey, label);", "副武器也必须补备弹"),
    ):
        if token not in armory:
            errors.append("SkyIslandEnemyArmory " + why + "（缺 " + token + "）")
    for body in PER_FRAME.findall(armory):
        errors.append("SkyIslandEnemyArmory 不得有每帧方法：" + body)

    pools = squash(src["pools"])
    prewarm = pools.split(squash("internal static IEnumerator Prewarm()"), 1)[-1]
    if squash("IEnumerator armory = SkyIslandEnemyArmory.Prewarm();") not in prewarm \
            or squash("while (armory.MoveNext()) yield return armory.Current;") not in prewarm:
        errors.append("武器池必须经物资池预热逐帧透传")
    if squash("SkyIslandEnemyArmory.ResetStaticCaches();") not in squash(src["module"]):
        errors.append("武器池缓存必须在模块 OnDestroy 复位")

    for name in ("SkyIslandEnemyArmoryRules", "SkyIslandEnemyArmory"):
        if "echo(SkyIsland\\" + name + ".cs" not in bat:
            errors.append("正式编译漏收：" + name)
    return errors


def load():
    src = {key: clean_source((ROOT / path).read_text(encoding="utf-8-sig")) for key, path in FILES.items()}
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    return src, bat


def reverse(src, bat):
    probes = [
        ("rules", "internal const int BossFloor = 5;", "internal const int BossFloor = 1;"),
        ("rules", "return new SkyIslandWeaponBand(3, 4, IslandFloor, 2);", "return new SkyIslandWeaponBand(1, 4, IslandFloor, 2);"),
        ("patrols", "SkyIslandEnemyArmory.ArmPatrol(created, cell.Profile.Rank);", ""),
        ("prelude", "SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Chief);", ""),
        ("encounters", "SkyIslandEnemyArmory.Arm(created, tier);", ""),
        ("armory", "SkyIslandEnemyArmoryRules.IsOwnModItem(id)", "false"),
        ("armory", "return !string.IsNullOrEmpty(caliber) &&", "return SkyIslandEnemyArmoryRules.IsIslandCaliber(caliber) &&"),
        ("armory", "SkyIslandLootTables.AllowedInPool(prefab.Value)", "true"),
        ("pools", "while (armory.MoveNext()) yield return armory.Current;", ""),
        ("module", "SkyIslandEnemyArmory.ResetStaticCaches();", ""),
    ]
    missed = []
    for key, old, new in probes:
        if old not in src[key]:
            missed.append("反向探针锚点不存在：" + old)
            continue
        broken = dict(src)
        broken[key] = src[key].replace(old, new)
        if not check(broken, bat):
            missed.append("反向探针没转红：" + old)
    if not check(src, bat.replace("echo(SkyIsland\\SkyIslandEnemyArmory.cs", "")):
        missed.append("反向探针没转红：编译清单")
    return missed


if __name__ == "__main__":
    sources, bat_text = load()
    problems = check(sources, bat_text) + reverse(sources, bat_text)
    if problems:
        for p in problems:
            print("FAIL " + p)
        sys.exit(1)
    print("PASS SkyIslandEnemyArmoryGuard: quality floors, three spawn paths, mod-inclusive pool, cache lifecycle")
