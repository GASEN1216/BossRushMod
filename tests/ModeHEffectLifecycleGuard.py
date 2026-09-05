#!/usr/bin/env python3
"""Mode H effect ownership, expiry, command scope and initial context invariants.

Runtime readback regressions: python tests/fixtures/modeh_effects/run.py
This guard is source structure verification, not a Unity gameplay test.
"""
from pathlib import Path
import re
import sys

from modeh_guard_util import strip_cs_comments

ROOT = Path(__file__).resolve().parents[1]
FILES = ("ModeHInjuryAndScarSystem.cs", "ModeHCommandAdapters.cs", "ModeHCombatControl.cs")


def method(source, signature):
    start = source.find(signature)
    if start < 0:
        return ""
    brace = source.find("{", start)
    depth = 1
    end = brace + 1
    while end < len(source) and depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end] if not depth else ""


def ordered(source, *tokens):
    cursor = 0
    for token in tokens:
        cursor = source.find(token, cursor)
        if cursor < 0:
            return False
        cursor += len(token)
    return True


def check(sources):
    injury, adapter, combat = [strip_cs_comments(source) for source in sources]
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    standing = method(injury, "public bool ApplyStandingScars(")
    trigger = method(injury, "private bool TryGetScarTriggerSpec(")
    opening = method(injury, "public bool TryOpenScarWindow(")
    bind = method(injury, "public void BindFighter(")
    restore = method(injury, "public void RestoreAll(")
    scope = method(injury, "public float GetCommandScale(")
    self_settled = method(injury, "private void ApplySelfSettledComponents(")
    tick = method(injury, "public void Tick(")
    adapter_tick = method(adapter, "public void Tick(")
    enter = method(combat, "public bool OnFighterEntered(")
    bell = method(combat, "public bool TryRingBell(")
    preview = method(injury, "public float GetCommandScaleForBell(")
    require(ordered(standing, "_ownedScarIds.Add(scarIds[i])", "if (spec.WindowSeconds > 0) continue;"),
            "ModeHInjuryAndScarSystem: standing registration must include triggered scar ownership")
    require(ordered(trigger, "if (!_ownedScarIds.Contains(scarId))", "return false;", "GetScar(scarId)"),
            "ModeHInjuryAndScarSystem: trigger lookup must refuse unowned scars before catalog lookup")
    require(ordered(opening, "TryGetScarTriggerSpec(", "OpenWindow(", "_consumedTriggers.Add("),
            "ModeHInjuryAndScarSystem: scar consumption must follow ownership validation and successful Apply")
    require("_ownedScarIds.Contains(dto.scarId)" in method(injury, "public void RestoreScarWindows("),
            "ModeHInjuryAndScarSystem: snapshot restore must preserve scar ownership")
    require(all(token in restore for token in ("_ownedScarIds.Clear()", "_commandModulations.Clear()", "_sharedFireContext = null")),
            "ModeHInjuryAndScarSystem: cleanup must clear owned scars, command records and former context")
    require(ordered(bind, "RestoreAll();", "_sharedFireContext = fireContext;"),
            "ModeHInjuryAndScarSystem: BindFighter must restore prior owner before taking current context")
    require("string.Equals(modulation.TargetCommandId, commandId, StringComparison.Ordinal)" in scope
            and "modulation.RemainingSeconds <= 0f" in scope,
            "ModeHInjuryAndScarSystem: command scale must filter target ID and expired records")
    require("modulation.TargetCommandId = component.TargetCommandId" in self_settled
            and "component.WindowSeconds" in self_settled and "windowSeconds" in self_settled
            and "_selfSettledCommandScale *= multiplier" not in self_settled,
            "ModeHInjuryAndScarSystem: self-settled command components must retain scope and lifetime")
    require(ordered(tick, "RemainingSeconds -= deltaTime", "RemainingSeconds <= 0f", "_commandModulations.RemoveAt(i)"),
            "ModeHInjuryAndScarSystem: self-settled records must expire independently of engine adapters")
    require(re.search(r"if \(!window\.Adapter\.IsActive\)\s*\{\s*window\.Adapter\.Restore\(\);\s*_activeWindows\.RemoveAt\(i\);", tick) is not None,
            "ModeHInjuryAndScarSystem: owner must restore an expired adapter before releasing it")
    require(ordered(adapter_tick, "_windowRemaining -= deltaTime;", "if (_windowRemaining <= 0f)", "Restore();", "return;", "_reassertAccumulator += deltaTime;"),
            "ModeHCommandAdapters: expiration must restore before reassert throttling can return")
    require(ordered(enter, "RefreshFireContext(0f, true);", "_injuryAndScar.BindFighter(", "_matchIndex, _fireContext)", "ApplyStandingInjury(", "ApplyStandingScars("),
            "ModeHCombatControl: fighter entry must provide current condition context before standing effects")
    require("TryGetScarTriggerSpec(\"bell_dependence\", \"bell_rung\"" in preview
            and "TryOpenScarWindow(" not in preview and "_consumedTriggers.Add(" not in preview,
            "ModeHInjuryAndScarSystem: bell scale preview must validate ownership without consuming trigger")
    require(ordered(bell, "GetCommandScaleForBell(_commandController.LockedCommandId)", "if (!ok) return false;", "RefreshEffectConditionInputs();", 'TryOpenScarWindow("bell_dependence", "bell_rung"'),
            "ModeHCombatControl: this bell must receive the preview and commit its scar only after success with refreshed conditions")
    return errors


def reverse_checks(sources):
    # Mutations are in-memory only; no shared production file is touched.
    cases = (
        (0, "if (!_ownedScarIds.Contains(scarId))", "if (false)", "ownership"),
        (0, "modulation.TargetCommandId = component.TargetCommandId", "modulation.TargetCommandId = null", "target scope"),
        (0, "_commandModulations[i].RemainingSeconds -= deltaTime;", "", "expiry"),
        (0, "window.Adapter.Restore();\n                    _activeWindows.RemoveAt(i);", "_activeWindows.RemoveAt(i);", "owner finalization"),
        (1, "_windowRemaining -= deltaTime;", "_windowRemaining = 0f;", "adapter finalization order"),
        (2, "RefreshFireContext(0f, true);\n            _injuryAndScar.BindFighter", "_injuryAndScar.BindFighter", "initial context"),
        (2, "GetCommandScaleForBell(_commandController.LockedCommandId)", "GetCommandScale(_commandController.LockedCommandId)", "bell preview"),
        (2, "if (!ok) return false;", "", "failed bell consumption"),
        (2, "RefreshEffectConditionInputs();\n            string reason;", "string reason;", "post-bell context"),
    )
    for index, old, new, name in cases:
        if old not in sources[index]:
            raise AssertionError("reverse check missing mutation anchor: " + name)
        mutated = list(sources)
        mutated[index] = mutated[index].replace(old, new, 1)
        if not check(mutated):
            raise AssertionError("guard missed reverse mutation: " + name)
    return len(cases)


def main():
    sources = [(ROOT / "ModeH" / name).read_text(encoding="utf-8-sig") for name in FILES]
    errors = check(sources)
    if errors:
        print("ModeHEffectLifecycleGuard: FAIL\n" + "\n".join(errors))
        return 1
    count = reverse_checks(sources)
    print("ModeHEffectLifecycleGuard: PASS (%d in-memory reverse mutations rejected)" % count)
    return 0


if __name__ == "__main__":
    sys.exit(main())
