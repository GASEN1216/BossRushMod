"""Extract current production methods, then run the host-independent regressions."""
from pathlib import Path
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def member(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    # Braces in strings/comments are not C# block delimiters.
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                    lambda m: " " * len(m.group()), source, flags=re.S)
    depth = 0
    for i in range(opening, len(source)):
        if masked[i] == "{":
            depth += 1
        elif masked[i] == "}":
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
    raise ValueError("Unclosed production member: " + marker)


def generate():
    drops = (ROOT / "ZombieMode/ZombieModeDropsAndPerformance.cs").read_text(encoding="utf-8-sig")
    bridge = (ROOT / "Integration/Wedding/WeddingModBehaviourBridge.cs").read_text(encoding="utf-8-sig")
    module = (ROOT / "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs").read_text(encoding="utf-8-sig")
    methods = [member(drops, "private void CleanupZombieModeExpiredDropCandidates(bool forceWaveCleanup)")]
    for marker in ["private sealed class PermanentSpouseRestoreRequest", "private void InvalidatePermanentSpouseRestore()",
                   "private void RequestPermanentSpouseRestore(", "private bool IsPermanentSpouseRestoreCurrent(",
                   "private async UniTaskVoid RestorePermanentSpouseAsync("]:
        methods.append(member(bridge, marker))
    # Only async return type is adapted; bodies and call sites remain production code.
    body = "\n".join(methods).replace("async UniTaskVoid", "async Task")
    force = member(module, "internal static async UniTask<CharacterMainControl> ForceSpawnAtAsync(")
    force = force.replace("async UniTask<CharacterMainControl>", "async Task<CharacterMainControl>")
    generated = ("using System; using System.Threading.Tasks;\npublic partial class ModBehaviour {\n" + body
                 + "\n}\npublic partial class PermanentDuckNpcModule {\n" + force + "\n}\n")
    target = ROOT / "Build/runtime-ownership-fixture"
    target.mkdir(parents=True, exist_ok=True)
    (target / "Production.cs").write_text(generated, encoding="utf-8")
    return target


if __name__ == "__main__":
    target = generate()
    result = subprocess.call([
        "dotnet", "build", str(HERE / "RuntimeOwnership.csproj"), "--configuration", "Release", "--nologo",
        "-p:BaseIntermediateOutputPath=" + str(target / "obj") + "/",
        "-p:BaseOutputPath=" + str(target / "bin") + "/",
    ], cwd=ROOT)
    if result:
        raise SystemExit(result)
    raise SystemExit(subprocess.call(["dotnet", str(target / "bin/Release/net8.0/RuntimeOwnership.dll")], cwd=ROOT))
