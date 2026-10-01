"""
Guard: Mode E healthbar name override must cache desired text per healthbar
target instead of rebuilding suffix strings every throttled LateUpdate pass.

玩家血条（forceShowName）每帧都会进补丁（原版 LateUpdate 每帧改它）：只有玩家名或玩家阵营变了才重拼
「名字 + 阵营后缀」，不允许 needsRebuild 里再出现无条件的 forceShowName（2026-10-01 发布前审查 P3）。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source


SOURCES = [
    Path("ModeE/ModeE.cs"),
    Path("ModeE/ModeEUiAndHealthBars.cs"),
    Path("ModeE/ModeEStartup.cs"),
    Path("ModeE/ModeELifecycle.cs"),
    Path("ModeE/ModeEIntegrityAndHelpers.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def read_mode_e_sources() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in SOURCES)


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
    text = read_mode_e_sources()

    for required in (
        "modeEHealthBarBaseTextByBarId",
        "modeEHealthBarDesiredTextByBarId",
        "modeEHealthBarTargetIdsByBarId",
        "modeEHealthBarAppliedVersionByBarId",
        "modeEHealthBarNameVersion",
        "BuildModeEDesiredHealthBarText",
        "ClearModeEHealthBarOverrideCache",
    ):
        if required not in text:
            return fail(f"ModeEHealthBarNameCacheGuard: missing healthbar cache invariant -> {required}")

    reset_body = extract_method_body(text, "private void ResetModeEUiCaches()")
    if reset_body is None:
        return fail("ModeEHealthBarNameCacheGuard: missing ResetModeEUiCaches body")

    for required in (
        "modeEHealthBarBaseTextByBarId.Clear()",
        "modeEHealthBarDesiredTextByBarId.Clear()",
        "modeEHealthBarTargetIdsByBarId.Clear()",
        "modeEHealthBarAppliedVersionByBarId.Clear()",
    ):
        if required not in reset_body:
            return fail(f"ModeEHealthBarNameCacheGuard: ResetModeEUiCaches lacks -> {required}")

    apply_body = extract_method_body(text, "internal void ApplyModeEHealthBarNameOverride")
    if apply_body is None:
        return fail("ModeEHealthBarNameCacheGuard: missing ApplyModeEHealthBarNameOverride body")

    for required in (
        "modeEHealthBarDesiredTextByBarId.TryGetValue",
        "modeEHealthBarAppliedVersionByBarId.TryGetValue",
        "modeEHealthBarTargetIdsByBarId.TryGetValue",
        "BuildModeEDesiredHealthBarText(",
    ):
        if required not in apply_body:
            return fail(f"ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride lacks -> {required}")

    if "StripModeEFactionSuffix(" in apply_body:
        return fail("ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride still strips suffixes directly")

    # 玩家血条：按输入变化重拼，不再每帧无条件重拼（剥注释、规范空白后按完整语句匹配）
    clean_apply = " ".join(clean_source(apply_body).split())
    rebuild = re.search(r"bool needsRebuild =([^;]*);", clean_apply)
    if rebuild is None:
        return fail("ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride lacks needsRebuild expression")
    terms = [term.strip() for term in rebuild.group(1).split("||")]
    if "forceShowName" in terms:
        return fail("ModeEHealthBarNameCacheGuard: player bar still rebuilds name string every frame (bare forceShowName in needsRebuild)")
    if "playerInputsChanged" not in terms:
        return fail("ModeEHealthBarNameCacheGuard: needsRebuild lacks playerInputsChanged term")
    for required in (
        "playerInputsChanged = !string.Equals(modeEPlayerBarBuiltName, GetModeEPlayerName(), StringComparison.Ordinal) || modeEPlayerBarBuiltFaction != ModeEPlayerFaction;",
        "if (forceShowName) { modeEPlayerBarBuiltName = GetModeEPlayerName(); modeEPlayerBarBuiltFaction = ModeEPlayerFaction; }",
    ):
        if required not in clean_apply:
            return fail(f"ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride lacks player-bar input cache -> {required}")

    clean_reset = " ".join(clean_source(reset_body).split())
    for required in ("modeEPlayerBarBuiltName = null;", "modeEPlayerBarBuiltFaction = null;"):
        if required not in clean_reset:
            return fail(f"ModeEHealthBarNameCacheGuard: ResetModeEUiCaches lacks -> {required}")

    print("ModeEHealthBarNameCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
