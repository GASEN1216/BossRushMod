from pathlib import Path
import sys


EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print("ZombieModeExtractionSceneTransitionGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str):
    if needle not in text:
        raise AssertionError(message + " -> " + needle)


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
    extraction = EXTRACTION.read_text(encoding="utf-8")

    try:
        ensure_area = extract_method(extraction, "private void EnsureZombieModeExtractionArea")
        if not ensure_area:
            return fail("cannot extract EnsureZombieModeExtractionArea")

        require(
            ensure_area,
            "request.OnSucceed",
            "ZombieMode extraction must keep its own success bridge attached to the shared extraction point",
        )
        require(
            ensure_area,
            "request.OnFallbackNotify = TryNotifyZombieModeExtractionFromFactory;",
            "ZombieMode extraction factory fallback must bind through a Vector3-aware adapter",
        )

        complete_success = extract_method(extraction, "private void CompleteZombieModeExtractionSuccess")
        if not complete_success:
            return fail("cannot extract CompleteZombieModeExtractionSuccess")

        require(
            complete_success,
            "if (!TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea))",
            "ZombieMode success flow must dispatch the CountDownArea success chain before manual scene-transition fallback",
        )
        require(
            complete_success,
            "TryNotifyZombieModeExtraction();",
            "ZombieMode success flow must keep a manual NotifyEvacuated fallback when event dispatch fails",
        )
        require(
            complete_success,
            "TryLoadBaseSceneAfterZombieModeExtraction();",
            "ZombieMode success flow must keep a manual LoadBaseScene fallback when event dispatch fails",
        )

        dispatch_success = extract_method(extraction, "private bool TryDispatchZombieModeExtractionSuccess")
        if not dispatch_success:
            return fail("cannot extract TryDispatchZombieModeExtractionSuccess")

        require(
            dispatch_success,
            "area.onCountDownStopped.Invoke(area);",
            "ZombieMode extraction success dispatch must mirror Duckov CountDownArea stop notification",
        )
        require(
            dispatch_success,
            "area.onCountDownSucceed.Invoke();",
            "ZombieMode extraction success dispatch must run CountDownArea success listeners",
        )

        factory_notify = extract_method(extraction, "private void TryNotifyZombieModeExtractionFromFactory")
        if not factory_notify:
            return fail("cannot extract TryNotifyZombieModeExtractionFromFactory")

        require(
            factory_notify,
            "TryNotifyZombieModeExtraction(position);",
            "ZombieMode Vector3-aware fallback adapter must forward the factory position into the shared notify helper",
        )
    except AssertionError as exc:
        return fail(str(exc))

    print("ZombieModeExtractionSceneTransitionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
