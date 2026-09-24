"""Execute manual-review regressions against current production methods."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/manual-september-review'


def member(text, signature):
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0:
            return text[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = ROOT / 'PetNest/PetNestHatchRevealView.cs'
    raw = source.read_text(encoding='utf-8-sig')
    signatures = ('private IEnumerator PlayRoutine()', 'private static IEnumerator WaitForPresentation(',
                  'private void ShowResult()', 'private void CompleteReveal()', 'private void OnDismiss()')
    # Extract unchanged production method bodies; host controls input, pause and coroutine advancement.
    code = 'using System.Collections; using System.Collections.Generic; using UnityEngine; namespace BossRush { partial class PetNestHatchRevealView {\n'
    code += '\n'.join(member(raw, signature) for signature in signatures) + '\n}}'
    (OUT / 'Reveal.cs').write_text(code, encoding='utf-8')
    expedition = ROOT / 'PetNest/PetNestExpeditionRevealView.cs'
    code = 'using System.Collections; using UnityEngine; namespace BossRush { partial class PetNestExpeditionRevealView {\n'
    code += '\n'.join(member(expedition.read_text(encoding='utf-8-sig'), signature) for signature in (
        'private IEnumerator PlayRoutine()', 'private static IEnumerator WaitForPresentation(')) + '\n}}'
    (OUT / 'ExpeditionReveal.cs').write_text(code, encoding='utf-8')
    combat = ROOT / 'Integration/Bonus/SetBonusVisuals.cs'
    code = 'using System; using UnityEngine; namespace BossRush { public partial class ModBehaviour {\n'
    code += member(combat.read_text(encoding='utf-8-sig'), 'private bool TryResolveSetBonusEnemyTarget(')
    code += '\n public bool CanTrigger(Health target, DamageInfo info) { CharacterMainControl victim; Vector3 position; return TryResolveSetBonusEnemyTarget(target,info,out victim,out position); } }}'
    (OUT / 'Combat.cs').write_text(code, encoding='utf-8')
    maps = []
    for path in sorted((ROOT / 'Assets/SpawnPoints').glob('*.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        if isinstance(data, dict) and data.get('sceneName') and data.get('sceneID') and data.get('spawnPoints'):
            maps.append(data)
    (OUT / 'maps.json').write_text(json.dumps(maps), encoding='utf-8')
    linked = [ROOT / path for path in (
        'PetNest/PetNestBaseIdleSpawner.cs', 'PetNest/PetNestCompanionRuntime.cs', 'PetNest/PetNestPetProxyBridge.cs',
        'ModeH/ModeHMapSupportRegistry.cs', 'Common/MapConfig/BossRushMapConfig.cs',
        'Integration/Codex/CodexSceneNames.cs')]
    files = linked + [OUT / 'Reveal.cs', OUT / 'ExpeditionReveal.cs', OUT / 'Combat.cs', HERE / 'Program.cs', HERE / 'Stubs.cs']
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0169;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    (OUT / 'Regression.csproj').write_text(project, encoding='utf-8')
    (OUT / 'sources.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in linked + [source, expedition, combat]}, indent=2), encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(OUT / 'Regression.csproj'),
        '--configuration', 'Release', '--', str(OUT / 'maps.json')], cwd=ROOT)


if __name__ == '__main__':
    sys.exit(main())
