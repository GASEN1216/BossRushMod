from pathlib import Path
import sys


FACTORY = Path("Utilities/ModeExtractionPointFactory.cs")
COMPILE = Path("compile_official.bat")
MODEF = Path("ModeF/ModeFExtraction.cs")
ZOMBIE = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print(message)
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
    if not FACTORY.exists():
        return fail("ZombieModeExtractionFactoryGuard: missing Utilities/ModeExtractionPointFactory.cs")

    factory = FACTORY.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")
    modef = MODEF.read_text(encoding="utf-8")
    zombie = ZOMBIE.read_text(encoding="utf-8")

    if "Utilities\\ModeExtractionPointFactory.cs" not in compile_text:
        return fail("ZombieModeExtractionFactoryGuard: compile_official.bat does not include ModeExtractionPointFactory")

    for token in [
        "internal sealed class ModeExtractionPointRequest",
        "internal sealed class ModeExtractionPointResult",
        "internal static class ModeExtractionPointFactory",
        "LevelManager.Instance",
        "ExitCreator.exitPrefab",
        "PrepareExtractionPrefab",
        "EnsureTriggerCollider",
        "ConfigureCountDown",
        "ResolveFallbackActions",
        "EvacuationCountdownUI.Request(area)",
        "EvacuationCountdownUI.Release(area)",
    ]:
        if token not in factory:
            return fail("ZombieModeExtractionFactoryGuard: factory missing token -> " + token)

    if "ModeExtractionPointFactory.CreateExtractionPoint(request)" not in modef:
        return fail("ZombieModeExtractionFactoryGuard: Mode F does not use shared extraction factory")

    create = extract_method(factory, "internal static ModeExtractionPointResult CreateExtractionPoint")
    if not (0 <= create.find("ClearPersistentEvents(countDown)")
            < create.find("ResolveFallbackActions(countDown.onCountDownSucceed")
            < create.find("ConfigureEvents(countDown")):
        return fail("ZombieModeExtractionFactoryGuard: 必须按清除后的事件决定通知/返基地兜底，不能依赖已删除回调")
    events = extract_method(factory, "private static void ConfigureEvents")
    if not (0 <= events.find("if (successDispatched") < events.find("successDispatched = true;")
            < events.find("request.OnSucceed();")):
        return fail("ZombieModeExtractionFactoryGuard: 成功事件必须先占有再结算，防止丧尸递归派发导致双重返程")

    for token in [
        "TryCreateModeFExtractionFromPrefab",
        "PrepareModeFExtractionPrefab",
        "EnsureModeFExtractionCollider",
        "ConfigureModeFExtractionCountDown",
        "ResolveModeFExtractionFallbackActions",
    ]:
        if token in modef:
            return fail("ZombieModeExtractionFactoryGuard: Mode F still owns duplicated extraction code -> " + token)

    ensure_area = extract_method(zombie, "private void EnsureZombieModeExtractionArea")
    if not ensure_area:
        return fail("ZombieModeExtractionFactoryGuard: cannot extract EnsureZombieModeExtractionArea")

    if "ModeExtractionPointFactory.CreateExtractionPoint(request)" not in ensure_area:
        return fail("ZombieModeExtractionFactoryGuard: Zombie extraction area does not use shared factory")

    if "GameObject.CreatePrimitive(PrimitiveType.Cylinder)" in ensure_area:
        return fail("ZombieModeExtractionFactoryGuard: Zombie extraction still creates primitive cylinder")

    for token in [
        "zombieModeCountDownAreaBeginCountDownMethod",
        "zombieModeCountDownAreaHoveringMainCharactersField",
        "TryForcePlayerIntoZombieModeExtractionArea",
        "BeginCountDown",
        "hoveringMainCharacters",
    ]:
        if token in zombie:
            return fail("ZombieModeExtractionFactoryGuard: Zombie extraction still touches CountDownArea private countdown state -> " + token)

    print("ZombieModeExtractionFactoryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
