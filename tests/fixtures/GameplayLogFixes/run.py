"""Compile exact production methods with explicit Unity/save boundary substitutes."""
from pathlib import Path
import hashlib
import os
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/gameplay-log-fixes"


def extract(path, signature):
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    start = source.index(signature)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    groups = {
        "internal partial class ModeHRuntimeModule": [
            ("ModeH/ModeHRuntimeModule.cs", "public override void OnAwake(ModBehaviour owner)"),
        ],
        "internal static partial class ModeHWarehouseStakeJournal": [
            ("ModeH/ModeHWarehouseStakeJournal.cs", "public static void RecomputeSlotConsistency("),
            ("ModeH/ModeHWarehouseStakeJournal.cs", "public static bool TryRecomputeDeferredSlotConsistency()"),
            ("ModeH/ModeHWarehouseStakeJournal.cs", "public static bool IsTerminalPhase("),
            ("ModeH/ModeHWarehouseStakeJournal.cs", "private static ModeHStakePhase ToPhase("),
            ("ModeH/ModeHWarehouseStakeJournal.cs", "public static void LoadPersisted("),
        ],
        "internal partial class DragonKingAbilityController": [
            ("Integration/DragonKing/DragonKingAbilityController_AttackFlow.cs", "private void OnBossHurt("),
            ("Integration/DragonKing/DragonKingAbilityController_AttackFlow.cs", "private void CheckChildProtection()"),
        ],
        "internal partial class ModBehaviour": [
            ("Integration/Wedding/WeddingBuildingInjector.cs", "public void InitWeddingBuilding()"),
            ("Integration/Wedding/WeddingBuildingInjector.cs", "private void TryInitializeWeddingBuildingEarly()"),
        ],
    }
    parts = ["using System; using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEngine; namespace BossRush {"]
    hashes = []
    for declaration, entries in groups.items():
        parts.append(declaration + " {")
        for path, signature in entries:
            method = extract(path, signature)
            parts.append(method)
            hashes.append(path + " | " + signature + " | " + hashlib.sha256(method.encode()).hexdigest())
        parts.append("}")
    parts.append("}")
    host_methods = [
        ("public partial class InteractableBase", "鸭科夫源码/TeamSoda.Duckov.Core/InteractableBase.cs", "protected virtual void Awake()"),
        ("namespace NodeCanvas.Tasks.Actions { public partial class SearchEnemyAround", "鸭科夫源码/TeamSoda.Duckov.Core/NodeCanvas/Tasks/Actions/SearchEnemyAround.cs", "private void OnSearchFinished("),
        ("namespace NodeCanvas.Tasks.Actions { public partial class CheckObsticle", "鸭科夫源码/TeamSoda.Duckov.Core/NodeCanvas/Tasks/Actions/CheckObsticle.cs", "private void OnCheckFinished("),
    ]
    for declaration, path, signature in host_methods:
        method = extract(path, signature)
        parts.append(declaration + " {\n" + method + "\n}" + ("}" if declaration.startswith("namespace") else ""))
        hashes.append(path + " | " + signature + " | " + hashlib.sha256(method.encode()).hexdigest())
    (OUT / "Production.cs").write_text("\n".join(parts), encoding="utf-8")
    (OUT / "source-hashes.txt").write_text("\n".join(hashes), encoding="utf-8")
    raise SystemExit(subprocess.call(
        ["dotnet", "run", "--project", str(HERE / "GameplayLogFixes.csproj"),
         "--configuration", "Release", "--verbosity", "quiet"], cwd=ROOT,
        env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en-US")))
