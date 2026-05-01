"""Zombie Mode review optimization guard.

Locks down the review fixes that keep Zombie Mode aligned with existing project
patterns: own the entry transaction, reuse original map spawner Points, register
enemy recovery anchors, and avoid raw damageValue heal-back compensation.
"""

from pathlib import Path
import re
import sys


MAP_SELECTION = Path("ZombieMode/ZombieModeMapSelectionHelper.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
RECOVERY = Path("Utilities/EnemyRecoveryMonitor.cs")


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


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeReviewOptimizationGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    map_selection = MAP_SELECTION.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    boss = BOSS.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    recovery = RECOVERY.read_text(encoding="utf-8")

    if 'ReadMapSelectionViewBool(mapView, "confirmButtonClicked")' in map_selection:
        return fail("ZombieModeReviewOptimizationGuard: Zombie entry must not poll original MapSelectionView confirmation")

    for snippet in [
        "CreateZombieModeFreeMapEntryCost",
        "entry.enabled = false;",
        "ShowZombieModeCashInvestmentPrompt(delegate",
        "StartZombieModeConfirmedMapLoad",
        "SceneLoader.Instance.LoadScene",
        "ZombieModeMapSelectionHelper.ConfirmZombieModeMapEntry(entryIndex)",
    ]:
        result = require(map_selection, snippet, "manual map entry confirmation")
        if result:
            return result

    if "ZombieModeMapSelectionHelper.SetPendingZombieMapEntryIndex(entryIndex)" in map_selection:
        return fail("ZombieModeReviewOptimizationGuard: click handler must start the Zombie confirmation flow")

    commit = extract_method(entry, "private bool CommitZombieModeEntryResourcesShell")
    if not commit:
        return fail("ZombieModeReviewOptimizationGuard: cannot extract CommitZombieModeEntryResourcesShell")

    for snippet in [
        "ZombieModeMapSelectionHelper.CreateZombieModeCost()",
        "EconomyManager.Pay(invitationCost, true, true)",
        "zombieModeEntryTransaction.InvitationTemporarilyHeld = true;",
    ]:
        result = require(commit, snippet, "manual invitation commit")
        if result:
            return result

    if "damageInfo.damageValue" in waves:
        return fail("ZombieModeReviewOptimizationGuard: wave damage compensation must not use raw damageValue")

    for snippet in [
        "damageInfo.finalDamage",
        "RestoreZombieModeFinalDamageReduction",
        "TryRestoreZombieModeFinalDamage",
    ]:
        result = require(waves, snippet, "final-damage based reduction")
        if result:
            return result

    if not re.search(r"public\s+float\s+AbsorbDamage\s*\(float\s+finalDamage", boss):
        return fail("ZombieModeReviewOptimizationGuard: boss shield should absorb finalDamage and return absorbed amount")

    for snippet in [
        "CollectZombieModeOriginalSpawnerPoints",
        "CharacterSpawnerRoot[] spawners",
        "GetComponentInChildren<Points>(true)",
        "pointsComponent.GetPoint(j)",
        "RegisterEnemyRecoveryAnchor(zombie, position)",
        "RegisterEnemyRecoveryAnchor(boss, position)",
    ]:
        result = require(spawner, snippet, "spawner reuse and recovery anchor")
        if result:
            return result

    for snippet in [
        "IsZombieModeActive",
        "MonitorZombieModeEnemyRecovery(player)",
        "AppendZombieModeRecoverySpawnCandidates()",
    ]:
        result = require(recovery, snippet, "zombie enemy recovery integration")
        if result:
            return result

    print("ZombieModeReviewOptimizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
