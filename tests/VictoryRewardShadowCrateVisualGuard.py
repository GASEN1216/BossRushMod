"""
Guard: the victory reward shadow crate and hero shell must stay visible and
tinted through the official shader's own fields.

2026-09-23 (VA-16 / VA-17): the visual template's material (Box_EnemyDie_Red ->
SodaCraft/SodaLit_EdgeLight) only has ShadowCaster / DepthOnly / DepthNormals /
UniversalGBuffer passes with hard-coded blend state. The old "transparent"
setup moved it to renderQueue 3000, where URP Deferred only draws Forward
passes, so the crate was not drawn at all. Colour now goes through _Tint via a
MaterialPropertyBlock; the fake-transparency writes are forbidden here.
"""

from pathlib import Path
import sys


# 控制器与 2026-09-23 拆出的表现组件（_Tint 染色、落地回弹、常驻灯跟随）一起算作「奖励箱视觉」源码，
# 必需字面量在任一文件里即可，禁用写法两份都不许出现。
SOURCES = [
    Path("LootAndRewards/VictoryRewardShadowCrateController.cs"),
    Path("LootAndRewards/VictoryRewardCrateFx.cs"),
]
REWARD_SOURCES = [
    Path("LootAndRewards/LootAndRewards.cs"),
    Path("LootAndRewards/LootAndRewardsVictoryRewards.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = "\n".join(source.read_text(encoding="utf-8") for source in SOURCES)
    reward_text = "\n".join(source.read_text(encoding="utf-8") for source in REWARD_SOURCES)

    required_snippets = [
        "private const float RotationSpeedDegreesPerSecond = 30f;",
        "private const float GhostHeroScaleMultiplier = 2f;",
        "private const float HeroShellScaleMultiplier = 2f;",
        "CreateGhostAuraLight();",
        "ghostAuraLight = auraLightObject.AddComponent<Light>();",
        "ghostAuraLight.type = LightType.Point;",
        "internal static class VictoryRewardCrateHeroVisual",
        "AttachToLootbox(",
        # 官方箱子 / 角色着色器的颜色字段是 _Tint，经属性块写入，不复制材质
        'private static readonly int TintPropertyId = Shader.PropertyToID("_Tint");',
        "material.HasProperty(TintPropertyId)",
        "material.HasProperty(EmissionColorPropertyId)",
        "renderer.SetPropertyBlock(block);",
        "VictoryRewardCrateTint.Apply(",
        # 虚影凝实靠尺寸（0.6 -> 1，EaseOut），不靠 alpha
        "private const float InitialGhostScale = 0.6f;",
        "BossRushUI.EaseOut(",
        # 灯：淡入 / 呼吸 / 淡出，落地与打开 / 销毁时不硬切
        "ghostAuraFade = BossRushFxLightFade.Attach(ghostAuraLight,",
        "BossRushFxLightFade fade = BossRushFxLightFade.Attach(aura,",
        "ghostAuraFade.FadeOut(GhostAuraFadeOutSeconds, true);",
        "fade.FadeOut(FadeOutSeconds, true);",
        "Duckov.UI.LootView.HasInventoryEverBeenLooted(inventory)",
        # 落地冲击
        "NewWeaponFx.PlayBurst(landingPosition,",
        "CameraShaker.Shake(",
        "shell.AddComponent<VictoryRewardCrateLandingBounce>();",
        "DisableGhostInteraction(GameObject target)",
        "lootbox.enabled = false;",
        "GetComponentsInChildren<Collider>(true)",
        "GetComponentsInChildren<Rigidbody>(true)",
        "EnsureVisualChildrenVisible(",
        "GetComponentsInChildren<ParticleSystem>(true)",
        "GetComponentsInChildren<Light>(true)",
        "renderer.enabled = true;",
        "renderer.gameObject.SetActive(true);",
    ]

    for snippet in required_snippets:
        if snippet not in text:
            return fail("VictoryRewardShadowCrateVisualGuard: missing snippet -> " + snippet)

    forbidden_snippets = [
        "GetComponentsInChildren<Behaviour>(true)",
        "behaviour.enabled = false;",
    ]

    for snippet in forbidden_snippets:
        if snippet in text:
            return fail("VictoryRewardShadowCrateVisualGuard: forbidden blanket-disable snippet -> " + snippet)

    # 官方 Soda 着色器没有 Forward pass、混合写死：改透明队列 = 整只箱子在 URP Deferred 下不画；
    # renderer.materials 每次通关复制一份材质且无人销毁。
    fake_transparency_snippets = [
        "renderQueue = 3000",
        '"_SrcBlend"',
        '"_ZWrite"',
        "_ALPHABLEND_ON",
        'SetOverrideTag("RenderType", "Transparent")',
        "renderer.materials",
    ]

    for snippet in fake_transparency_snippets:
        if snippet in text:
            return fail("VictoryRewardShadowCrateVisualGuard: forbidden fake-transparency / material-instance snippet -> " + snippet)

    reward_required = "VictoryRewardCrateHeroVisual.AttachToLootbox(lootbox, GetVictoryRewardVisualLootBoxTemplate_LootAndRewards());"
    if reward_required not in reward_text:
        return fail("VictoryRewardShadowCrateVisualGuard: missing final reward hero visual hook -> " + reward_required)

    print("VictoryRewardShadowCrateVisualGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
