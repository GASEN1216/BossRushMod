"""
Guard: 幽灵女巫 / 噬魂挽歌特效的「廉价感」与预警可读性修复（2026-09-23 审美审查 VB-01…VB-10）。

结构不变式（逐条对应 findings，改结构时同步本守卫）：
- VB-02 材质：女巫目录不再自己 Shader.Find 粒子 / 透明着色器（首选的 Legacy Additive 等在游戏里都不存在），
  线、面片、粒子材质统一取自共享工厂 BossRushFxMaterials；程序化贴图不再用 Alpha8
  （部分图形 API 采样出 rgb = 0，走顶点色 / 属性块上色会变黑）。
- VB-04 Circle 发射器必须放平：Unity 的 Circle 形状默认在局部 XY 平面（竖着）。
- VB-01 领域预警：外圈钉在判定半径（不再用 PhantomWitchShrinkRing 往里缩），填充从圆心长满读时机，
  结算前最后 0.08 s 闪一下。
- VB-07 扇形预警：横扫 / 安魂弧 / 怨灵两段都有出手前的扇形预警，半径 / 半角 / 前移量与判定逐一相等。
- VB-05 生命周期：淡出组件不再跳过粒子渲染器，并在淡出中停发射；建灯必须显式传特效时长。
- VB-09 灯光：强度 ≤ 3.5、半径 ≤ 5 m，起爆倍率 ≤ 1.2。
"""

from pathlib import Path
import re
import sys

from cs_source_util import clean_source


ROOT_DIR = Path("Integration/PhantomWitch")
ASSET = ROOT_DIR / "PhantomWitchAssetManager.cs"
ASSET_RUNTIME = ROOT_DIR / "PhantomWitchAssetManager_RuntimeComponents.cs"
REDESIGN_PARTS = [
    ROOT_DIR / "PhantomWitchVfxRedesign.cs",
    ROOT_DIR / "PhantomWitchVfxRedesign_EmittersAndTextures.cs",
    ROOT_DIR / "PhantomWitchVfxRedesign_RuntimeComponents.cs",
]
SCYTHE_PARTS = [
    ROOT_DIR / "PhantomWitchScytheAction.cs",
    ROOT_DIR / "PhantomWitchScytheAction_RuntimeComponents.cs",
]
AMBIENT = ROOT_DIR / "PhantomWitchAmbientPresence.cs"
SWEAT = ROOT_DIR / "PhantomWitchCurseSweatVfx.cs"
ABILITY_PARTS = sorted(ROOT_DIR.glob("PhantomWitchAbilityController*.cs"))

# 唯一允许的 Shader.Find：武器模型强制换回游戏原版角色着色器（与特效材质无关）。
ALLOWED_SHADER_FIND = {'Shader.Find("SodaCraft/SodaCharacter")'}


def fail(message: str) -> int:
    print("PhantomWitchVfxReadabilityGuard: FAIL - " + message)
    return 1


def read(path: Path) -> str:
    return clean_source(path.read_text(encoding="utf-8-sig"))


def read_all(paths) -> str:
    return "\n".join(read(path) for path in paths)


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


def float_const(text: str, name: str):
    match = re.search(r"const\s+float\s+" + re.escape(name) + r"\s*=\s*([0-9.]+)f\s*;", text)
    return float(match.group(1)) if match else None


def check_materials() -> str | None:
    for path in sorted(ROOT_DIR.glob("*.cs")):
        code = read(path)
        for call in re.findall(r"Shader\.Find\(\s*\"[^\"]*\"\s*\)", code):
            if call not in ALLOWED_SHADER_FIND:
                return f"{path.as_posix()} still resolves its own shader: {call} (use BossRushFxMaterials)"
        if "TextureFormat.Alpha8" in code:
            return f"{path.as_posix()} still builds an Alpha8 texture"

    asset = read(ASSET)
    for signature in (
        "internal static Material GetLineMaterial()",
        "internal static Material GetQuadMaterial()",
        "internal static Material GetGlowQuadMaterial()",
        "internal static Material GetParticleMaterial(BossRushFxBlend blend)",
    ):
        block = extract_block(asset, signature)
        if not block:
            return f"missing {signature}"
        if "BossRushFxMaterials.Get(" not in block:
            return f"{signature} does not route through BossRushFxMaterials.Get"

    # 工厂材质是全 Mod 共享的，女巫侧不得销毁它们（销毁会让其它系统拿到坏材质）。
    finalize = extract_block(asset, "private static void FinalizeCacheCleanup()")
    if re.search(r"Destroy\(\s*cached(Line|Quad|Particle)Material\s*\)", finalize):
        return "FinalizeCacheCleanup still destroys shared factory materials"
    return None


