#!/usr/bin/env python3
"""逐字执行天空岛纪念品、蛙卵和头目补发入口，链接真实库存事务验证失败回滚。"""
from pathlib import Path
import hashlib
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "runtime-regressions" / "SkyIslandDelivery"


def method(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
    raise ValueError(marker)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source_path = ROOT / "Integration" / "SkyIsland" / "SkyIslandItems.cs"
    source = source_path.read_text(encoding="utf-8-sig")
    extracted = method(source, "internal static bool TryGive(int typeId, bool toStorage, Func<bool> recordGrant")
    sky = ROOT / "DebugAndTools/SkyIsland"
    fieldcraft = (sky / "SkyIslandFieldcraft.cs").read_text(encoding="utf-8-sig")
    gnats = (sky / "SkyIslandGnats.cs").read_text(encoding="utf-8-sig")
    loot = (sky / "SkyIslandBossLoot.cs").read_text(encoding="utf-8-sig")
    generated = """using System;
using ItemStatsSystem;
namespace BossRush {
public static class SkyIslandItems {
private sealed class Definition { }
private const string LogPrefix = "[SkyIslandItems] ";
private static Definition GetDefinition(int typeId) { return typeId == BossRushItemIds.SkyIslandHomecomingBadge ? new Definition() : null; }
""" + extracted + "\n}\n" + "internal sealed partial class SkyIslandFieldcraft {\n" + method(fieldcraft, "internal bool ConsumeOne(int typeId)")
    # 旧实现保留在修复前的复现里；修复后生产只复用事务，不再保留第二份扣料算法。
    if "private static bool ConsumeFromPack(int typeId, int count)" in fieldcraft:
        generated += "\n" + method(fieldcraft, "private static bool ConsumeFromPack(int typeId, int count)")
    generated += "\n}\ninternal sealed partial class SkyIslandGnats {\n" + method(gnats, "internal bool TakeSpawn(out string message)")
    generated += "\n}\ninternal static class SkyIslandBossLoot {\n" + method(loot, "private static bool TryAddFresh(Item characterItem, int typeId)")
    generated += "\ninternal static bool Give(Item character, int id) { return TryAddFresh(character, id); }\n}\n}"
    (OUT / "Generated.cs").write_text(generated, encoding="utf-8")
    linked = [sky / "SkyIslandInventoryTransaction.cs", ROOT / "Utilities/InteractableLootboxInventoryHelper.cs",
              ROOT / "Config/ConfigItemIds.cs"]
    (OUT / "source-hashes.txt").write_text(
        "".join(hashlib.sha256(p.read_bytes()).hexdigest() + " " + p.relative_to(ROOT).as_posix() + "\n"
                for p in [source_path, sky / "SkyIslandFieldcraft.cs", sky / "SkyIslandGnats.cs", sky / "SkyIslandBossLoot.cs"] + linked),
        encoding="utf-8")
    project = OUT / "SkyIslandDelivery.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        '<Compile Include="' + str(OUT / "Generated.cs") + '"/><Compile Include="'
        + str(HERE / "Program.cs") + '"/><Compile Include="' + str(HERE / "Stubs.cs")
        + '"/>' + ''.join('<Compile Include="' + str(p) + '"/>' for p in linked)
        + '<Compile Include="' + str(HERE / "InventoryRegression.cs") + '"/></ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
