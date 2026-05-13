"""
Guard: the victory reward shadow crate must configure cloned lootbox materials
for actual transparent rendering instead of only writing alpha values.
"""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/VictoryRewardShadowCrateController.cs")
REWARD_SOURCES = [
    Path("LootAndRewards/LootAndRewards.cs"),
    Path("LootAndRewards/LootAndRewardsVictoryRewards.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    reward_text = "\n".join(source.read_text(encoding="utf-8") for source in REWARD_SOURCES)

    required_snippets = [
        "private const float RotationSpeedDegreesPerSecond = 30f;",
        "private const float GhostHeroScaleMultiplier = 2f;",
        "private const float HeroShellScaleMultiplier = 2f;",
        "ConfigureGhostMaterialTransparency(",
        "CreateGhostAuraLight();",
        "ghostAuraLight = auraLightObject.AddComponent<Light>();",
        "ghostAuraLight.type = LightType.Point;",
        "internal static class VictoryRewardCrateHeroVisual",
        "AttachToLootbox(",
        'material.SetOverrideTag("RenderType", "Transparent")',
        'material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);',
        'material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);',
        'material.SetInt("_ZWrite", 0);',
        "material.renderQueue = 3000;",
        'material.EnableKeyword("_ALPHABLEND_ON");',
        'material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");',
        "material.HasProperty(TintColorPropertyId)",
        "material.HasProperty(EmissionColorPropertyId)",
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

    reward_required = "VictoryRewardCrateHeroVisual.AttachToLootbox(lootbox, GetVictoryRewardVisualLootBoxTemplate_LootAndRewards());"
    if reward_required not in reward_text:
        return fail("VictoryRewardShadowCrateVisualGuard: missing final reward hero visual hook -> " + reward_required)

    print("VictoryRewardShadowCrateVisualGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
