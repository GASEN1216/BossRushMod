"""
Guard: 噬魂挽歌左键挥击的自建粒子不能再使用大号 Box 烟雾轮廓。

原因：
- 当前 `BuildCustomScytheParticles` 使用了 `Box` 发射形状和超大 `startSize`
- 这类 billboard 粒子会在挥击时堆出一团雾感，而不是清晰的刀光

要求：
- 不能继续使用 `ParticleSystemShapeType.Box`
- 不能继续保留 `1.5f ~ 2.5f` 这种大号 startSize
- 应改为更小的粒子尺寸轮廓
- （2026-09-23 审查 VB-03）运行时 TintParticles 不能用 `startSizeMultiplier` 改尺寸（两常数随机模式下只改上限，
  0.1–0.2 m 的烟会被拉成 0.1–2 m 的紫雾团）；必须显式写两端、倍率夹在 0.8–1.3，最大不超过 0.35 m；
  女巫目录里任何文件都不再写 startSizeMultiplier。
"""

from pathlib import Path
import re
import sys

from cs_source_util import clean_source


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheSwingFx.cs")


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
    block = extract_block(text, "private void BuildCustomScytheParticles(GameObject node)")
    if "BuildSharedSmokeParticles(node);" in block or not block:
        block = extract_block(text, "private static void BuildSharedSmokeParticles(GameObject node)")
    if not block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: missing swing smoke particle builder block")

    if "ParticleSystemShapeType.Box" in block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: box-shaped swing particles still present")

    if "main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);" in block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: oversized swing particles still present")

    if "main.startSize = new ParticleSystem.MinMaxCurve(0." not in block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: missing reduced small-particle startSize profile")

    # 2026-09-23 审查 VB-03：建造块里的小尺寸会在运行时被 TintParticles 改写。此前那里写
    # `main.startSizeMultiplier = 2.0f * sizeScale`：startSize 是「两常数随机」时它只改上限，
    # 0.10–0.22 m 的烟被拉成 0.10–2 m 的紫雾团，而上面的断言只查建造块，查不到这一行。
    # 现在要求：整个女巫目录不再写 startSizeMultiplier；TintParticles 显式写两端并把倍率夹在 0.8–1.3。
    for path in sorted(Path("Integration/PhantomWitch").glob("*.cs")):
        code = clean_source(path.read_text(encoding="utf-8-sig"))
        if re.search(r"startSizeMultiplier\s*[-+*/]?=", code):
            return fail(
                "PhantomWitchScytheSwingParticleProfileGuard: "
                f"{path.as_posix()} still writes startSizeMultiplier (TwoConstants mode only scales the max)"
            )

    code = clean_source(text)
    tint_block = extract_block(code, "private static void TintParticles(ParticleSystem[] particleSystems, float sizeScale)")
    if not tint_block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: missing runtime TintParticles block")

    clamp = re.search(r"float\s+(\w+)\s*=\s*Mathf\.Clamp\(\s*sizeScale\s*,\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*\)\s*;", tint_block)
    if clamp is None:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: TintParticles must clamp sizeScale before resizing")
    k_name, k_min, k_max = clamp.group(1), float(clamp.group(2)), float(clamp.group(3))
    if k_min < 0.8 or k_max > 1.3 or k_min > k_max:
        return fail(
            "PhantomWitchScytheSwingParticleProfileGuard: TintParticles size factor must stay within 0.8–1.3, "
            f"got {k_min}–{k_max}"
        )

    explicit = re.search(
        r"main\.startSize\s*=\s*new\s+ParticleSystem\.MinMaxCurve\(\s*(\w+)\s*\*\s*" + k_name
        + r"\s*,\s*(\w+)\s*\*\s*" + k_name + r"\s*\)\s*;",
        tint_block,
    )
    if explicit is None:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: TintParticles must scale both startSize ends explicitly")

    sizes = {}
    for name in (explicit.group(1), explicit.group(2)):
        literal = re.search(r"const\s+float\s+" + name + r"\s*=\s*([0-9.]+)f\s*;", code)
        if literal is None:
            return fail(f"PhantomWitchScytheSwingParticleProfileGuard: startSize bound {name} is not a float const")
        sizes[name] = float(literal.group(1))
    low, high = sizes[explicit.group(1)], sizes[explicit.group(2)]
    if not (0 < low <= high) or high * k_max > 0.35:
        return fail(
            "PhantomWitchScytheSwingParticleProfileGuard: runtime swing smoke may reach "
            f"{high * k_max:.2f} m (limit 0.35 m) — this is the purple-fog regression"
        )

    if "StardustEmitterName" not in tint_block:
        return fail("PhantomWitchScytheSwingParticleProfileGuard: TintParticles must leave the stardust emitter's own size profile alone")

    print("PhantomWitchScytheSwingParticleProfileGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
