"""Execute current starter, cash, purification, isolation order and visual cleanup methods."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieModeStarterRuntime'


def member(source, signature):
    assert source.count(signature) == 1, signature
    start=source.index(signature); opening=source.index('{',start); end=opening+1; depth=1
    while depth:
        depth += (source[end]=='{')-(source[end]=='}'); end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes={}
    def read(name):
        path=ROOT/'ZombieMode'/name; raw=path.read_bytes(); hashes[str(path.relative_to(ROOT))]=hashlib.sha256(raw).hexdigest(); return raw.decode('utf-8-sig')
    selections={
        'ZombieModeEntry_StarterLoadout.cs':['public void SelectZombieModeStarterLoadout(', 'private bool GrantZombieModeStarterLoadout(', 'private bool GrantZombieModeStarterProtectionSet(', 'private int TryGiveZombieModeStarterGuaranteedHealingItems(', 'private bool TryGiveZombieModeStarterGuaranteedHealingItem('],
        'ZombieModeCashInvestmentView.cs':['public bool ConfigureZombieModePendingCashInvestment(', 'public long GetZombieModePendingCashInvestment(', 'public int PreviewZombieModeInitialPurificationPoints('],
        'ZombiePurificationPointController.cs':['internal void ForceCollectZombieModePendingPurificationStars(', 'public void CollectZombieModePurificationPoint(', 'private static SoulCube TryGetSoulCubePrefab(', 'private static void PrewarmSoulCubePrefabCache(', 'internal void PrepareSoulCubePrefabCacheForZombieRun('],
        'ZombieModeMapIsolation.cs':['internal bool ApplyZombieModeMapIsolationShell(', 'internal void RestoreZombieModeMapIsolationShell(', 'private void DisableZombieModeOriginalSpawners(', 'private void RestoreZombieModeOriginalSpawners('],
        'ZombieModeRuntimeModule_EnemyRuntime.cs':['internal static void RestoreZombieModeVisualScale(', 'internal static void ReleaseZombieModeFootMarker('],
    }
    source='using System; using System.Collections.Generic; using UnityEngine; using Duckov.Utilities; using ItemStatsSystem;\nnamespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    for name, signatures in selections.items():
        text=read(name)
        source+='\n'.join(member(text, signature) for signature in signatures)+'\n'
    rewards=read('ZombieModeRewards.cs')
    source+='\n'.join(re.findall(r'^        internal static readonly string\[\] ZombieModeRewardTag\w+ = [^\n]+;',rewards,re.M))+'\n'
    purification=read('ZombiePurificationPointController.cs')
    source+='\n'.join(re.findall(r'^        private static (?:SoulCube|bool) s_\w+;',purification,re.M))+'\n}}'
    generated=OUT/'Production.cs'; generated.write_text(source,encoding='utf-8')
    (OUT/'production-sha256.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
    includes=''.join('<Compile Include="'+escape(str(path),{'"':'&quot;'})+'" />' for path in [generated,HERE/'Stubs.cs',HERE/'Program.cs'])
    project=OUT/'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf-8')
    return subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT)


if __name__=='__main__': raise SystemExit(main())
