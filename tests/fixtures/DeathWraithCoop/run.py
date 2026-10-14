"""链接亡魂网络 owner 的完整生产文件与原个人列表读写函数，隔离 Unity/传输/ES3。"""
from pathlib import Path
import os
import sys
import hashlib
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", ROOT / "Build/runtime-regressions/DeathWraithCoop"))
sys.path.insert(0, str(ROOT / "tests/fixtures/ModeDEntryOwnership"))
from run import member


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    system = ROOT / "Integration/DeathWraith/DeathWraithSystem.cs"
    lifecycle = ROOT / "Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs"
    coop = ROOT / "Integration/DeathWraith/DeathWraithCoop.cs"
    spawn = ROOT / "Integration/DeathWraith/DeathWraithSpawnFlow.cs"
    generated = "using System; using System.Collections.Generic; using UnityEngine; using Saves; using Duckov; using Duckov.Utilities; using ItemStatsSystem; using ItemStatsSystem.Data; namespace BossRush {\n"
    generated += "[Serializable] " + member(system.read_text(encoding="utf-8-sig"), "public class WraithInfo") + "\n"
    generated += member(system.read_text(encoding="utf-8-sig"), "internal sealed class DeadBodySpawnContext_DeathWraith") + "\ninternal sealed partial class DeathWraithRuntimeModule {\n"
    source = lifecycle.read_text(encoding="utf-8-sig")
    for signature in ("private List<WraithInfo> LoadStoredDeathWraithInfos_DeathWraith()",
                      "private void SaveStoredDeathWraithInfos_DeathWraith(", "private void MarkDeathWraithListDirty_DeathWraith()",
                      "internal void FlushDeathWraithListIfDirty_DeathWraith()", "private WraithInfo FindStoredDeathWraithInfoByRaidId_DeathWraith(",
                      "private bool RemoveStoredDeathWraithInfoByRaidId_DeathWraith(", "private void MergeStoredDeathWraithInfo_DeathWraith("):
        generated += member(source, signature) + "\n"
    generated += member(source, "internal void ClearDeathWraithState_DeathWraith(") + "\n"
    generated += member(system.read_text(encoding="utf-8-sig"), "internal void OnSetFile_DeathWraith(") + "\n"
    spawn_source = spawn.read_text(encoding="utf-8-sig")
    for signature in ("internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(", "internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(", "internal void NotifyOriginalDeadBodyTouched_DeathWraith(", "private DeadBodySpawnContext_DeathWraith TryConsumePendingDeadBodySpawnContext_DeathWraith(", "private void RemovePendingDeadBodySpawnContextByRaidId_DeathWraith("):
        generated += member(spawn_source, signature) + "\n"
    routing = member(spawn_source, "private async void TrySpawnStoredDeathWraithForRaid_DeathWraith(")
    routing = routing[routing.index("{") + 1:routing.index("CharacterMainControl spawnedWraith")]
    generated += "private void RouteOriginalTomb(uint raidID, Vector3 spawnPos, CoopWraithSpawn coopSpawn = null) {" + routing + 'throw new Exception("original tomb must use cooperative request path");}\n'
    generated += "} }\n"
    extracted = OUT / "PersonalPersistence.cs"
    extracted.write_text(generated, encoding="utf-8")
    files = [coop, extracted, HERE / "Stubs.cs", HERE / "Program.cs"]
    files += [ROOT / p for p in ("Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs")]
    project = OUT / "DeathWraithCoop.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                      '<LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0169;0414</NoWarn></PropertyGroup><ItemGroup>'
                      + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files) + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "source-sha256.txt").write_text('\n'.join(str(p.relative_to(ROOT)) + ' ' + hashlib.sha256(p.read_bytes()).hexdigest()
                                                 for p in (system, lifecycle, coop, spawn)), encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    result, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return result


if __name__ == "__main__":
    raise SystemExit(main())
