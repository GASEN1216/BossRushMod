"""Guard: Zombie mode threat affix runtime should sample unscaled time once per Update."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModeThreatRuntimeTimeCacheGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def extract_method_body(text: str, signature: str) -> str | None:
    block = extract_block(text, signature)
    if block is None:
        return None

    brace_start = block.find("{")
    return block[brace_start:] if brace_start >= 0 else None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    runtime = extract_block(text, "public sealed class ZombieModeThreatRuntime")
    if runtime is None:
        return fail("missing ZombieModeThreatRuntime")

    update = extract_method_body(runtime, "private void Update()")
    if update is None:
        return fail("missing Update")

    required = [
        "float currentTime = Time.unscaledTime;",
        "pauseStartTime = currentTime;",
        "float pausedDuration = Mathf.Max(0f, currentTime - pauseStartTime);",
        "if (currentTime < nextSkillTime)",
        "nextSkillTime = currentTime + cooldown + UnityEngine.Random.Range(0f, 2f);",
    ]
    for token in required:
        if token not in update:
            return fail("Update missing single-time-sample token -> " + token)

    if update.count("Time.unscaledTime") != 1:
        return fail("Update should read Time.unscaledTime exactly once")

    print("ZombieModeThreatRuntimeTimeCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
