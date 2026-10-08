"""Execute production localization, equipment registration and configuration entry order."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import sys
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(REPO / "tests"))
sys.path.insert(0, str(REPO / "tools"))
from cs_source_util import clean_source
from run_runtime_regressions import run_project_fixture


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=REPO)
    parser.add_argument("--output-root", type=Path, default=REPO / "Build/phantom-scythe-localization")
    args = parser.parse_args()
    root, out = args.source_root.resolve(), args.output_root.resolve()
    out.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def read(path):
        data = (root / path).read_bytes()
        hashes[path] = hashlib.sha256(data).hexdigest()
        return clean_source(data.decode("utf-8-sig"))

    factory = read("Integration/EquipmentFactory.cs")
    policy = factory[factory.index("private static readonly List<Action<Item, string>> gunConfigurators"):
                     factory.index("private static Dictionary<int, ItemAgent> loadedModels")]
    bootstrap = read("Integration/EquipmentConfiguratorBootstrap.cs")
    hook = member(read("Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs"),
                  "internal void InjectLocalization_Extra_Integration()")
    weapon = read("Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs")
    entry_members = "\n".join(member(weapon, signature) for signature in (
        "public static void RegisterEquipmentConfigurator()",
        "public static bool TryConfigure(Item item)",
        "public static bool TryConfigure(Item item, string baseName)",
        "private static bool TryConfigureInternal(Item item)",
        "private static void InjectLocalization(Item item)"))
    l10n = read("Localization/L10n.cs")
    language = member(l10n, "public static bool IsChinese") + member(l10n, "public static string T(string cn, string en)")
    # Other content's localization/configuration is outside this fixture's boundary.
    other_calls = {}
    for typename, method in re.findall(r"\b([A-Z]\w*)\.(\w+)\(\);", hook + bootstrap):
        if typename not in ("PhantomWitchScytheLocalization", "PhantomWitchScytheWeaponConfig"):
            other_calls.setdefault(typename, set()).add(method)
    noops = "\n".join("static class " + typename + " { " + " ".join(
        "public static void " + method + "() {}" for method in sorted(methods)) + " }"
        for typename, methods in sorted(other_calls.items()))
    generated = out / "Production.cs"
    generated.write_text(
        "using System; using System.Collections.Generic; using ItemStatsSystem; using UnityEngine; using SodaCraft.Localizations;\nnamespace BossRush {\n"
        + "public static partial class EquipmentFactory {" + policy
        + "public static void Apply(Item item, string name) { ApplyRegisteredConfigurators(equipmentConfigurators, item, name); }}\n"
        + "internal sealed class IntegrationRuntimeModule { private ModBehaviour _owner = new ModBehaviour(); private void InjectModeFItemLocalization() {} " + hook + "}\n"
        + "public static partial class PhantomWitchScytheWeaponConfig { private const string SCYTHE_BASE_NAME = \"PhantomScythe\"; " + entry_members + "}\n"
        + "static class L10n {" + language + "}\n" + noops + "\n}", encoding="utf-8")
    links = [
        "Localization/PhantomWitchScytheLocalization.cs", "Localization/LocalizationHelper.cs",
        "Integration/EquipmentConfiguratorBootstrap.cs",
        "Integration/PhantomWitch/PhantomWitchScytheConfig.cs",
        "Integration/PhantomWitch/PhantomWitchScytheIds.cs",
        "Common/Equipment/EquipmentAbilityConfig.cs",
    ]
    for path in links:
        read(path)
    files = [generated, HERE / "Program.cs", HERE / "Stubs.cs"] + [root / path for path in links]
    project = out / "Regression.csproj"
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in files)
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
                       + includes + '</ItemGroup></Project>', encoding="utf-8")
    (out / "source-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    code, output = run_project_fixture(project, out / "runs", REPO)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
