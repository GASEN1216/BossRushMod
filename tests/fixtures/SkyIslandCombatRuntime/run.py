"""真实 Harmony + 原样生产/官方方法；Unity 场景与物理查询是受控边界。"""
from pathlib import Path
import hashlib
import os
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def method(path, signature):
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    if source.count(signature) != 1:
        raise ValueError("Method anchor must be unique: " + signature)
    start = source.index(signature)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    if os.name != "nt":
        raise SystemExit("SkyIslandCombatRuntime requires Windows .NET Framework and installed Harmony")
    harmony = Path(os.environ.get("BOSSRUSH_HARMONY_DLL", ""))
    if not harmony.is_file():
        raise SystemExit("Set BOSSRUSH_HARMONY_DLL to installed 0Harmony.dll")
    managed = Path(os.environ.get("BOSSRUSH_GAME_MANAGED", ""))
    if not (managed / "TeamSoda.Duckov.Core.dll").is_file():
        raise SystemExit("Set BOSSRUSH_GAME_MANAGED to a game Managed copy for the read-only DLL contract")
    output = ROOT / "Build/runtime-regressions/SkyIslandCombatRuntime"
    output.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    groups = [
        ("public partial class ExplosionManager", [
            ("鸭科夫源码/TeamSoda.Duckov.Core/ExplosionManager.cs", "public void CreateExplosion("),
            ("鸭科夫源码/TeamSoda.Duckov.Core/ExplosionManager.cs", "private bool CheckObsticle("),
        ]),
        ("public partial class AICharacterController", [
            ("鸭科夫源码/TeamSoda.Duckov.Core/AICharacterController.cs", "private void Update()"),
        ]),
        ("public partial class AIMainBrain", [
            ("鸭科夫源码/TeamSoda.Duckov.Core/AIMainBrain.cs", "private void DoSearch("),
        ]),
        ("namespace NodeCanvas.Tasks.Actions { public partial class SearchEnemyAround", [
            ("鸭科夫源码/TeamSoda.Duckov.Core/NodeCanvas/Tasks/Actions/SearchEnemyAround.cs", "private void OnSearchFinished("),
        ]),
        ("namespace NodeCanvas.Tasks.Actions { public partial class SetAim", [
            ("鸭科夫源码/TeamSoda.Duckov.Core/NodeCanvas/Tasks/Actions/SetAim.cs", "protected override void OnExecute()"),
        ]),
        ("namespace BossRush { internal static partial class SkyIslandEnemyTiers", [
            ("SkyIsland/SkyIslandEnemyTiers.cs", "internal static float TraceDistance("),
            ("SkyIsland/SkyIslandEnemyTiers.cs", "internal static void ApplyAi("),
        ]),
    ]
    parts = ["using System; using System.Collections.Generic; using UnityEngine; using Duckov; using Duckov.Utilities; using Object = UnityEngine.Object;"]
    hashes = []
    for declaration, entries in groups:
        parts.append(declaration + " {")
        for path, signature in entries:
            body = method(path, signature)
            parts.append(body)
            hashes.append(path + " | " + signature + " | " + hashlib.sha256(body.encode()).hexdigest())
        parts.append("}" + ("}" if declaration.startswith("namespace") else ""))
    extracted = invocation / "Production.Extracted.cs"
    extracted.write_text("\n".join(parts), encoding="utf-8")
    (invocation / "source-hashes.txt").write_text("\n".join(hashes) + "\n", encoding="utf-8")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    executable = invocation / "Regression.exe"
    shutil.copy2(harmony, invocation / "0Harmony.dll")
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/nostdlib+",
            '/out:"' + str(executable) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    sources = [extracted, HERE / "Host.cs", HERE / "Program.cs", HERE / "OfficialContract.cs",
               ROOT / "SkyIsland/SkyIslandEnemyTier.cs", ROOT / "SkyIsland/SkyIslandExplosionObstaclePatch.cs",
               ROOT / "SkyIsland/SkyIslandExplosionBufferPatch.cs"]
    args += ['"' + str(path) + '"' for path in sources]
    response = invocation / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "/noconfig", "@" + str(response)], cwd=ROOT)
    if code:
        return code
    print("Execute fresh binary: " + str(executable), flush=True)
    print("SHA-256: " + hashlib.sha256(executable.read_bytes()).hexdigest(), flush=True)
    return subprocess.call([str(executable)], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
