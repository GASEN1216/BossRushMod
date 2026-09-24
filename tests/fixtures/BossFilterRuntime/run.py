"""Execute the production Boss filter state and cache methods with small owner stubs."""
from pathlib import Path
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def member(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                    lambda match: " " * len(match.group()), source, flags=re.S)
    depth = 0
    for offset in range(opening, len(source)):
        if masked[offset] == "{":
            depth += 1
        elif masked[offset] == "}":
            depth -= 1
            if depth == 0:
                return source[start:offset + 1]
    raise ValueError("Unclosed production member: " + marker)


def generate():
    source = (ROOT / "BossFilter/BossFilter.cs").read_text(encoding="utf-8-sig")
    markers = (
        "internal void InitializeBossPoolFilter()",
        "public bool IsBossEnabled(string bossName)",
        "public void SetBossEnabled(string bossName, bool enabled)",
        "public List<EnemyPresetInfo> GetFilteredEnemyPresets()",
        "internal void InvalidateFilteredPresetsCache()",
        "public float GetBossInfiniteHellFactor(string bossName)",
    )
    members = "\n".join(member(source, marker) for marker in markers)
    generated = """using System; using System.Collections.Generic; using System.Linq;
internal sealed class BossFilterRuntimeModule
{
    private readonly ModBehaviour owner;
    internal BossFilterRuntimeModule(ModBehaviour owner) { this.owner = owner; }
    private Dictionary<string, bool> bossEnabledStates = new Dictionary<string, bool>();
    private Dictionary<string, float> bossInfiniteHellFactors = new Dictionary<string, float>();
    private bool bossPoolFilterInitialized;
    private List<EnemyPresetInfo> _filteredPresetsCache;
    private bool _filteredPresetsCacheDirty = true;
""" + members + "\n}\n"
    target = ROOT / "Build/boss-filter-runtime-fixture"
    target.mkdir(parents=True, exist_ok=True)
    (target / "Production.cs").write_text(generated, encoding="utf-8")
    return target


if __name__ == "__main__":
    target = generate()
    result = subprocess.call([
        "dotnet", "build", str(HERE / "BossFilterRuntime.csproj"), "--configuration", "Release", "--nologo",
        "-p:BaseIntermediateOutputPath=" + str(target / "obj") + "/",
        "-p:BaseOutputPath=" + str(target / "bin") + "/",
    ], cwd=ROOT)
    if result:
        raise SystemExit(result)
    raise SystemExit(subprocess.call(["dotnet", str(target / "bin/Release/net8.0/BossFilterRuntime.dll")], cwd=ROOT))
