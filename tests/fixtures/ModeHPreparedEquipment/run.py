"""Execute current prepared-equipment stat and ammunition policy with isolated host doubles."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ModeHPreparedEquipment"
sys.path.insert(0, str(ROOT / "tests"))
from ModeHOneClickFlowGuard import method_body


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    paths = (ROOT / "ModeH/ModeHRuntimeModule_PreparedLoadouts.cs", ROOT / "ModeH/ModeHLoadoutKitRegistry_Prepared.cs",
             ROOT / "ModeH/ModeHProfilePersistence.cs", ROOT / "ModeH/ModeHRuntimeModule_CombatProfiles.cs",
             ROOT / "ModeH/ModeHLoadoutKitApplicator.cs", ROOT / "ModeH/ModeHRuntimeModule_LoadoutEditing.cs")
    stats = paths[0].read_text(encoding="utf-8-sig")
    registry = paths[1].read_text(encoding="utf-8-sig")
    members = "\n".join(method_body(stats, signature) for signature in (
        "private static ModeHPreparedFighterStats CalculatePreparedStats(",
        "private static void PrepareVisibleBaseStats(", "private static void SetPreviewBase("))
    # Tiny expression bodies remain verbatim as well; the helper extracts only brace-bodied members.
    start = stats.index("        private static float AmmoConstant(")
    end = stats.index("        /// <summary>", start)
    members += stats[start:end]
    start = stats.index("        private static float ReadStat(")
    end = stats.index("        private static void SetPreviewBase(", start)
    members += stats[start:end]
    code = "using System; using System.Collections; using System.Collections.Generic; using System.Reflection; namespace BossRush { partial class ModeHRuntimeModule {\n"
    code += members + "\ninternal static ModeHPreparedFighterStats Read(Item b, Item w, Item a) { return CalculatePreparedStats(b,w,a); }\n"
    code += "internal static void Prepare(Item b, CharacterRandomPreset p) { PrepareVisibleBaseStats(b,p); }\n"
    code += method_body(paths[3].read_text(encoding="utf-8-sig"), "private List<string> BuildDefaultKitSelection(") + "\n"
    editing = paths[5].read_text(encoding="utf-8-sig")
    code += "\n".join(method_body(editing, signature) for signature in (
        "private bool CanEditLoadout(", "private ModeHActionData MakePreparationOption(",
        "private void RecordKitPreferences(", "private List<string> LoadKitPreference(",
        "private void AddKitOptions(", "private static bool KitReplacesSlot(")) + "\n}\n"
    code += "static partial class ModeHLoadoutKitRegistry {\n" + method_body(registry, "internal static int ResolvePreparedAmmoTypeId(") + "\n}\n"
    persistence = paths[2].read_text(encoding="utf-8-sig")
    code += "static partial class ModeHProfilePersistence {\n"
    code += "\n".join(method_body(persistence, signature) for signature in (
        "public static bool StageWrite(", "public static bool FlushPending()",
        "private static bool VerifyDigest(", "private static object CloneSaveSnapshot(")) + "\n}\n"
    code += "static partial class ModeHLoadoutKitApplicator {\n" + method_body(
        paths[4].read_text(encoding="utf-8-sig"), "private static bool TryStoreAmmo(") + "\n}}"
    generated = OUT / "Production.cs"
    generated.write_text(code, encoding="utf-8")
    files = [generated, HERE / "Program.cs", HERE / "PersistenceAndLoadout.cs", HERE / "LoadoutOptions.cs",
             ROOT / "ModeH/ModeHStateDtos.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in files)
    (OUT / "Regression.csproj").write_text(project + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "sources.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in paths}, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Regression.csproj"), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