def check_circle_emitters() -> str | None:
    redesign = read_all(REDESIGN_PARTS)
    scythe = read_all(SCYTHE_PARTS)
    ambient = read(AMBIENT)
    flat = "shape.rotation = new Vector3(90f, 0f, 0f);"
    targets = [
        (redesign, "private static ParticleSystem CreateSoulMistEmitter"),
        (redesign, "private static ParticleSystem CreateSoulFlameEmitter"),
        (scythe, "private static void CreateRisingWisps"),
        (scythe, "private static void CreateOrbitSparks"),
        (ambient, "private void CreateVeilParticles()"),
        (ambient, "private void CreateGroundMistParticles()"),
    ]
    for text, signature in targets:
        block = extract_block(text, signature)
        if not block:
            return f"missing emitter block {signature}"
        if "ParticleSystemShapeType.Circle" in block and flat not in block:
            return f"{signature} uses a Circle shape without laying it flat (Circle defaults to the vertical XY plane)"

    orbit = extract_block(scythe, "private static void CreateOrbitSparks")
    if "orbitalY" not in orbit:
        return "orbit sparks no longer orbit along the realm edge"

    smoke = extract_block(scythe, "private static void CreateBlackSmokeEmitter(")
    if "velocity.space = ParticleSystemSimulationSpace.World;" not in smoke:
        return "realm black smoke is rotated flat but still drifts in Local space (local +Y points away from the camera)"
    return None


def check_realm_warning() -> str | None:
    redesign = read_all(REDESIGN_PARTS)
    warning = extract_block(redesign, "internal static GameObject CreateCurseRealmWarningCircle")
    if not warning:
        return "missing CreateCurseRealmWarningCircle"
    if "PhantomWitchShrinkRing" in warning:
        return "curse realm warning boundary shrinks again (reads as 'stand outside the shrinking ring')"
    if re.search(r"CreateCircleTelegraph\(\s*root\.transform\s*,\s*safeRadius\s*,\s*safeDuration\s*\)", warning) is None:
        return "curse realm warning no longer builds its telegraph at the judgement radius / duration"

    circle = extract_block(redesign, "private static void CreateCircleTelegraph(")
    if "driver.SetRingOutline(ring, radius);" not in circle or "driver.SetFill(" not in circle:
        return "circle telegraph must pin the outline at radius and drive a growing fill"

    runtime = read(ROOT_DIR / "PhantomWitchVfxRedesign_RuntimeComponents.cs")
    driver = extract_block(runtime, "internal sealed class PhantomWitchTelegraphDriver")
    if not driver:
        return "missing PhantomWitchTelegraphDriver"
    flash = float_const(driver, "FlashDuration")
    if flash is None or abs(flash - 0.08) > 1e-6:
        return "telegraph flash window must stay 0.08 s"
    if "outlineRing.SetShape(outlineRingRadius, width);" not in driver:
        return "telegraph ring radius is no longer pinned (only width may animate)"
    if not re.search(r"fillFullScale\.x\s*\*\s*grow", driver) or "float grow = Mathf.Max(0.001f, charge);" not in driver:
        return "telegraph fill must grow linearly with charge time"
    return None


def cone_calls(text: str):
    pattern = re.compile(
        r"CreateConeTelegraph(Outline)?\(\s*bossCharacter\.transform\s*,\s*[^,]+,\s*([^,]+?)\s*,\s*([^,]+?)\s*,\s*([^,]+?)\s*,",
        re.S,
    )
    return [(m.group(2).strip(), m.group(3).strip(), m.group(4).strip()) for m in pattern.finditer(text)]


def damage_calls(text: str):
    pattern = re.compile(r"DealConeDamage\(\s*([^,]+?)\s*,\s*([^,]+?)\s*,\s*[^,]+,\s*[^,]+,\s*([^,]+?)\s*,", re.S)
    return [(m.group(1).strip(), m.group(2).strip(), m.group(3).strip()) for m in pattern.finditer(text)]


