"""天空岛原版参照 +50% 的数值来源与创建时序。行为由 SkyIslandEncounters 执行回归验证。"""
import json
import re
from pathlib import Path

from cs_source_util import clean_source


def check(root):
    sky = root / "DebugAndTools/SkyIsland"
    code = {name: clean_source((sky / (name + ".cs")).read_text(encoding="utf-8-sig"))
            for name in ("SkyIslandCombatBalance", "SkyIslandCombatPreset", "SkyIslandEncounters",
                         "SkyIslandEnemyTiers", "SkyIslandBossForge", "SkyIslandBossRules", "SkyIslandPreludeFlow")}
    balance = code["SkyIslandCombatBalance"]
    assert re.search(r"const\s+float\s+Multiplier\s*=\s*1\.5f\s*;", balance), "统一倍率必须是原版 ×1.5"
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
                "await clone.CreateCharacterAsync(", "SkyIslandEnemyTiers.ApplyAi(ai, tier)", "ApplyIdentity(created, encounter, i, tier)"]
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
    adapter = code["SkyIslandCombatPreset"]
    assert "ReferenceEquals(clone, source)" in adapter, "不得修改原版资源"
    assert re.search(r"clone\.setMeleeDamageMultiplier\s*=\s*true\s*;", adapter), "必须显式接上近战倍率"
    for field in ("health", "damageMultiplier", "meleeDamageMultiplier", "moveSpeedFactor", "bulletSpeedMultiplier",
                  "gunDistanceMultiplier", "gunScatterMultiplier", "gunCritRateGain", "nightVisionAbility",
                  "aiCombatFactor", "sightDistance", "hearingAbility", "reactionTime", "shootDelay", "nightReactionTimeFactor"):
        assert re.search(r"clone\." + field + r"\s*=", adapter), "缺少战斗属性接线：" + field
    for field in ("exp", "nameKey", "team", "loot", "dropBoxOnDead"):
        assert not re.search(r"clone\." + field + r"\s*=", adapter), "战斗倍率不得改写经济或身份：" + field
    bat = (root / "compile_official.bat").read_text(encoding="utf-8-sig")
    for name in ("SkyIslandCombatBalance", "SkyIslandCombatPreset"):
        assert "echo(DebugAndTools\\SkyIsland\\" + name + ".cs" in bat, "正式编译漏收：" + name


if __name__ == "__main__":
    check(Path(__file__).resolve().parent.parent)
    print("PASS SkyIslandCombatBalanceGuard: Wiki baseline, +50%, clone ownership and spawn order")
