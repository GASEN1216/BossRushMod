"""Run the production EquipmentFactory registration policy without Unity."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/equipment-configurator-registry"


def definition(source, signature):
    start = source.index(signature)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def factory_registration_probe():
    registry = (ROOT / "Integration/BossRushDynamicItemRegistry.cs").read_text(encoding="utf-8-sig")
    generated = OUT / "ProductionFactoryRegistration.cs"
    bundle_methods = ("public static int LoadBundle(string bundleName)", "private static bool IsBundleReady(string bundleName)",
                      "private static int LoadBundleInternal(string bundlePath, string bundleName)")
    chunks = ["using System; using System.Collections.Generic; using System.IO; namespace FactoryProbe {",
              definition(registry, "internal sealed class FactoryBundleRegistration")]
    for factory, lookup in (("ItemFactory", "FindHeldBundle"), ("EquipmentFactory", "FindOwnedBundle")):
        source = (ROOT / "Integration" / (factory + ".cs")).read_text(encoding="utf-8-sig")
        chunks += ["internal static partial class " + factory + " {",
                   *(definition(source, method) for method in bundle_methods),
                   definition(source, "private static AssetBundle " + lookup + "(string bundleName)")]
        if factory == "ItemFactory":
            chunks.append(definition(source, "internal static void ConfigureItemPrefab(Item item)"))
        else:
            processing = (ROOT / "Integration/EquipmentFactory_ItemProcessing.cs").read_text(encoding="utf-8-sig")
            chunks += [*(definition(source, signature) for signature in (
                "public static bool TryGetLoadedModel(", "public static bool TryBindLoadedMeleeModel(",
                "internal static bool HasBoundMeleeModel(", "private static ItemAgent GetHandheldPrefab(",
                "internal static bool HasBoundEquipmentModel(")),
                definition(processing, "private static bool FinalizeCustomMeleeWeapon(")]
        chunks.append("}")
    chunks += ["internal static partial class BossRushDynamicItemRegistry {",
               definition(registry, "internal static bool EnsureRegistered(int typeId)"),
               definition(registry, "private static bool HasSplitModel("),
               definition(registry, "private static bool EnsureItemConfiguratorsRegistered()"), "}}"]
    generated.write_text("\n".join(chunks), encoding="utf-8")
    return generated


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
    bootstrap = OUT / "ProductionBootstrap.cs"
    bootstrap.write_text("using System; namespace BossRush { internal sealed class IntegrationRuntimeModule {\n" +
                         definition(registration, "internal void LoadEquipmentContent()") + "\n}}", encoding="utf-8")
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />'
                       for p in (generated, bootstrap, factory_registration_probe(), HERE / "Program.cs",
                                 HERE / "EquipmentBootstrapProbe.cs", HERE / "FactoryRegistrationProbe.cs"))
    project = OUT / "EquipmentConfiguratorRegistry.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                       '<ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
