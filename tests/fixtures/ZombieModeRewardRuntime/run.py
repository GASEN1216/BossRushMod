"""Compile current reward/NPC ownership methods with deterministic host adapters."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieModeRewardRuntime'


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def read(name):
        relative = 'ZombieMode/' + name
        raw = (ROOT / relative).read_bytes()
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        return raw.decode('utf-8-sig')

    effects = read('ZombieModeRewardEffects.cs')
    fields = member(effects, 'internal sealed partial class ZombieModeRuntimeModule')
    source = 'using System; using System.Collections.Generic; using UnityEngine; using ItemStatsSystem;\nnamespace BossRush {\n' + fields + '\ninternal sealed partial class ZombieModeRuntimeModule {\n'
    selections = {
        'ZombieModeRewardNpcServices.cs': [
            'internal ZombieModeTemporaryNpc FindZombieModeTemporaryNpc(',
            'public ZombieModeNpcCatalog.MerchantStockEntry[] GetZombieModeMerchantStock(',
            'public int GetZombieModeNpcServicePrice(',
            'public bool TryPurchaseZombieModeMerchantStock(',
            'public bool TryUseZombieModeNurseService(',
        ],
        'ZombieModeRuntimeModule_RewardCatalogAndSelection.cs': [
            'public bool SpendZombieModePurificationPoints(', 'internal void RefundZombieModePurificationPoints(',
        ],
        'ZombieModeRewardRuntimeModifiers.cs': [
            'private void EnsureZombieModeOptionPlayerHealthListener()', 'private void UnregisterZombieModeOptionPlayerHealthListener()',
        ],
        'ZombieModeRewardProjectileSpread.cs': [
            'private void RestoreZombieModeProjectileSpreadState()',
            'private void RestoreZombieModeProjectileSpreadSnapshot(',
            'private void EnsureZombieModeProjectileSpreadListener()',
        ],
        'ZombieModeRewardOptionCore.cs': ['internal void RemoveZombieModeOptionRuntimeEffects()'],
        'ZombieModeRewardEffectsAndNpc.cs': [
            'internal void BindZombieModeTemporaryNpcServices(', 'private GameObject CreateZombieModeTemporaryCourierNpc(',
        ],
        'ZombieModeDropsAndPerformance.cs': [
            'internal void RecycleZombieModeTemporaryNpcs(', 'internal void RecycleZombieModeTemporaryRealNpcs(',
            'private void CloseZombieModeTemporaryRealNpcServices(ZombieModeTemporaryRealNpcRecord npc)',
            'private void CloseZombieModeTemporaryRealNpcServices(GameObject npcObject)',
            'internal void RecycleZombieModeSafeZoneBoundTemporaryNpcs(',
            'internal void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(',
        ],
    }
    for name, signatures in selections.items():
        text = read(name)
        for signature in signatures:
            source += member(text, signature) + '\n'
    npc = read('ZombieModeRewardEffectsAndNpc.cs')
    for declaration in ('private System.Func<string, GameObject> resolveZombieModeNpcPrefab;',
                        'private System.Action<GameObject> addZombieModeCourierInteraction;'):
        assert npc.count(declaration) == 1
        source += declaration + '\n'
    source += '}}\n'
    generated = OUT / 'Production.cs'
    generated.write_text(source, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    sources = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs', ROOT / 'Utilities/RunScopedRegistry.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
