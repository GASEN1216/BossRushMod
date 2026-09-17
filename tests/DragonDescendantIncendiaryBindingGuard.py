"""龙裔燃烧弹必须按官方物品身份接线，不能误取 Firework 或任意手雷。

仅验证静态资源接线；官方资源内容、碰撞与地面火焰仍需实机确认。
"""
from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parents[1]
BASE = "Integration/DragonDescendant/"


def source(name):
    return clean_source((ROOT / BASE / name).read_text(encoding="utf-8-sig"))


def method(text, signature):
    start = text.index(signature)
    start = text.index("{", start)
    depth = 0
    for match in re.finditer(r'"(?:\\.|[^"\\])*"|[{}]', text[start:]):
        if match.group() == "{":
            depth += 1
        elif match.group() == "}":
            depth -= 1
            if depth == 0:
                return re.sub(r"\s+", " ", text[start:start + match.end()])
    raise AssertionError("方法体未闭合: " + signature)


def main():
    controller = source("DragonDescendantAbilities.cs")
    projectiles = source("DragonDescendantAbilities_ProjectilesAndGrenades.cs")
    config = source("DragonDescendantConfig.cs")
    phase = source("DragonDescendantAbilities_ResurrectionAndPhase.cs")
    assert re.search(r"\bconst\s+int\s+IncendiaryGrenadeTypeId\s*=\s*941\s*;", config), \
        "DragonDescendantConfig.cs: 官方燃烧弹 TypeID 必须为 941"
    assert "PreCachePrefabs();" in method(controller, "public void Initialize("), \
        "Initialize 必须实际调用预缓存"
    cache = method(controller, "private void PreCachePrefabs()")
    for statement in (
        "if (!grenadeSearched)",
        "grenadeSearched = true;",
        "var itemPrefab = ItemAssetsCollection.GetPrefab(DragonDescendantConfig.IncendiaryGrenadeTypeId);",
        'if (itemPrefab != null && itemPrefab.TypeID == DragonDescendantConfig.IncendiaryGrenadeTypeId && string.Equals(itemPrefab.DisplayNameRaw, "Item_FireGrenade", StringComparison.Ordinal))',
        "var skillSetting = itemPrefab.GetComponent<ItemSetting_Skill>();",
        "var grenadeSkill = skillSetting != null ? skillSetting.Skill as Skill_Grenade : null;",
        "if (grenadeSkill != null && grenadeSkill.grenadePfb != null)",
        "cachedGrenadeSkill = grenadeSkill;",
        "if (cachedGrenadeSkill == null)",
    ):
        assert statement in cache, "PreCachePrefabs 缺少官方身份接线: " + statement
    assert "Debug.LogWarning(" in cache, "资源失配必须在正式构建中可诊断"
    assert "Resources.FindObjectsOfTypeAll" not in cache, "禁止重新按已加载资源顺序猜手雷类型"
    assert re.findall(r"cachedGrenadeSkill\s*=(?!=)\s*([^;]+);", controller) == ["null", "null", "grenadeSkill"], \
        "燃烧弹缓存只能来自经过身份校验的官方技能"
    reset = method(controller, "public static void ClearStaticCache()")
    for statement in ("cachedGrenadeSkill = null;", "grenadeSearched = false;"):
        assert statement in reset, "切图必须清理燃烧弹缓存: " + statement
    assert "return cachedGrenadeSkill;" in method(projectiles, "private Skill_Grenade FindIncendiaryGrenadeSkill()"), \
        "投掷路径必须读取已验证的技能缓存"
    create = method(projectiles, "private void CreateIncendiaryGrenade(")
    for statement in (
        "Skill_Grenade grenadeSkill = FindIncendiaryGrenadeSkill();",
        "Grenade grenadePrefab = grenadeSkill != null ? grenadeSkill.grenadePfb : null;",
        "UnityEngine.Object.Instantiate(grenadePrefab, startPos, Quaternion.identity);",
        "grenade.createExplosion = grenadeSkill.createExplosion;",
        "grenade.explosionShakeStrength = grenadeSkill.explosionShakeStrength;",
        "grenade.damageRange = grenadeSkill.SkillContext.effectRange;",
        "grenade.delayFromCollide = grenadeSkill.delayFromCollide;",
        "grenade.delayTime = grenadeSkill.delay;",
        "grenade.isLandmine = grenadeSkill.isLandmine;",
        "grenade.landmineTriggerRange = grenadeSkill.landmineTriggerRange;",
        "grenade.Launch(startPos, velocity, bossCharacter, false);",
        "StartCoroutine(DelayedFireExplosion(targetPos, 1f));",
    ):
        assert statement in create, "CreateIncendiaryGrenade 缺少官方参数或后备接线: " + statement
    assert create.index("grenade.delayTime =") < create.index("grenade.Launch("), "引信必须在 Launch 前配置"
    assert "CreateIncendiaryGrenade(targetPos);" in method(projectiles, "private void ThrowIncendiaryGrenade()"), \
        "定时投掷必须走统一创建入口"
    assert "CreateIncendiaryGrenade(targetPos);" in method(phase, "private void ThrowIncendiaryGrenadesInEightDirections()"), \
        "复活八方向投掷必须走统一创建入口"
    print("DragonDescendantIncendiaryBindingGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError) as error:
        raise SystemExit("DragonDescendantIncendiaryBindingGuard: FAIL - " + str(error))
