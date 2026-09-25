"""Run the production EquipmentFactory registration policy without Unity."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/equipment-configurator-registry"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = (ROOT / "Integration/EquipmentFactory.cs").read_text(encoding="utf-8-sig")
    start = source.index("private static readonly List<Action<Item, string>> gunConfigurators")
    end = source.index("private static Dictionary<int, ItemAgent> loadedModels", start)
    members = source[start:end]
    generated = OUT / "Production.cs"
    generated.write_text(
        "using System; using System.Collections.Generic; namespace BossRush {"
        "public class Item {} public static partial class EquipmentFactory {" + members +
        "internal static void ApplyGun(Item item) { ApplyRegisteredConfigurators(gunConfigurators, item, \"gun\"); }"
        "internal static void ApplyGeneral(Item item) { ApplyRegisteredConfigurators(equipmentConfigurators, item, \"general\"); }"
        "}}", encoding="utf-8")
    registration = (ROOT / "Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs").read_text(encoding="utf-8-sig")
    start = registration.index("internal void LoadEquipmentContent()")
    opening = registration.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (registration[end] == "{") - (registration[end] == "}")
        end += 1
    bootstrap = OUT / "ProductionBootstrap.cs"
    bootstrap.write_text("using System; namespace BossRush { internal sealed class IntegrationRuntimeModule {\n" +
                         registration[start:end] + "\n}}", encoding="utf-8")
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />'
                       for p in (generated, bootstrap, HERE / "Program.cs", HERE / "EquipmentBootstrapProbe.cs"))
    project = OUT / "EquipmentConfiguratorRegistry.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                       '<ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
