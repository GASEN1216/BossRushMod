"""Execute production NPC movement and real Harmony patches over the official Hurt body.

Windows/.NET Framework + installed game Harmony, no game process, player data or deployment.
Outputs and copied dependencies stay under Build/integration-third-review-fixture.
"""
from pathlib import Path
import hashlib
import os
import re
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/integration-third-review-fixture"


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
    raise ValueError(marker)


def main():
    if os.name != "nt":
        raise SystemExit("Requires Windows .NET Framework and the installed game Harmony.")
    harmony = Path(os.environ.get("BOSSRUSH_HARMONY_DLL",
        r"D:\software\steam\steamapps\workshop\content\3167020\3588386576\0Harmony.dll"))
    if not harmony.is_file():
        raise SystemExit("Set BOSSRUSH_HARMONY_DLL to the installed 0Harmony.dll")
    managed = Path(os.environ.get("BOSSRUSH_GAME_MANAGED",
        r"D:\software\steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed"))
    if not (managed / "TeamSoda.Duckov.Core.dll").is_file():
        raise SystemExit("Set BOSSRUSH_GAME_MANAGED to the installed game's Managed directory")
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copy2(harmony, OUT / "0Harmony.dll")
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core"
    health = (official / "Health.cs").read_text(encoding="utf-8-sig")
    damage = (official / "DamageInfo.cs").read_text(encoding="utf-8-sig")
    fields = damage[damage.index("public DamageTypes damageType;"):damage.rfind("}")]
    fields = re.sub(r"\[(SerializeField|HideInInspector|ItemTypeID)\]\s*", "", fields)
    generated = "using System; using System.Collections.Generic; using UnityEngine; using Random = UnityEngine.Random;\n"
    generated += "public partial class Health {\n"
    generated += "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
    generated += member(health, "public bool Hurt(DamageInfo damageInfo)") + "}\n"
    generated += "public struct DamageInfo {\n" + fields
    generated += member(damage, "public DamageInfo(CharacterMainControl fromCharacter = null)")
    generated += member(damage, "public void AddElementFactor(ElementTypes _type, float _factor)") + "}\n"
    # The actual production helper and ownership predicate are included, too.
    visuals = (ROOT / "Integration/Bonus/SetBonusVisuals.cs").read_text(encoding="utf-8-sig")
    generated += "namespace BossRush { public partial class ModBehaviour {\n"
    generated += member(visuals, "private static float GetSetBonusElementDamagePortion(")
    generated += member(visuals, "internal bool HasSetBonusElementHealing") + "}}\n"
    generated_path = OUT / "OfficialAndHelpers.cs"
    generated_path.write_text(generated, encoding="utf-8")
    sources = [ROOT / "Integration/NPCs/DuckNpc/DuckNpcMovement.cs",
               ROOT / "Integration/NPCs/DuckNpc/DuckNpcRuntimeMarker.cs",
               ROOT / "Integration/Bonus/SetBonusDamageObservation.cs",
               official / "AI_PathControl.cs", official / "ElementFactor.cs",
               official / "ElementTypes.cs", official / "DamageTypes.cs",
               HERE / "Stubs.cs", HERE / "Program.cs", generated_path]
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    executable = OUT / "IntegrationThirdReview.exe"
    args = ["/nologo", "/target:exe", "/optimize+", "/langversion:7.3", "/nostdlib+",
            '/out:"' + str(executable) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(path) + '"' for path in sources]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    (OUT / "source-hashes.txt").write_text("\n".join(
        hashlib.sha256(path.read_bytes()).hexdigest() + "  " + str(path)
        for path in sources + [official / "Health.cs", official / "DamageInfo.cs", harmony]), encoding="utf-8")
    result = subprocess.call(["dotnet", str(compiler), "/noconfig", "@" + str(response)], cwd=ROOT)
    if result:
        return result
    result = subprocess.call([str(executable)], cwd=OUT)
    if result:
        return result
    # Compile the exact same production patch against installed real game types and check actual IL.
    official_exe = OUT / "OfficialIlCheck.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/nostdlib+",
            '/out:"' + str(official_exe) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(path) + '"' for path in sorted(managed.glob("*.dll"))]
    args += ['"' + str(ROOT / "Integration/Bonus/SetBonusDamageObservation.cs") + '"',
             '"' + str(HERE / "OfficialIlCheck.cs") + '"']
    response = OUT / "official-il-compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    game_core = managed / "TeamSoda.Duckov.Core.dll"
    (OUT / "installed-game.sha256.txt").write_text(hashlib.sha256(game_core.read_bytes()).hexdigest()
        + "  " + str(game_core), encoding="utf-8")
    result = subprocess.call(["dotnet", str(compiler), "/noconfig", "@" + str(response)], cwd=ROOT)
    if result:
        return result
    return subprocess.call([str(official_exe), str(managed)], cwd=OUT)


if __name__ == "__main__":
    raise SystemExit(main())
