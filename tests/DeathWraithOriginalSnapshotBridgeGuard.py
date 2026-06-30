from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")
SYSTEM = Path("Integration/DeathWraith/DeathWraithSystem.cs")
RECORDING = Path("Integration/DeathWraith/DeathWraithRecording.cs")
BRIDGE = Path("Integration/DeathWraith/DeathWraithOriginalDeadBodyBridge.cs")
PATCH = Path("Patches/Death/DeadBodyAppendPatch.cs")


def fail(message: str) -> int:
    print("DeathWraithOriginalSnapshotBridgeGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
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
    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    system_text = SYSTEM.read_text(encoding="utf-8", errors="ignore")
    recording_text = RECORDING.read_text(encoding="utf-8", errors="ignore")
    bridge_text = BRIDGE.read_text(encoding="utf-8", errors="ignore")
    patch_text = PATCH.read_text(encoding="utf-8", errors="ignore")

    for token in [
        "Integration\\DeathWraith\\DeathWraithOriginalDeadBodyBridge.cs",
        "Patches\\Death\\DeadBodyAppendPatch.cs",
    ]:
        if token not in compile_text:
            return fail("compile_official missing token -> " + token)

    for token in [
        "internal sealed class PendingDeathWraithContext_DeathWraith",
        "internal sealed class OriginalDeadBodyInfo_DeathWraith",
        "private PendingDeathWraithContext_DeathWraith pendingDeathWraithContext;",
        "private OriginalDeadBodyInfo_DeathWraith pendingOriginalDeadBodyInfo_DeathWraith;",
    ]:
        if token not in system_text:
            return fail("DeathWraithSystem missing token -> " + token)

    for token in [
        '[HarmonyPatch(typeof(DeadBodyManager), "AppendDeathInfo")]',
        "NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith",
    ]:
        if token not in patch_text:
            return fail("append bridge patch missing token -> " + token)

    for token in [
        "NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith",
        "CreateOriginalDeadBodyInfo_DeathWraith",
        "StorePendingOriginalDeadBodyInfo_DeathWraith",
        "GetPendingOriginalDeadBodyInfo_DeathWraith",
        "TryFinalizePendingDeathWraithRecord_DeathWraith",
        "BuildWraithInfoFromPendingContext_DeathWraith",
        "fallback death-wraith snapshot recorded",
    ]:
        if token not in bridge_text:
            return fail("bridge file missing token -> " + token)

    record_method = extract_method(
        recording_text,
        "private void RecordDeathWraithDataForMainCharacter_DeathWraith(",
    )
    if not record_method:
        return fail("missing RecordDeathWraithDataForMainCharacter_DeathWraith")

    for token in [
        "GetPendingDeathWraithContext_DeathWraith",
        "BuildPendingDeathWraithContext_DeathWraith",
        "StorePendingDeathWraithInfo_DeathWraith(context);",
        "TryFinalizePendingDeathWraithRecord_DeathWraith(source);",
    ]:
        if token not in record_method:
            return fail("record method missing token -> " + token)

    if "AppendStoredDeathWraithInfo_DeathWraith(info);" in record_method:
        return fail("record method should not directly append old in-frame snapshot anymore")

    if "WraithInfo info = null;" in record_method:
        return fail("record method still contains stale unreachable WraithInfo placeholder")

    print("DeathWraithOriginalSnapshotBridgeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
