#!/usr/bin/env python3
"""好感度运行时必须由唯一 AffinityRuntimeModule 拥有事件与生命周期。"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "Integration/Affinity/AffinityRuntimeHooks.cs"
BRIDGE_PATH = ROOT / "Integration/Affinity/AffinityRuntimeModuleHostBridge.cs"
REGISTRATION_PATH = ROOT / "Common/Lifecycle/BossRushRuntimeModuleRegistration.cs"
COMPILE_PATH = ROOT / "compile_official.bat"
ALWAYS_ON_PATH = ROOT / "Utilities/AlwaysOnRuntimeHooks.cs"


def fail(message):
    print("AffinityRuntimeModuleGuard: FAIL: " + message)
    return 1


def method_body(source, signature):
    start = source.find(signature)
    if start < 0:
        return ""
    opening = source.find("{", start + len(signature))
    if opening < 0:
        return ""

    depth = 0
    quoted = None
    escaped = False
    for index in range(opening, len(source)):
        char = source[index]
        if quoted is not None:
            if escaped:
                escaped = False
            elif char == "\\" and quoted == '"':
                escaped = True
            elif char == quoted:
                quoted = None
            continue

        if char in ('"', "'"):
            quoted = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    return ""


def require_order(body, tokens, label):
    positions = [body.find(token) for token in tokens]
    if any(position < 0 for position in positions):
        missing = [token for token, position in zip(tokens, positions) if position < 0]
        return label + " missing: " + ", ".join(missing)
    if positions != sorted(positions):
        return label + " changed required call order: " + " -> ".join(tokens)
    return ""


def main():
    module = clean_source(MODULE_PATH.read_text(encoding="utf-8", errors="ignore"))
    bridge = clean_source(BRIDGE_PATH.read_text(encoding="utf-8", errors="ignore"))
    registration = clean_source(REGISTRATION_PATH.read_text(encoding="utf-8", errors="ignore"))
    compile_text = COMPILE_PATH.read_text(encoding="utf-8", errors="ignore").replace("\\", "/")
    always_on = clean_source(ALWAYS_ON_PATH.read_text(encoding="utf-8", errors="ignore"))

    if "internal sealed class AffinityRuntimeModule : BossRushRuntimeModuleBase" not in module:
        return fail("AffinityRuntimeHooks.cs must contain the dedicated AffinityRuntimeModule")
    if "partial class ModBehaviour" in module:
        return fail("AffinityRuntimeHooks.cs must no longer own a ModBehaviour partial")

    attach = method_body(module, "public override void OnAwake(ModBehaviour owner)")
    if not attach or "_owner = owner;" not in attach or "owner.AttachAffinityRuntimeModule(this);" not in attach:
        return fail("OnAwake must attach this unique module instance to its host bridge")

    initialize = method_body(module, "internal void InitializeAffinitySystem()")
    error = require_order(
        initialize,
        [
            "AffinityManager.Initialize();",
            "NPCModuleRegistry.RegisterAffinityConfigs();",
            "AffinityManager.OnAffinityChanged += OnAffinityChanged;",
            "AffinityManager.OnLevelUp += OnAffinityLevelUp;",
        ],
        "InitializeAffinitySystem",
    )
    if error:
        return fail(error)
    for token in (
        "if (!_affinityChangedSubscribed)",
        "_affinityChangedSubscribed = true;",
        "if (!_levelUpSubscribed)",
        "_levelUpSubscribed = true;",
    ):
        if token not in initialize:
            return fail("InitializeAffinitySystem must use per-event idempotent subscription state: " + token)

    changed = method_body(module, "private void OnAffinityChanged(string npcId, int oldPoints, int newPoints)")
    error = require_order(
        changed,
        [
            "AffinityUIManager.ShowAffinityChange(npcId, delta);",
            "_owner.HandleSpouseFollowAffinityLoss(npcId);",
            "_owner.RefreshSpouseInteractionOptionsForNpc(npcId);",
        ],
        "OnAffinityChanged",
    )
    if error:
        return fail(error)
    if "if (!string.IsNullOrEmpty(npcId) && _owner != null)" not in changed:
        return fail("OnAffinityChanged must guard the spouse compatibility calls")

    level_up = method_body(module, "private void OnAffinityLevelUp(string npcId, int newLevel)")
    if not level_up or "AffinityUIManager.ShowLevelUpNotification(npcId, newLevel);" not in level_up:
        return fail("OnAffinityLevelUp must keep the existing level notification")

    tick = method_body(module, "internal void TickAffinityRuntime()")
    if tick.strip() != "AffinityManager.UpdateDeferredSave();":
        return fail("TickAffinityRuntime must own only the deferred affinity save tick")
    scene_unload = method_body(module, "internal void OnAffinitySceneUnload()")
    error = require_order(
        scene_unload,
        ["AffinityUIManager.OnSceneUnload();", "AffinityManager.OnSceneUnload();"],
        "OnAffinitySceneUnload",
    )
    if error:
        return fail(error)

    cleanup = method_body(module, "internal void Cleanup()")
    error = require_order(
        cleanup,
        [
            "AffinityManager.OnAffinityChanged -= OnAffinityChanged;",
            "AffinityManager.OnLevelUp -= OnAffinityLevelUp;",
            "AffinityManager.Shutdown();",
            "AffinityManager.ResetStaticCaches();",
            "AffinityUIManager.Cleanup();",
        ],
        "Cleanup",
    )
    if error:
        return fail(error)
    if "if (_cleanupCompleted)" not in cleanup or "_cleanupCompleted = true;" not in cleanup:
        return fail("Cleanup must make the host cleanup forwarding and OnDestroy idempotent")
    for token in (
        "if (_affinityChangedSubscribed)",
        "_affinityChangedSubscribed = false;",
        "if (_levelUpSubscribed)",
        "_levelUpSubscribed = false;",
    ):
        if token not in cleanup:
            return fail("Cleanup must unsubscribe only this module's recorded event delegates: " + token)

    destroy = method_body(module, "public override void OnDestroy()")
    error = require_order(
        destroy,
        ["Cleanup();", "owner.DetachAffinityRuntimeModule(this);", "_owner = null;"],
        "OnDestroy",
    )
    if error:
        return fail(error)

    for signature, target in (
        ("private void InitializeAffinitySystem()", "affinityRuntime.InitializeAffinitySystem();"),
        ("internal void TickAffinityRuntimeFromHost()", "affinityRuntime.TickAffinityRuntime();"),
        ("internal void OnSceneUnloadAffinityRuntimeFromHost()", "affinityRuntime.OnAffinitySceneUnload();"),
        ("internal void CleanupAffinityRuntimeFromHost()", "affinityRuntime.Cleanup();"),
    ):
        body = method_body(bridge, signature)
        if not body or target not in body:
            return fail("host bridge must forward " + signature + " to the module")

    construction = "affinityRuntime = new AffinityRuntimeModule();"
    registration_call = "runtimeModuleHost.Register(affinityRuntime);"
    if registration.count(construction) != 1 or registration.count(registration_call) != 1:
        return fail("runtime registration must construct and register exactly one AffinityRuntimeModule")
    death_wraith_index = registration.find("runtimeModuleHost.Register(deathWraithRuntimeModule);")
    affinity_index = registration.find(construction)
    wedding_index = registration.find("weddingRuntime = new WeddingRuntimeModule();")
    if not (0 <= death_wraith_index < affinity_index < wedding_index):
        return fail("AffinityRuntimeModule must register after DeathWraith and before Wedding")
    for path in (
        "Integration/Affinity/AffinityRuntimeHooks.cs",
        "Integration/Affinity/AffinityRuntimeModuleHostBridge.cs",
    ):
        if path not in compile_text:
            return fail("compile_official.bat must include " + path)

    wrappers = {
        "internal void TickAlwaysOnRuntime()": "TickAffinityRuntimeFromHost();",
        "internal void OnSceneUnloadAlwaysOnRuntime()": "OnSceneUnloadAffinityRuntimeFromHost();",
        "internal void CleanupAlwaysOnRuntimeOnDestroy()": "CleanupAffinityRuntimeFromHost();",
    }
    for signature, forwarding_call in wrappers.items():
        body = method_body(always_on, signature)
        if not body or forwarding_call not in body:
            return fail("AlwaysOn host lifecycle must forward through " + forwarding_call)
        if "AffinityManager.UpdateDeferredSave();" in body or "AffinityManager.OnSceneUnload();" in body:
            return fail("AlwaysOn host must not own affinity tick or scene cleanup")

    print("AffinityRuntimeModuleGuard: PASS（单实例注册、事件 owner、生命周期顺序与兼容桥）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
