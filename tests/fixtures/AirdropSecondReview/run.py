"""CR-2026-09-05-017: execute current pool/configuration methods, with host metadata stubs."""
from pathlib import Path
import hashlib
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/airdrop-second-review-fixture"


def member(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                    lambda m: " " * len(m.group()), source, flags=re.S)
    depth = 0
    for i in range(opening, len(source)):
        depth += (masked[i] == "{") - (masked[i] == "}")
        if depth == 0:
            return source[start:i + 1]
    raise ValueError("Unclosed production method: " + marker)


if __name__ == "__main__":
    path = ROOT / "RandomEvents/RandomEventEffectsBridge_Loot.cs"
    source = path.read_text(encoding="utf-8-sig")
    methods = [member(source, "private void ConfigureRandomEventAirdropLoader("),
               member(source, "private bool FillRandomEventAirdropPool(")]
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "Production.cs").write_text(
        "using System; using System.Collections; using System.Collections.Generic; "
        "using System.Reflection; using UnityEngine; using ItemStatsSystem;\n"
        "public partial class ModBehaviour {\n" + "\n".join(methods) + "\n}\n", encoding="utf-8")
    (OUT / "source.sha256.txt").write_text(hashlib.sha256(path.read_bytes()).hexdigest(), encoding="utf-8")
    result = subprocess.call(["dotnet", "build", str(HERE / "Airdrop.csproj"), "--configuration", "Release", "--nologo",
                              "-p:BaseIntermediateOutputPath=" + str(OUT / "obj") + "/",
                              "-p:BaseOutputPath=" + str(OUT / "bin") + "/"], cwd=ROOT)
    if result:
        raise SystemExit(result)
    raise SystemExit(subprocess.call(["dotnet", str(OUT / "bin/Release/net8.0/Airdrop.dll")], cwd=ROOT))
