from pathlib import Path
import sys


ZOMBIE = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print("ZombieModeExtractionCleanupGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = ZOMBIE.read_text(encoding="utf-8")

    method = extract_method(text, "private void CompleteZombieModeExtractionSuccess")
    if not method:
        return fail("CompleteZombieModeExtractionSuccess not found")

    required = [
        "SettleZombieModeExtractionCashShell();",
        "TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea)",
        "CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction);",
    ]
    for token in required:
        if token not in method:
            return fail("missing token -> " + token)

    dispatch_index = method.find("TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea)")
    cleanup_index = method.find("CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction);")
    if cleanup_index < 0 or dispatch_index < 0 or cleanup_index < dispatch_index:
        return fail("cleanup must run after dispatch attempt in success path")

    if "if (!TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea))" in method:
        return fail("cleanup must not be limited to dispatch failure only")

    print("ZombieModeExtractionCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
