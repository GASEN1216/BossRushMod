"""Execute fresh production members; only UniTask return types become Task.

Generated sources, binaries and fault-probe logs stay under Build/.
"""
from pathlib import Path
import argparse
import hashlib
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
NPC = "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs"
SHOWCASE = "Integration/BackMountain/ShowcaseService.cs"


def member(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                    lambda m: " " * len(m.group()), source, flags=re.S)
    depth = 0
    for end in range(opening, len(source)):
        depth += (masked[end] == "{") - (masked[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise ValueError(marker)


def generate(probe):
    npc = (ROOT / NPC).read_text(encoding="utf-8-sig")
    showcase = (ROOT / SHOWCASE).read_text(encoding="utf-8-sig")
    fields = npc[npc.index("private bool _spawnInFlight;"):npc.index("public void Spawn(ModBehaviour mod)")]
    methods = "\n".join(member(npc, marker) for marker in [
        "public bool ShouldSpawnInScene(", "private static bool IsArenaLikeScene(",
        "public void Spawn(ModBehaviour mod)", "private void TryStartPendingSpawn()",
        "private async UniTaskVoid SpawnForSceneAsync(", "private async UniTask SpawnOneAsync(",
        "private static bool IsSpawnRequestValid(", "public void Destroy(ModBehaviour mod)",
        "internal static void ResetStaticCaches()"])
    methods = methods.replace("async UniTaskVoid", "async Task").replace("async UniTask", "async Task")
    health = member(showcase, "internal static void ReapplyBonuses()")
    if probe == "drop-successor":
        marker = "public void Spawn(ModBehaviour mod)\n        {"
        assert marker in methods
        methods = methods.replace(marker, marker + "\n            if (_spawnInFlight) return;", 1)
    elif probe == "sample-after-remove":
        remove = 'RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");'
        assert health.count(remove) == 1
        health = health.replace(remove, "", 1).replace(
            "CharacterMainControl main = CharacterMainControl.Main;",
            remove + "\n                CharacterMainControl main = CharacterMainControl.Main;", 1)
    target = ROOT / "Build/content-second-review-fixture" / probe
    target.mkdir(parents=True, exist_ok=True)
    production = ("using System; using System.Collections.Generic; using System.Threading.Tasks; using UnityEngine;\n"
                  "public partial class PermanentDuckNpcModule {\n" + fields + methods + "\n}\n"
                  "public static partial class ShowcaseService {\n" + health + "\n}\n")
    (target / "Production.cs").write_text(production, encoding="utf-8")
    for name in ["Stubs.cs", "Program.cs", "ContentSecondReview.csproj"]:
        (target / name).write_bytes((HERE / name).read_bytes())
    (target / "source-sha256.txt").write_text("\n".join(
        path + " " + hashlib.sha256((ROOT / path).read_bytes()).hexdigest()
        for path in [NPC, SHOWCASE]), encoding="utf-8")
    return target


def execute(probe):
    target = generate(probe)
    result = subprocess.run(["dotnet", "run", "--project", str(target / "ContentSecondReview.csproj"),
                             "--configuration", "Release", "--nologo"], cwd=ROOT,
                            text=True, encoding="utf-8", stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (target / "run.log").write_text(result.stdout, encoding="utf-8")
    print(result.stdout)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--negative-probes", action="store_true")
    args = parser.parse_args()
    result = execute("current")
    if result.returncode:
        raise SystemExit(result.returncode)
    if args.negative_probes:
        expected = {"drop-successor": "ABC remains serial while A awaits", "sample-after-remove": "injured above base stays injured"}
        for probe, message in expected.items():
            result = execute(probe)
            if result.returncode == 0 or "FAIL: " + message not in result.stdout:
                raise SystemExit("negative probe failed to expose its regression: " + probe)
            print("REJECTED negative probe: " + probe)
