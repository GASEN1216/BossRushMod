"""CR-2026-09-06-012/013/014/016: ownership and actual damage observation boundaries.

Behavioral regressions live in fixtures/IntegrationThirdReviewFixes/run.py.
This guard protects the production wiring the executable fixture depends on.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def require(text, fragments, path):
    for fragment in fragments:
        if fragment not in text:
            raise AssertionError(path + " missing " + fragment)


def main():
    movement = read("Integration/NPCs/DuckNpc/DuckNpcMovement.cs")
    marker = read("Integration/NPCs/DuckNpc/DuckNpcRuntimeMarker.cs")
    observation = read("Integration/Bonus/SetBonusDamageObservation.cs")
    visuals = read("Integration/Bonus/SetBonusVisuals.cs")
    require(movement, ("revision != _pathRevision", "CancelCurrentPathRequest(true)",
        "IsHeld || Time.time < _pauseUntil", "_held || _dialogueHeld", "private void OnDisable()",
        "internal void HoldForDialogue()", "internal void ReleaseFromDialogue(float idleSeconds)"), "DuckNpcMovement")
    if ".MoveToPos(" in movement:
        raise AssertionError("DuckNpcMovement must not bypass its owned Seeker callback through official MoveToPos")
    wander = movement[movement.index("private void UpdateWander()"):
                      movement.index("private void MoveTowardFollowTarget()")]
    if wander.index("_followTarget != null") > wander.index("_pathControl.path != null"):
        raise AssertionError("follow replan must precede existing-path wander early return")
    require(marker, ("move.HoldForDialogue();", "move.ReleaseFromDialogue(stayDuration);"), "DuckNpcRuntimeMarker")
    if re.search(r"move\.(Hold|Release)\(", marker):
        raise AssertionError("dialogue must not acquire/release the residency owner")
    require(observation, ("[HarmonyPatch(typeof(Health), nameof(Health.Hurt))]", "[HarmonyPrefix]",
        "[HarmonyTranspiler]", "[HarmonyFinalizer]", "[ThreadStatic]", "Parent = current",
        "current = __state.Parent;", "HasSetBonusElementHealing", "TryFindObservationPoint",
        "ReferenceEquals(observation.Factors, info.elementFactors)", "ReportMissingObservation();",
        "throw new InvalidOperationException(supportDetail);"), "SetBonusDamageObservation")
    require(visuals, ("SetBonusDamageObservation.GetElementDamagePortion(health, damageInfo, element)",), "SetBonusVisuals")
    if "elementFactor / allFactor" in visuals:
        raise AssertionError("set healing must not apportion final damage by pre-resistance factors")
    for path, element in (("FrostSetBonus.cs", "ice"), ("ThunderSetBonus.cs", "electricity")):
        require(read("Integration/Bonus/" + path),
                ("GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes." + element + ")",), path)
    require(read("compile_official.bat"), ("Integration\\Bonus\\SetBonusDamageObservation.cs",), "compile_official.bat")
    for file in ("run.py", "Program.cs", "Stubs.cs", "OfficialIlCheck.cs", "README.md"):
        if not (ROOT / "tests/fixtures/IntegrationThirdReviewFixes" / file).is_file():
            raise AssertionError("missing executable fixture: " + file)
    print("IntegrationThirdReviewFixesGuard: PASS")


if __name__ == "__main__":
    main()
