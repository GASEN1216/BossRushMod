"""天空岛原版参照倍率（生命 / 伤害 / 反应等 ×1.5，机动与感知 ×1.5）的数值来源、刷怪底模与创建时序。行为由 SkyIslandEncounters 执行回归验证。"""
import json
import re
from pathlib import Path

from cs_source_util import clean_source


def check(root):
    sky = root / "SkyIsland"
    code = {name: clean_source((sky / (name + ".cs")).read_text(encoding="utf-8-sig"))
            for name in ("SkyIslandCombatBalance", "SkyIslandCombatPreset", "SkyIslandEncounters",
                         "SkyIslandEnemyTiers", "SkyIslandBossForge", "SkyIslandBossRules", "SkyIslandPreludeFlow")}
    balance = code["SkyIslandCombatBalance"]
    # owner 2026-10-01：先定属性翻 3 倍，同日实测后「整体 Boss 数值改为现在的一半」→ ×1.5；
    # 移速、弹速、射程、视野、听觉、夜视一直是 1.5（再高会在一屏外开火、跑成瞬移；减半又比原版还瞎）。
    assert re.search(r"const\s+float\s+Multiplier\s*=\s*1\.5f\s*;", balance), "统一倍率必须是原版 ×1.5"
    assert re.search(r"const\s+float\s+PerceptionMultiplier\s*=\s*1\.5f\s*;", balance), "机动与感知倍率必须是原版 ×1.5"
    assert re.search(r"const\s+float\s+PreludeWardenHealthFactor\s*=\s*2f\s*;", balance), "零号区序章守卫额外生命系数必须是 2"
    # owner 2026-10-02：本 Mod 专属 Boss（头目、岛主、具名对手、噬风，即 IsBossTier）血量至少 1000，统一倍率之后取大。
    floor = re.search(r"const\s+float\s+BossHealthFloor\s*=\s*([\d.]+)f\s*;", balance)
    assert floor and float(floor.group(1)) >= 1000, "专属 Boss 生命下限必须 ≥ 1000"
    snapshot = json.loads((root / "tests/fixtures/SkyIslandEncounters/VanillaCombatReference.json").read_text(encoding="utf-8"))
    rows = re.findall(r'new SkyIslandCombatBaseline\s*\{\s*PresetId\s*=\s*"([^"]+)"\s*,([^}]+)\}', balance)
    assert len(rows) == len(snapshot["baselines"]), "原版参照表与 Wiki 快照数量不一致"
    actual = dict(rows)
    for row in snapshot["baselines"]:
        assert row["id"] in actual, "缺少原版参照：" + row["id"]
        values = dict(re.findall(r"(\w+)\s*=\s*(-?[\d.]+)f", actual[row["id"]]))
        for field, value in row.items():
            if field not in ("id", "name"):
                assert field in values and float(values[field]) == value, "Wiki 基准漂移：" + row["id"] + "/" + field
    for preset in re.findall(r'VanillaPresetId\s*=\s*"([^"]+)"', code["SkyIslandBossRules"]):
        assert preset in actual, "头目映射到不存在的原版参照：" + preset

    spawn = code["SkyIslandEncounters"].split("private async void Spawn(Encounter encounter)", 1)[1].split("private void ApplyIdentity(", 1)[0]
    sequence = ["UnityEngine.Object.Instantiate(source)", "SkyIslandCombatPreset.Apply(clone, source, encounter.Id, i, tier)",
                "await clone.CreateCharacterAsync(", "SkyIslandEnemyTiers.ApplyAi(ai, tier, encounter.Manual)", "ApplyIdentity(created, encounter, i, tier)"]
    positions = [spawn.find(token) for token in sequence]
    assert all(p >= 0 for p in positions) and positions == sorted(positions), "必须先克隆、写属性，再创建角色、挂身份"
    assert spawn.count("SkyIslandCombatPreset.Apply(") == 1, "同一个生成位只准备一次属性"
    prelude = code["SkyIslandPreludeFlow"].split("private async void SpawnBoss(Vector3 position, int expectedGeneration)", 1)[1].split("private bool IsObjectiveGeneration(", 1)[0]
    sequence = ["UnityEngine.Object.Instantiate(source)",
                'SkyIslandCombatPreset.Apply(clone, source, "K3_Relay", 0, SkyIslandEnemyTier.Chief)',
                "await clone.CreateCharacterAsync(", 'SkyIslandBossForge.TryApply(created, "K3_Relay", 0, context)']
    positions = [prelude.find(token) for token in sequence]
    assert all(p >= 0 for p in positions) and positions == sorted(positions), "序章头目也必须在创建前应用同一战斗基准"
    assert prelude.count("SkyIslandCombatPreset.Apply(") == 1, "序章头目不能遗漏或重复强化"
    for name in ("SkyIslandEnemyTiers", "SkyIslandBossForge"):
        assert not re.search(r"\b(?:ApplyStats|ApplyReaction|HealthMultiplier|DamageMultiplier|ReactionSpeedup)\s*\(", code[name]), name + " 不得再叠旧倍率"
        assert not re.search(r"\b(?:baseReactionTime|reactionTime|shootDelay)\s*[/\*]=|BaseValue\s*\*=", code[name]), name + " 不得再乘血量、伤害或反应"
    warden = prelude.find("clone.health *= SkyIslandCombatBalance.PreludeWardenHealthFactor;")
    assert prelude.count("clone.health *= SkyIslandCombatBalance.PreludeWardenHealthFactor;") == 1 and (
        prelude.find('SkyIslandCombatPreset.Apply(clone, source, "K3_Relay"') < warden < prelude.find("await clone.CreateCharacterAsync(")), (
        "序章守卫的额外生命必须在统一倍率之后、官方创建之前乘一次")
    adapter = code["SkyIslandCombatPreset"]
    health_at = adapter.find("clone.health = (baseline != null ? baseline.Health : source.health) * factor;")
    floor_line = "if (SkyIslandEnemyArmoryRules.IsBossTier(tier)) clone.health = Math.Max(clone.health, SkyIslandCombatBalance.BossHealthFloor);"
    assert 0 <= health_at < adapter.find(floor_line) and adapter.count(floor_line) == 1, "专属 Boss 生命下限要在统一倍率之后取大一次"
    for field, factor in (("health", "factor"), ("damageMultiplier", "factor"), ("meleeDamageMultiplier", "factor"),
                          ("aiCombatFactor", "factor"), ("moveSpeedFactor", "sense"), ("bulletSpeedMultiplier", "sense"),
                          ("gunDistanceMultiplier", "sense"), ("nightVisionAbility", "sense"), ("sightDistance", "sense"),
                          ("hearingAbility", "sense")):
        assert re.search(r"clone\." + field + r"\s*=[^;]*\*\s*" + factor + r"\s*;", adapter), "倍率分组漂移：" + field + " 应乘 " + factor
    for field in ("gunScatterMultiplier", "reactionTime", "shootDelay"):
        assert re.search(r"clone\." + field + r"\s*=[^;]*/\s*factor\s*;", adapter), "反应 / 开火 / 散布必须除以统一倍率：" + field
    # 刷怪底模只许经 SkyIslandEnemySources 取（官方 Boss）：自己扫 preset 会把近战小弟、足球员这类底模混进来，配了枪也不开（2026-10-01 实机）。
    for path in sorted(sky.glob("*.cs")):
        if path.name == "SkyIslandEnemySources.cs":
            continue
        text = clean_source(path.read_text(encoding="utf-8-sig"))
        assert "FindObjectsOfTypeAll<CharacterRandomPreset>" not in text, path.name + " 自己扫 CharacterRandomPreset：刷怪底模必须走 SkyIslandEnemySources"
    # owner 2026-10-01：天空岛全部用官方 Boss 预设。池子只收官方 Boss，排除项是 owner 拍板的那几类。
    sources = clean_source((sky / "SkyIslandEnemySources.cs").read_text(encoding="utf-8-sig"))
    pool = sources.split("internal static bool IsPoolBoss(", 1)[1].split("internal static void ResetStaticCaches(", 1)[0]
    for token, why in (("!preset.isBoss", "只收官方 Boss"), ('"EnemyPreset_"', "只收官方前缀（排除本 Mod 与别的 Mod 的大型 Boss）"),
                       ('"_NPC_"', "排除 NPC 伪装 Boss"), ('"Koukou"', "排除口口口口岛主"), ('"_Test"', "排除测试预设"),
                       ("preset.isVehicle", "排除载具"), ("Teams.middle", "排除中立阵营")):
        assert token in pool, "Boss 池口径缺失：" + why
    spawn = code["SkyIslandEncounters"].split("private async void Spawn(Encounter encounter)", 1)[1].split("private void ApplyIdentity(", 1)[0]
    order = ["SkyIslandEnemySources.ForBaseline(baseline, encounter.Id", "UnityEngine.Object.Instantiate(source)",
             "SkyIslandCombatPreset.Apply(clone, source, encounter.Id, i, tier)",
             "if (baseline == null) SkyIslandMinionKit.UseScavLoot(clone, scavReference);", "await clone.CreateCharacterAsync("]
    positions = [spawn.find(token) for token in order]
    assert all(p >= 0 for p in positions) and positions == sorted(positions), "遭遇：先按参照取 Boss 底模、写属性、小兵换拾荒者掉落，再交官方创建"
    patrols = clean_source((sky / "SkyIslandPatrols.cs").read_text(encoding="utf-8-sig"))
    assert "SkyIslandEnemySources.ForMinion(cell.Slot.Id)" in patrols, "巡守每个槽位要固定抽一位官方 Boss"
    assert "SkyIslandMinionKit.UseScavLoot(clone, scavReference);" in patrols, "巡守要换拾荒者掉落与经验"
    assert "SkyIslandEnemySources.ForBaseline(" in code["SkyIslandPreludeFlow"], "序章守卫要克隆参照的那位官方 Boss"
    kit = clean_source((sky / "SkyIslandMinionKit.cs").read_text(encoding="utf-8-sig"))
    assert "clone.isBoss = false;" in kit, "小兵不能算 Boss 击杀（战役目标、日报、Rogue 结算按 isBossCharacter 计数）"
    for token in ("clone.exp = scav.exp;", "ItemsField.SetValue(clone, merged);"):
        assert token in kit, "小兵掉落 / 经验换拾荒者口径缺失：" + token
    armory = clean_source((sky / "SkyIslandEnemyArmory.cs").read_text(encoding="utf-8-sig"))
    assert "if (current == null) return false;" in armory, "近战 Boss 空着的主武器槽不许塞枪（塞了也不开）"
    assert "ReferenceEquals(clone, source)" in adapter, "不得修改原版资源"
    assert re.search(r"clone\.setMeleeDamageMultiplier\s*=\s*true\s*;", adapter), "必须显式接上近战倍率"
    for field in ("health", "damageMultiplier", "meleeDamageMultiplier", "moveSpeedFactor", "bulletSpeedMultiplier",
                  "gunDistanceMultiplier", "gunScatterMultiplier", "gunCritRateGain", "nightVisionAbility",
                  "aiCombatFactor", "sightDistance", "hearingAbility", "reactionTime", "shootDelay", "nightReactionTimeFactor"):
        assert re.search(r"clone\." + field + r"\s*=", adapter), "缺少战斗属性接线：" + field
    for field in ("exp", "nameKey", "team", "loot", "dropBoxOnDead"):
        assert not re.search(r"clone\." + field + r"\s*=", adapter), "战斗倍率不得改写经济或身份：" + field
    bat = (root / "compile_official.bat").read_text(encoding="utf-8-sig")
    for name in ("SkyIslandCombatBalance", "SkyIslandCombatPreset", "SkyIslandEnemySources", "SkyIslandMinionKit"):
        assert "echo(SkyIsland\\" + name + ".cs" in bat, "正式编译漏收：" + name


if __name__ == "__main__":
    check(Path(__file__).resolve().parent.parent)
    print("PASS SkyIslandCombatBalanceGuard: Wiki baseline, x1.5 / perception x1.5, official boss sources, clone ownership and spawn order")