def check_cone_telegraphs() -> str | None:
    ability = read_all(ABILITY_PARTS)
    packages = [
        ("private IEnumerator ExecuteScytheSweep()", "private IEnumerator ExecuteImmediateScytheSweep(CharacterMainControl target)"),
        ("private IEnumerator ExecuteMidrangeRequiemPackage()", None),
        ("private IEnumerator ExecuteWraithTrailObservePackage()", None),
    ]
    for signature, damage_signature in packages:
        block = extract_block(ability, signature)
        if not block:
            return f"missing {signature}"
        telegraphs = cone_calls(block)
        if not telegraphs:
            return f"{signature} has no cone telegraph before the hit"
        damage_block = extract_block(ability, damage_signature) if damage_signature else block
        damages = damage_calls(damage_block)
        if not damages:
            return f"cannot locate DealConeDamage for {signature}"
        # 预警的 (半径, 半角, 前移量) 必须与每一段判定逐字相等：只改表现，判定一字不动。
        if sorted(telegraphs) != sorted(damages):
            return f"{signature} telegraph {telegraphs} does not match judgement {damages}"

    asset = read(ASSET)
    for signature in (
        "internal static GameObject CreateConeTelegraph(",
        "internal static GameObject CreateConeTelegraphOutline(",
    ):
        block = extract_block(asset, signature)
        if "ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical)" not in block:
            return f"{signature} must be Critical (telegraphs are never skipped by the detail policy)"
    return None


def check_lifecycle_and_lights() -> str | None:
    runtime = read(ASSET_RUNTIME)
    fade = extract_block(runtime, "internal sealed class PhantomWitchFadeDestroy")
    if "renderer is ParticleSystemRenderer" in fade:
        return "PhantomWitchFadeDestroy skips particle renderers again (particles pop at destroy)"
    if "ParticleSystemStopBehavior.StopEmitting" not in fade:
        return "PhantomWitchFadeDestroy must stop emission while fading"
    if "PhantomWitchVfxRecycler" not in fade:
        return "PhantomWitchFadeDestroy must leave pooled roots to the recycler"

    scythe = read_all(SCYTHE_PARTS)
    fader = extract_block(scythe, "internal sealed class PhantomWitchCurseRealmFader")
    if "r is ParticleSystemRenderer" in fader or "StopEmitting" not in fader:
        return "curse realm fader must fade particle renderers and stop emission"
    if "FadeMultiplier" not in fader:
        return "curse realm fader must scale the core pulse instead of letting it overwrite alpha"

    redesign = read_all(REDESIGN_PARTS)
    light = extract_block(redesign, "private static GameObject CreatePointLight(")
    if not light:
        return "missing CreatePointLight"
    if re.search(r"float\s+duration\s*=", light.split("{", 1)[0]):
        return "CreatePointLight has a default duration again (callers must pass the root lifetime)"
    max_intensity = float_const(redesign, "MaxLightIntensity")
    max_range = float_const(redesign, "MaxLightRange")
    if max_intensity is None or max_intensity > 3.5 or max_range is None or max_range > 5.0:
        return f"witch light caps drifted (intensity {max_intensity}, range {max_range}; limits 3.5 / 5 m)"
    if "Mathf.Clamp(intensity, 0f, MaxLightIntensity)" not in light or "Mathf.Clamp(range, 0.5f, MaxLightRange)" not in light:
        return "CreatePointLight no longer clamps intensity / range"

    runtime_redesign = read(ROOT_DIR / "PhantomWitchVfxRedesign_RuntimeComponents.cs")
    pulse = extract_block(runtime_redesign, "internal sealed class PhantomWitchLightPulse")
    burst = float_const(pulse, "BurstIntensityScale")
    if burst is None or burst > 1.2:
        return "light pulse burst must stay <= 1.2x"
    if "targetLight.enabled = false;" not in pulse:
        return "light pulse must switch the light off when it has faded out"
    return None


def main() -> int:
    for check in (check_materials, check_circle_emitters, check_realm_warning, check_cone_telegraphs, check_lifecycle_and_lights):
        error = check()
        if error is not None:
            return fail(error)

    print("PhantomWitchVfxReadabilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
