"""Link the complete EclipseBloom runtime owners against explicit host boundaries."""
from pathlib import Path
import hashlib
import json
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture

PRODUCTION = (
    "Integration/EclipseBloom/EclipseBloomRules.cs",
    "Integration/EclipseBloom/EclipseBloomVisuals_Geometry.cs",
    "Integration/EclipseBloom/EclipseBloomAbilityController.cs",
    "Integration/EclipseBloom/EclipseBloomPetal.cs",
    "Integration/EclipseBloom/EclipseBloomBoss.cs",
    "Integration/EclipseBloom/EclipseBloomRuntimeModule.cs",
    "Integration/EclipseBloom/EclipseBloomEncounterPlacement.cs",
    "Integration/EclipseBloom/EclipseBloomGroundProjection.cs",
    "Integration/EclipseBloom/EclipseBloomPoolAdapter.cs",
    "Integration/EclipseBloom/ResidualSunShieldModelGeometry.cs",
    "Patches/Combat/CharacterOnDeadPatch.cs",
)
INTRO_PRODUCTION = (
    "Integration/EclipseBloom/EclipseBloomIntroduction.cs",
    "Integration/EclipseBloom/EclipseBloomIntroductionPersistence.cs",
    "Common/Lifecycle/BossRushSlotJsonStore.cs",
    "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs",
    "Common/Data/BossRushJsonValue.cs",
    "Utilities/SimpleJsonHelper.cs",
    "Utilities/OfficialQuests/OfficialQuestBinding.cs",
)
ASSET_PRODUCTION = (
    "Integration/EclipseBloom/ResidualSunShieldIcon.cs",
    "Integration/EclipseBloom/ResidualSunShieldModel.cs",
    "Integration/EclipseBloom/ResidualSunShieldConfig.cs",
    "Integration/EquipmentFactory_RuntimeModels.cs",
    "Integration/EclipseBloom/EclipseBloomAuthoredAssets.cs",
    "Integration/EclipseBloom/EclipseBloomAssetManager.cs",
    "Integration/EclipseBloom/EclipseBloomVisuals.cs",
    "Integration/EclipseBloom/EclipseBloomVisuals_Geometry.cs",
    "Integration/EclipseBloom/EclipseBloomRules.cs",
    "Integration/EclipseBloom/EclipseBloomGroundProjection.cs",
)


def run_fixture(source_root=ROOT, output_root=None):
    output_root = output_root or ROOT / "Build/runtime-regressions/EclipseBloomRuntime"
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=output_root))
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core"
    health = invocation / "HealthSimpleBase.cs"
    health.write_text("using Random = UnityEngine.Random;\n" +
                      (official / "HealthSimpleBase.cs").read_text(encoding="utf-8-sig"), encoding="utf-8")
    sources = [source_root / name for name in PRODUCTION]
    sources += [HERE / filename for filename in ("Program.cs", "ModuleRegression.cs", "PoolRegression.cs", "GeometryRegression.cs", "GroundProjectionRegression.cs", "Async.cs", "UnityStubs.cs", "HostStubs.cs")]
    sources += [health, official / "DamageReceiver.cs"]
    project = invocation / "EclipseBloomRuntime.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
        + '</ItemGroup></Project>', encoding="utf-8")
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return run_project_fixture(project, invocation / "execution", ROOT)


def run_introduction_fixture(source_root=ROOT, output_root=None):
    output_root = output_root or ROOT / "Build/runtime-regressions/EclipseBloomRuntime/Introduction"
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=output_root))
    sources = [source_root / name for name in INTRO_PRODUCTION]
    sources += [HERE / name for name in ("IntroductionRegression.cs", "IntroductionStubs.cs", "UnityStubs.cs")]
    project = invocation / "EclipseBloomIntroduction.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
        + '</ItemGroup></Project>', encoding="utf-8")
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return run_project_fixture(project, invocation / "execution", ROOT)


def main():
    code, output = run_fixture()
    print(output)
    intro_code, intro_output = run_introduction_fixture()
    print(intro_output)
    asset_code, asset_output = run_asset_fixture()
    print(asset_output)
    return max(code, intro_code, asset_code)


def run_asset_fixture(source_root=ROOT, output_root=None):
    output_root = output_root or ROOT / "Build/runtime-regressions/EclipseBloomRuntime/AuthoredAssets"
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=output_root))
    sources = [source_root / name for name in ASSET_PRODUCTION]
    sources += [HERE / name for name in ("AssetLeaseRegression.cs", "AssetLeaseStubs.cs", "UnityStubs.cs")]
    project = invocation / "EclipseBloomAuthoredAssets.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
        + '</ItemGroup></Project>', encoding="utf-8")
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return run_project_fixture(project, invocation / "execution", ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
