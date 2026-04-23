"""
Guard: 噬魂挽歌左键挥击的自建粒子不能再使用大号 Box 烟雾轮廓。

原因：
- 当前 `BuildCustomScytheParticles` 使用了 `Box` 发射形状和超大 `startSize`
- 这类 billboard 粒子会在挥击时堆出一团雾感，而不是清晰的刀光

要求：
- 不能继续使用 `ParticleSystemShapeType.Box`
- 不能继续保留 `1.5f ~ 2.5f` 这种大号 startSize
- 应改为更小的粒子尺寸轮廓
"""

from pathlib import Path
import sys


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

    print("PhantomWitchScytheSwingParticleProfileGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
