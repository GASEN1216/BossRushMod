"""潮闸镇 Dev 演练的纯判据执行回归（2026-11-06）。

把 `DebugAndTools/F3GameplayValidationTidegateDrill.cs` 的 `#region 纯判据` 逐字抽出来，与生产
`Tidegate/TidegateTideRules.cs`（纯函数）一起编译执行：不启动 Unity、不碰存档。纯判据区一旦引用 Unity 或运行时对象，
这里直接失败——那一区的全部意义就是能在这里跑。

入口：`python tools/run_runtime_regressions.py --filter TidegateDrillJudges`。
"""
from pathlib import Path
import json
import os
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from cs_source_util import strip_comments
from crane_site import verify_site

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/TidegateDrillJudges"


def pure_region(source):
    start = source.index("#region 纯判据")
    body_start = source.index("\n", start) + 1
    end = source.index("#endregion", body_start)
    return source[body_start:end]


def declaration(source, signature):
    source = strip_comments(source)
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for at in range(brace, len(source)):
        if source[at] == "{":
            depth += 1
        elif source[at] == "}":
            depth -= 1
            if depth == 0:
                return source[start:at + 1]
    raise ValueError("unterminated production declaration: " + signature)


if __name__ == "__main__":
    if os.name != "nt":
        raise SystemExit("This fixture requires Windows .NET Framework.")
    drill = (ROOT / "DebugAndTools/F3GameplayValidationTidegateDrill.cs").read_text(encoding="utf-8-sig")
    judges = pure_region(drill)
    for token in ("UnityEngine", "Mathf.", "GameObject", "Transform", "Vector3", "TidegateSession", "Time.", "CharacterMainControl"):
        if token in judges:
            raise SystemExit("纯判据区引用了 Unity 或运行时对象（" + token + "）：它必须能脱离游戏执行")
    OUT.mkdir(parents=True, exist_ok=True)
    generated = OUT / "Judges.cs"
    generated.write_text("using System;\nusing System.Collections.Generic;\n\nnamespace BossRush\n{\n"
                         "    internal sealed partial class F3GameplayValidationRunner\n    {\n" + judges + "    }\n}\n",
                         encoding="utf-8")
    enemies = (ROOT / "Tidegate/TidegateEnemies.cs").read_text(encoding="utf-8-sig")
    loot = (ROOT / "Tidegate/TidegateLoot.cs").read_text(encoding="utf-8-sig")
    hooks = (ROOT / "Tidegate/TidegateDrillHooks.cs").read_text(encoding="utf-8-sig")
    crane = (ROOT / "Tidegate/TidegateCraneDanger.cs").read_text(encoding="utf-8-sig")
    # 与作者工程相同地把 Three.js 坐标转成本图 Unity 坐标；将真实点位传给生产伙伴配对，而不是伪造理想阵型。
    layout = json.loads((HERE / "PatrolLayout.json").read_text(encoding="utf-8"))
    authored = ROOT / "ArtSource/Tidegate/export/tidegate_layout.json"
    if authored.is_file():
        full = json.loads(authored.read_text(encoding="utf-8-sig"))
        actual = {"spawn": full["spawn"], "enemies": full["enemies"], "waypoints": {g: full["waypoints"][g] for g in ("town", "docks")}}
        if actual != layout:
            raise SystemExit("点位样本已与作者布局不同，请重新核对两区阵型并更新 PatrolLayout.json。")
        verify_site(authored.parent, full)
    def vector(p):
        return "new Vector3(%sf, %sf, %sf)" % (p[0], p[1], -p[2])
    layout_cs = "private static void AuthoredLayout(List<Slot> slots, TestWorld world) {\n"
    for point in layout["enemies"]:
        layout_cs += 'AddPost(slots, "%s", "%s", "%s", %s);\n' % (point["id"], point["group"], point["type"], vector(point["position"]))
    for group in ("town", "docks"):
        layout_cs += 'world.Waypoints["%s"] = new List<Vector3> { %s };\n' % (group, ", ".join(vector(p) for p in layout["waypoints"][group]))
    layout_cs += "world.PlayerSpawn.position = %s;\n}" % vector((layout["spawn"]["x"], 0.8, layout["spawn"]["z"]))
    lifecycle = OUT / "SpawnLifecycle.cs"
    lifecycle.write_text("using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing Tier = BossRush.SkyIslandLootTier;\n"
                         "namespace BossRush { " + declaration(crane, "internal struct TidegateCraneCycle") +
                         "\ninternal sealed partial class TidegateEnemies {\n" +
                         layout_cs + "\n" + declaration(enemies, "private sealed class Slot") + "\n" +
                         declaration(enemies, "private async void Spawn(Slot slot)") + "\n" +
                         "\n".join(declaration(enemies, signature) for signature in ("private void SelectRoster(",
                                  "private void AddPartners(", "private void AssignRoles()", "private static bool ScavDistrict(",
                                  "private static bool CanJoin(", "private bool InSpawnRing(", "internal void SpawnExtra(",
                                  "private bool TryNearbyFooting(", "private bool CanStand(")) + "\n" +
                         declaration(hooks, "internal int DevRemoveExtras(out int pending)") +
                         "\n} internal sealed class TidegateLoot {\n" +
                         declaration(loot, "internal static int RollCount(") + "\n}}\n", encoding="utf-8")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    exe = OUT / "Regression.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/define:BOSSRUSH_DEV", "/noconfig", "/nostdlib+", '/out:"' + str(exe) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(p) + '"' for p in (ROOT / "Tidegate/TidegateTideRules.cs", ROOT / "Tidegate/TidegateEnemyActors.cs",
                                       ROOT / "Tidegate/TidegateSquadPatrols.cs", ROOT / "SkyIsland/SkyIslandLootTables.cs", generated, lifecycle,
                                       HERE / "Program.cs", HERE / "SpawnLifecycleRegression.cs")]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call(["dotnet", str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        raise SystemExit(code)
    raise SystemExit(subprocess.call([str(exe)], cwd=OUT))
