"""Execute actual seed and dragon reward delivery against a bounded inventory."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/boss-reward-delivery"


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == '{':
            depth += 1
        elif source[i] == '}':
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = (ROOT / 'LootAndRewards/LootAndRewardsSpecialLoot.cs').read_text(encoding='utf-8-sig')
    methods = '\n'.join(member(source, signature) for signature in (
        'private IEnumerator AddDragonDescendantLoot(Inventory inv)',
        'private bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)',
    ))
    extracted = OUT / 'DragonRewards.cs'
    extracted.write_text('using System; using System.Collections; using ItemStatsSystem; '
                         'namespace BossRush { public partial class ModBehaviour {\n' + methods + '\n} }',
                         encoding='utf-8')
    sources = [ROOT / 'Utilities/InteractableLootboxInventoryHelper.cs',
               ROOT / 'Integration/BackMountain/BackMountainSeedDrops.cs',
               HERE / 'Program.cs', HERE / 'Stubs.cs', extracted]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / 'BossRewardDelivery.csproj'
    path.write_text(project, encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(path), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
