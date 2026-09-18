"""Guard: Energy Shield frontal check must use the official aim direction and cheap dot math.

两条不变式：
  1. 判据取 CharacterMainControl.CurrentAimDirection（= modelRoot.forward）。
     官方角色的根 Transform 从不旋转（Movement.rotationRoot => modelRoot，官方 Movement.cs:445/464），
     用 transform.forward 会拿到一个恒定的世界方向，"正面吸收"就退化成"只有从世界 +Z 打来才触发"，
     与玩家实际朝向无关——机制与它自称的弱点同时失效，而编译、守卫、实机都不会报错。
  2. 热路径不做 Vector3.Angle / normalized，改用平方比较。
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")


def fail(message: str) -> int:
    print("EnergyShieldFrontalDotGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "private static bool IsFrontalAttack(")
    if body is None:
        return fail("missing IsFrontalAttack body")

    forbidden = [
        "Vector3.Angle(",
        ".normalized",
        # 根节点从不旋转：拿它当朝向等于永远朝世界 +Z
        "transform.forward",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("frontal check uses a forbidden construct -> " + snippet)

    required = [
        "EnergyShieldFrontalAngleCos",
        "Vector3 playerForward = player.CurrentAimDirection;",
        "Vector3.Dot(playerForward, toAttacker)",
        "dot <= 0f",
        "dot * dot >=",
        "playerForwardSqr * toAttackerSqr",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing dot-product frontal check snippet -> " + snippet)

    # 护盾特效的摆放朝向同源，否则环会画在背后
    facing = extract_method_body(text, "private static Vector3 GetFacing(")
    if facing is None:
        return fail("missing GetFacing body")
    if "player.CurrentAimDirection" not in facing:
        return fail("GetFacing must also use CurrentAimDirection")
    if "transform.forward" in facing:
        return fail("GetFacing must not fall back to the never-rotating root transform")

    print("EnergyShieldFrontalDotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
