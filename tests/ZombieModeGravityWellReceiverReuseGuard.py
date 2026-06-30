"""Guard: gravity well AI targeting should reuse the player's damage receiver."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeRewardProjectileSpread.cs")


def fail(message: str) -> int:
    print("ZombieModeGravityWellReceiverReuseGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "internal void RefreshZombieModeGravityWellTargets(")
    if body is None:
        return fail("missing RefreshZombieModeGravityWellTargets body")

    required = [
        "DamageReceiver playerDamageReceiver = player != null ? player.mainDamageReceiver : null;",
        "if (ai != null && playerDamageReceiver != null)",
        "ai.searchedEnemy = playerDamageReceiver;",
        "try { ai.SetTarget(playerDamageReceiver.transform); } catch { }",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing receiver reuse snippet -> " + snippet)

    loop_start = body.find("for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)")
    if loop_start < 0:
        return fail("missing gravity well marker loop")

    declaration = body.find(required[0])
    if declaration < 0 or declaration > loop_start:
        return fail("playerDamageReceiver should be captured before the marker loop")

    loop_body = body[loop_start:]
    if "player.mainDamageReceiver" in loop_body:
        return fail("marker loop still rereads player.mainDamageReceiver")

    print("ZombieModeGravityWellReceiverReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
