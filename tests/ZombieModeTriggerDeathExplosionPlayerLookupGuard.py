"""Guard: zombie reward death/explosion trigger paths should reuse player lookups."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeRewardTriggerEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeTriggerDeathExplosionPlayerLookupGuard: FAIL - " + message)
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
                return text[brace_start:idx + 1]

    return None


def require(body: str, snippet: str) -> int | None:
    if snippet not in body:
        return fail("missing player reuse snippet -> " + snippet)
    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    dead_body = extract_method_body(text, "private void HandleZombieModeOptionHealthDead(")
    if dead_body is None:
        return fail("missing HandleZombieModeOptionHealthDead body")
    if dead_body.count("CharacterMainControl.Main") != 1:
        return fail("HandleZombieModeOptionHealthDead should read CharacterMainControl.Main exactly once")
    for snippet in [
        "CharacterMainControl player = CharacterMainControl.Main;",
        "damageInfo.fromCharacter != player",
        "player.Health.AddHealth",
    ]:
        result = require(dead_body, snippet)
        if result is not None:
            return result

    explosion_body = extract_method_body(text, "private void CreateZombieModeOptionExplosion(")
    if explosion_body is None:
        return fail("missing CreateZombieModeOptionExplosion body")
    if explosion_body.count("CharacterMainControl.Main") != 1:
        return fail("CreateZombieModeOptionExplosion should read CharacterMainControl.Main exactly once")
    for snippet in [
        "CharacterMainControl player = CharacterMainControl.Main;",
        "if (!IsZombieModeRunValid(runId))",
        "if (player == null)",
        "DamageInfo info = new DamageInfo(player);",
        "Vector3 normal = player.transform.position - position;",
        "info.isFromBuffOrEffect = true;",
    ]:
        result = require(explosion_body, snippet)
        if result is not None:
            return result

    if "CharacterMainControl.Main.transform" in dead_body or "CharacterMainControl.Main.transform" in explosion_body:
        return fail("death/explosion trigger path still dereferences CharacterMainControl.Main.transform")

    print("ZombieModeTriggerDeathExplosionPlayerLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
