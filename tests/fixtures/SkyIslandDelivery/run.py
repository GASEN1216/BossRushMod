#!/usr/bin/env python3
"""逐字执行天空岛纪念品 TryGive，验证交付回执与台账回滚边界。"""
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
    generated = """using System;
using ItemStatsSystem;
namespace BossRush {
public static class SkyIslandItems {
private sealed class Definition { }
private const string LogPrefix = "[SkyIslandItems] ";
private static Definition GetDefinition(int typeId) { return typeId == BossRushItemIds.SkyIslandHomecomingBadge ? new Definition() : null; }
""" + extracted + "\n}\n}"
    (OUT / "Generated.cs").write_text(generated, encoding="utf-8")
    (OUT / "source-hashes.txt").write_text(
        hashlib.sha256(source_path.read_bytes()).hexdigest() + " Integration/SkyIsland/SkyIslandItems.cs\n",
        encoding="utf-8")
    project = OUT / "SkyIslandDelivery.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        '<Compile Include="' + str(OUT / "Generated.cs") + '"/><Compile Include="'
        + str(HERE / "Program.cs") + '"/><Compile Include="' + str(HERE / "Stubs.cs")
        + '"/></ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
