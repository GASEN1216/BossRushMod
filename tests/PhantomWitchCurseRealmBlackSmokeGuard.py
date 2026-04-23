"""
Guard: 噬魂挽歌右键法阵必须包含整区黑烟覆盖层，并且覆盖范围不能缩水。

要求：
- `PhantomWitchCurseRealmVisual.Create(...)` 必须调用 `CreateAreaBlackSmoke(...)`
- 覆盖层 helper 必须存在于 `PhantomWitchScytheAction.cs`
- 覆盖层粒子必须使用 `Circle` 形状以铺满法阵区域，而不是只在边缘冒烟
- 覆盖 helper 需要至少创建两层黑烟 emitter，避免单层随机分布留下空洞
- 覆盖半径必须不小于法阵半径
- 覆盖 emitter 必须被旋转到水平地面平面，否则会变成一条线
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheAction.cs")


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

    create_block = extract_block(text, "internal static GameObject Create(Vector3 origin, float radius, float duration)")
    if not create_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: missing Create(...) block")

    if "CreateAreaBlackSmoke(root.transform, radius, duration, detailLevel);" not in create_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: curse realm create path does not add area black smoke")

    helper_block = extract_block(
        text,
        "private static void CreateAreaBlackSmoke(Transform parent, float radius, float duration, PhantomWitchFxDetailLevel detailLevel)",
    )
    if not helper_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: missing CreateAreaBlackSmoke helper")

    if helper_block.count("CreateBlackSmokeEmitter(") < 2:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: curse realm black smoke is still using fewer than two emitters")

    emitter_block = extract_block(
        text,
        "private static void CreateBlackSmokeEmitter(",
    )
    if not emitter_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: missing CreateBlackSmokeEmitter helper")

    if "shape.shapeType = ParticleSystemShapeType.Circle;" not in emitter_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: black smoke emitter is not using circle area coverage")

    if "go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);" not in emitter_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: black smoke emitter is not rotated flat onto the ground plane")

    if "radius * 0.92f" in helper_block:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: black smoke radius still shrinks below the realm radius")

    radius_matches = re.findall(r"radius\s*\*\s*([0-9.]+)f", helper_block)
    if not radius_matches:
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: black smoke helper is not scaled from realm radius")

    if not any(float(value) >= 1.0 for value in radius_matches):
        return fail("PhantomWitchCurseRealmBlackSmokeGuard: no black smoke emitter reaches the full realm radius")

    print("PhantomWitchCurseRealmBlackSmokeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
