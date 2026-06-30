"""Guard: Dragon King AoE receiver loops should reuse receiver instance IDs."""

from pathlib import Path
import sys


SOURCES = [
    (
        Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs"),
        "internal static void ApplyRadiusDamage(",
        "SharedReceiverIdSet",
    ),
    (
        Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs"),
        "private void TickZone()",
        "DragonKingBossGunRuntime.SharedReceiverIdSet",
    ),
    (
        Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs"),
        "private void ApplyDeathBuff(Buff buff, float radius)",
        "DragonKingBossGunRuntime.SharedReceiverIdSet",
    ),
]


def fail(message: str) -> int:
    print("DragonKingReceiverIdReuseGuard: FAIL - " + message)
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
    for source, signature, set_name in SOURCES:
        text = source.read_text(encoding="utf-8-sig")
        body = extract_method_body(text, signature)
        if body is None:
            return fail(f"missing method body in {source}: {signature}")

        if f"{set_name}.Contains(receiver.GetInstanceID())" in body:
            return fail(f"{source} still calls GetInstanceID inside Contains")

        if f"{set_name}.Add(receiver.GetInstanceID())" in body:
            return fail(f"{source} still calls GetInstanceID inside Add")

        if "int receiverId = receiver.GetInstanceID();" not in body:
            return fail(f"{source} missing cached receiverId")

        if f"{set_name}.Contains(receiverId)" not in body:
            return fail(f"{source} missing cached receiverId Contains")

        if f"{set_name}.Add(receiverId)" not in body:
            return fail(f"{source} missing cached receiverId Add")

    print("DragonKingReceiverIdReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
