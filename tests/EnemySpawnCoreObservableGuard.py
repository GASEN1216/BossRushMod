"""Guard: SpawnEnemyCore must expose observable internal completion without changing legacy callers."""

from pathlib import Path
import sys


SOURCE = Path("Utilities/EnemySpawnCore.cs")


def fail(message: str) -> int:
    print("EnemySpawnCoreObservableGuard: FAIL - " + message)
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


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def forbid(text: str, needle: str, message: str) -> int | None:
    if needle in text:
        return fail(message)
    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    for needle, message in (
        ("internal sealed class EnemySpawnCoreResult", "spawn core result object must exist"),
        ("public bool success;", "spawn core result must expose success"),
        ("public EnemySpawnContext context;", "spawn core result must expose context"),
        ("public string failureReason;", "spawn core result must expose failure reason"),
        ("public EnemyPresetInfo actualPreset;", "spawn core result must expose actual preset"),
        ("private void SpawnEnemyCore(", "legacy SpawnEnemyCore wrapper must remain for existing callers"),
        ("SpawnEnemyCoreFireAndForgetAsync(", "legacy wrapper must stay fire-and-forget"),
        ("private async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync", "internal spawn core must be awaitable"),
        ("const int maxAttempts = 5;", "spawn core retry count must stay 5"),
        ("await UniTask.Yield();", "ordinary spawn path must keep the existing yield"),
        ("SpawnDragonDescendant(", "dragon descendant path must remain in shared spawn core"),
        ("SpawnDragonKing(", "dragon king path must remain in shared spawn core"),
        ("SpawnPhantomWitch(", "phantom witch path must remain in shared spawn core"),
        ("EquipEnemyForModeD(character, waveIndex, currentPreset.baseHealth, isBoss);", "equipment path must keep existing condition target"),
        ("ApplyBossStatMultiplier(character);", "boss multiplier path must keep existing condition target"),
        ("NormalizeDamageMultiplier(character);", "damage normalization must remain in spawn core"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    wrapper = extract_method_body(text, "private void SpawnEnemyCore")
    if wrapper is None:
        return fail("missing legacy SpawnEnemyCore wrapper")
    result = forbid(wrapper, "async void", "legacy wrapper must not be async void")
    if result is not None:
        return result

    fire_and_forget = extract_method_body(text, "private async UniTaskVoid SpawnEnemyCoreFireAndForgetAsync")
    if fire_and_forget is None:
        return fail("missing fire-and-forget adapter")
    for needle, message in (
        ("onSpawned(result.context);", "success callback must still be invoked for legacy callers"),
        ("InvokeSpawnCoreFailureCallback(onFailed, reason);", "failure callback must still be invoked for legacy callers"),
    ):
        result = require(fire_and_forget, needle, message)
        if result is not None:
            return result

    internal_body = extract_method_body(text, "private async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync")
    if internal_body is None:
        return fail("missing internal awaitable body")
    for needle, message in (
        ("return EnemySpawnCoreResult.Succeeded(ctx, currentPreset);", "internal body must return success results"),
        ("return EnemySpawnCoreResult.Failed(\"模式结束\", currentPreset);", "internal body must return mode-ended failures"),
        ("return EnemySpawnCoreResult.Failed(\"重试耗尽\", currentPreset);", "internal body must return exhausted-retry failures"),
        ("return EnemySpawnCoreResult.Failed(\"主流程异常\", preset);", "internal body must return exception failures"),
    ):
        result = require(internal_body, needle, message)
        if result is not None:
            return result

    print("EnemySpawnCoreObservableGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
