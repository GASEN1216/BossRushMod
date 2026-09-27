"""Guard: 焚天龙铳所有弹药都不带那层「很白的光效」（2026-09-26 owner：太塑料）。

钉三件事：

1. 缺包后备特效不再退回白色。`Fx_DragonGun_*` 都没打进 dragonking 包，旧版 `GetEffectColor` 默认给 `Color.white`，
   冲锋 / 突击 / 重型 / 狙击 / 霰弹 / 马格南 / 箭矢七种弹药每发都挂一团白色加色光团和一盏白色点光源。
   现在只有 `TryGetEffectColor` 登记了专属配色的名字才画后备：默认分支返回 false，两个 `CreateFallbackEffect`
   在建 GameObject 之前就对未登记名字返回 null，`AddFallbackVisuals` 对未登记名字什么都不加。
   龙铳 profile 引用的每个 `Fx_DragonGun_*` 名字，要么没登记（不画），要么登记的是非白的弹体色（能量弹的青色）。
2. 龙铳弹体 / 拖尾 / 命中 / 绽放 / 地面区代码里没有「发出白光」的写法：Lerp 到白、`WithAlpha(Color.white…)`、
   `MinMaxGradient(Color.white…)`、把 startColor / endColor / 光色直接赋成白、纯白字面量、烟花的 `hotColor`、
   绽放里的 `BloomFlash` 近白闪光层与它的 (1, 0.95, 0.8) 色。渐变里的白色色键是「不染色」的乘子，不在此列。
3. 烟花弹的拖尾、拖尾火星与终点火花仍用自身调色板色着色，绽放仍播火花与光晕——防止把白光连同弹体一起删没。

文本守卫防不住「保留 token、杀掉执行路径」；运行时观感只能 owner 实机看。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ASSET_SOURCE = Path("Integration/DragonKing/DragonKingAssetManager.cs")
PROFILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
AGENT_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
GUN_FX_SOURCES = [
    AGENT_SOURCE,
    Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent_Fx.cs"),
    Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent_HitStage.cs"),
    Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs"),
    Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs"),
    Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs"),
]

# 「发出白光」的写法。渐变色键里的 Color.white 是乘子恒等元（只淡出不染色），不匹配这些模式。
FORBIDDEN_WHITE_EMISSION = [
    (r"Color\.Lerp\(\s*Color\.white\b", "Lerp 起点是白色"),
    (r"Color\.Lerp\([^;()]*(?:\([^;()]*\))?[^;()]*,\s*Color\.white\s*,", "Lerp 朝白色过渡"),
    (r"WithAlpha\(\s*Color\.white\b", "WithAlpha(Color.white…) 发白色"),
    (r"MinMaxGradient\(\s*Color\.white\b", "粒子起始色取白色"),
    (r"\b(?:startColor|endColor|color)\s*=\s*Color\.white\b", "颜色直接赋成白色"),
    (r"new\s+Color\(\s*1(?:\.0*)?f\s*,\s*1(?:\.0*)?f\s*,\s*1(?:\.0*)?f\b", "纯白颜色字面量"),
    (r"\bhotColor\b", "烟花拖尾的白热芯 hotColor"),
    (r"\"BloomFlash\"", "烟花绽放的近白闪光层 BloomFlash"),
    (r"private\s+ParticleSystem\s+flash\s*;", "烟花绽放的近白闪光层字段"),
    (r"new\s+Color\(\s*1f\s*,\s*0\.95f\s*,\s*0\.8f\s*\)", "烟花闪光的近白色 (1, 0.95, 0.8)"),
]

# 登记了后备配色的龙铳名字，三个分量里最小的不能高于这个值（再高就是近白）。
MAX_MIN_CHANNEL_FOR_GUN_FALLBACK = 0.6


def fail(message: str) -> int:
    print("DragonKingBossGunNoWhiteGlowGuard: FAIL - " + message)
    return 1


def read_clean(path: Path) -> str:
    return clean_source(path.read_text(encoding="utf-8-sig"))


def extract_block(text: str, signature: str, start_at: int = 0):
    start = text.find(signature, start_at)
    if start < 0:
        return None, -1
    brace = text.find("{", start)
    if brace < 0:
        return None, -1
    depth = 0
    for idx in range(brace, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace:idx + 1], start
    return None, -1


def normalize(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def check_asset_manager() -> int:
    if not ASSET_SOURCE.exists():
        return fail(f"missing {ASSET_SOURCE}")
    text = read_clean(ASSET_SOURCE)

    if re.search(r"\bGetEffectColor\s*\(", text):
        return fail("DragonKingAssetManager 又出现了带白色默认值的 GetEffectColor(...)")

    color_body, _ = extract_block(text, "private static bool TryGetEffectColor(string prefabName, out Color color)")
    if color_body is None:
        return fail("missing TryGetEffectColor(string prefabName, out Color color)")
    if "Color.white" in color_body:
        return fail("TryGetEffectColor 里出现 Color.white：后备特效不许给白色")
    if not re.search(r"default\s*:\s*color\s*=\s*default\s*\(\s*Color\s*\)\s*;\s*return\s+false\s*;", color_body):
        return fail("TryGetEffectColor 默认分支必须是 color = default(Color); return false;（未登记的名字不画后备）")

    signatures = [
        "private static GameObject CreateFallbackEffect(string prefabName, Vector3 position, Quaternion rotation)",
        "private static GameObject CreateFallbackEffect(string prefabName, Vector3 position, Quaternion rotation, Transform parent)",
    ]
    for signature in signatures:
        body, _ = extract_block(text, signature)
        if body is None:
            return fail("missing " + signature)
        flat = normalize(body)
        gate = re.search(r"if \( ?!TryGetEffectColor\(prefabName, out \w+\)\) \{ return null; \}", flat)
        create = flat.find("new GameObject(")
        if gate is None:
            return fail(signature + " 没有对未登记名字先返回 null")
        if create < 0 or gate.start() > create:
            return fail(signature + " 必须在 new GameObject 之前就对未登记名字返回 null")

    fallback_body, _ = extract_block(text, "private static void AddFallbackVisuals(GameObject obj, string prefabName)")
    if fallback_body is None:
        return fail("missing AddFallbackVisuals(GameObject obj, string prefabName)")
    flat = normalize(fallback_body)
    gate = re.search(r"if \( ?!TryGetEffectColor\(prefabName, out effectColor\)\) \{ return; \}", flat)
    if gate is None:
        return fail("AddFallbackVisuals 必须先 TryGetEffectColor，未登记名字直接 return")
    for later in ["new GameObject(\"FallbackGlow\")", "AddComponent<Light>()"]:
        pos = flat.find(later)
        if pos < 0:
            return fail("AddFallbackVisuals 缺少 " + later)
        if pos < gate.start():
            return fail("AddFallbackVisuals 在配色门之前就创建了 " + later)
    for snippet in [
        "main.startColor = new Color(effectColor.r, effectColor.g, effectColor.b, 0.8f);",
        "light.color = effectColor;",
    ]:
        if snippet not in flat:
            return fail("AddFallbackVisuals 的光团 / 点光源必须用登记的配色 -> " + snippet)

    profile_text = read_clean(PROFILE_SOURCE)
    gun_names = sorted(set(re.findall(r"\"(Fx_DragonGun_\w+)\"", profile_text)))
    for name in gun_names:
        label = f"case \"{name}\":"
        pos = color_body.find(label)
        if pos < 0:
            continue  # 未登记：缺包时不画后备
        match = re.search(
            r"color\s*=\s*new\s+Color\(\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*,\s*([0-9.]+)f",
            color_body[pos:])
        if match is None:
            return fail(f"{name} 登记了后备但读不出 new Color(r, g, b, a) 配色")
        rgb = [float(match.group(i)) for i in (1, 2, 3)]
        if min(rgb) > MAX_MIN_CHANNEL_FOR_GUN_FALLBACK:
            return fail(f"{name} 的后备配色 {tuple(rgb)} 接近白色：龙铳弹药不许挂白色光团")
    return 0


def check_gun_fx_sources() -> int:
    for path in GUN_FX_SOURCES:
        if not path.exists():
            return fail(f"missing {path}")
        text = read_clean(path)
        for pattern, label in FORBIDDEN_WHITE_EMISSION:
            match = re.search(pattern, text)
            if match:
                line = text.count("\n", 0, match.start()) + 1
                return fail(f"{path}:{line} {label} -> {match.group(0)}")
    return 0


def check_firework_keeps_own_colors() -> int:
    text = read_clean(AGENT_SOURCE)
    trail_body, _ = extract_block(text, "private GameObject CreateFireworkTrail()")
    if trail_body is None:
        return fail("missing CreateFireworkTrail()")
    flat = normalize(trail_body)
    for snippet in [
        "Color color = ResolveFireworkColor(spark ? projectileIndex : shotId, spark);",
        "trail.startColor = WithAlpha(color, spark ? 0.92f : 0.98f);",
        "trail.endColor = WithAlpha(color, 0f);",
        "main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(color, 0.88f), WithAlpha(color, 0.72f));",
        "ps.Play(true);",
    ]:
        if snippet not in flat:
            return fail("烟花拖尾必须用自身调色板色 -> " + snippet)

    spark_body, _ = extract_block(text, "internal void Play(Vector3 position, Color color, int generation)")
    if spark_body is None:
        return fail("missing FireworkSparkEffectHandle.Play(Vector3, Color, int)")
    flat = normalize(spark_body)
    for snippet in [
        "main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(color, 0.9f), WithAlpha(color, 0.82f));",
        "particles.Play(true);",
    ]:
        if snippet not in flat:
            return fail("烟花终点火花必须用自身调色板色 -> " + snippet)

    bloom_body, _ = extract_block(text, "internal void Play(Vector3 position, Color colorA, Color colorB, int generation)")
    if bloom_body is None:
        return fail("missing FireworkBloomEffectHandle.Play(Vector3, Color, Color, int)")
    flat = normalize(bloom_body)
    for snippet in [
        "burstMain.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(colorA, 0.95f), WithAlpha(colorB, 0.95f));",
        "burst.Play(true);",
        "halo.Play(true);",
    ]:
        if snippet not in flat:
            return fail("烟花绽放必须仍播调色板火花与光晕 -> " + snippet)
    return 0


def main() -> int:
    for check in (check_asset_manager, check_gun_fx_sources, check_firework_keeps_own_colors):
        result = check()
        if result:
            return result
    print("DragonKingBossGunNoWhiteGlowGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
