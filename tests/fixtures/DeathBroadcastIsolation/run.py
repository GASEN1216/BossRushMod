"""Death/hurt broadcast isolation: official method bodies under real Harmony + installed game IL.

Windows/.NET Framework + installed game Harmony and Managed assemblies; no game process,
player data or deployment. Outputs stay under Build/death-broadcast-isolation-fixture.
"""
from pathlib import Path
import hashlib
import os
import re
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/death-broadcast-isolation-fixture"
PRODUCTION = ROOT / "Patches/Combat/DeathBroadcastIsolationPatch.cs"


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


def compile_and_run(compiler, args, response, executable, run_args=()):
    response.write_text("\n".join(args), encoding="utf-8-sig")
    result = subprocess.call(["dotnet", str(compiler), "/noconfig", "@" + str(response)], cwd=ROOT)
    if result:
        return result
    return subprocess.call([str(executable), *run_args], cwd=OUT)


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

    # Part A：官方 Health.Hurt 与 CharacterMainControl.OnDead 方法体逐字提取，算式与事件顺序不变。
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core"
    health = (official / "Health.cs").read_text(encoding="utf-8-sig")
    character = (official / "CharacterMainControl.cs").read_text(encoding="utf-8-sig")
    damage = (official / "DamageInfo.cs").read_text(encoding="utf-8-sig")
    fields = damage[damage.index("public DamageTypes damageType;"):damage.rfind("}")]
    fields = re.sub(r"\[(SerializeField|HideInInspector|ItemTypeID)\]\s*", "", fields)
    generated = ("using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Events;"
                 " using Random = UnityEngine.Random;\n")
    generated += "public partial class Health {\n"
    generated += "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
    generated += member(health, "public bool Hurt(DamageInfo damageInfo)") + "}\n"
    generated += "public partial class CharacterMainControl {\n"
    generated += "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
    generated += member(character, "private void OnDead(DamageInfo dmgInfo)") + "}\n"
    generated += "public struct DamageInfo {\n" + fields
    generated += member(damage, "public DamageInfo(CharacterMainControl fromCharacter = null)")
    generated += member(damage, "public void AddElementFactor(ElementTypes _type, float _factor)") + "}\n"
    generated_path = OUT / "OfficialBodies.cs"
    generated_path.write_text(generated, encoding="utf-8")
    sources = [PRODUCTION, official / "ElementFactor.cs", official / "ElementTypes.cs", official / "DamageTypes.cs",
               HERE / "Stubs.cs", HERE / "Program.cs", generated_path]
    (OUT / "source-hashes.txt").write_text("\n".join(
        hashlib.sha256(path.read_bytes()).hexdigest() + "  " + str(path)
        for path in sources + [official / "Health.cs", official / "CharacterMainControl.cs",
                               official / "DamageInfo.cs", harmony, managed / "TeamSoda.Duckov.Core.dll"]),
        encoding="utf-8")

    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    executable = OUT / "DeathBroadcastIsolation.exe"
    args = ["/nologo", "/target:exe", "/optimize+", "/langversion:7.3", "/nostdlib+",
            '/out:"' + str(executable) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(path) + '"' for path in sources]
    result = compile_and_run(compiler, args, OUT / "compile.rsp", executable)
    if result:
        return result

    # Part B：同一份生产补丁对现装游戏程序集编译，核对真实 IL 与真实 UnityEvent 实现。
    official_exe = OUT / "OfficialCheck.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/nostdlib+",
            '/out:"' + str(official_exe) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(path) + '"' for path in sorted(managed.glob("*.dll"))]
    args += ['"' + str(PRODUCTION) + '"', '"' + str(HERE / "OfficialCheck.cs") + '"']
    return compile_and_run(compiler, args, OUT / "official-compile.rsp", official_exe, (str(managed),))


if __name__ == "__main__":
    raise SystemExit(main())
