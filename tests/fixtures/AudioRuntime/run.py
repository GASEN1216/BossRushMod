"""Execute complete production Audio owners and current root bindings against recording hosts."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/AudioRuntime'


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
    def read(relative):
        raw = (ROOT / relative).read_bytes()
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        return raw.decode('utf-8-sig')
    production = ['Audio/BossRushAudioHooks.cs', 'Audio/BossRushAudioRuntimeService.cs']
    for relative in production:
        read(relative)
    registration = read('ModBehaviourRuntimeModules.cs')
    field = re.search(r'private readonly BossRushAudioRuntimeService audioRuntime\s*=\s*new BossRushAudioRuntimeService\(\);', registration)
    assert field
    register = member(registration, 'private void RegisterRuntimeModules()')
    first_register = register.index('runtimeModuleHost.Register(')
    binding = register[register.index('{') + 1:first_register]
    random = read('RandomEvents/RandomEventsRuntimeModuleHostBridge.cs')
    arena = read('WavesArena/WavesArenaEnemyMaintenance.cs')
    bridges = '\n'.join((member(random, 'internal SpawnEgg RandomEventSpawnEggBehaviorForRuntime'),
                         member(random, 'internal CharacterRandomPreset RandomEventEggSpawnPresetForRuntime'),
                         member(arena, 'internal CharacterRandomPreset ArenaEggSpawnPreset')))
    cleanup = member(read('Utilities/AlwaysOnRuntimeHooks.cs'), 'internal void CleanupAlwaysOnRuntimeOnDestroy()')
    magic = cleanup.index('BossRush.Patches.Compatibility.MagicBlendInitializationOrderPatch.ResetStaticCaches();')
    start = cleanup.rfind('            try', 0, magic)
    npc = cleanup.index('BossRush.Utils.NPCUIAssetCache.ResetStaticCaches();', magic)
    end = cleanup.index('            try', npc)
    cleanup_slice = cleanup[start:end]
    generated = OUT / 'ProductionBindings.cs'
    generated.write_text('using Duckov.ItemUsage; namespace BossRush { public partial class ModBehaviour {\n'
                         + field.group(0) + '\ninternal void BindProductionAudioForFixture() {\n' + binding + '\n}\n'
                         + bridges + '\ninternal void CleanupProductionAudioForFixture() {\n' + cleanup_slice + '\n}\n}}', encoding='utf-8')
    paths = [ROOT / relative for relative in production] + [generated, HERE / 'Stubs.cs', HERE / 'Program.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
    (OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    for variant, define in (('OfficialReturn', 'OFFICIAL_RETURN'), ('CompatibleVoid', 'COMPATIBLE_VOID'), ('MissingAudio', 'MISSING_AUDIO')):
        folder = OUT / variant
        folder.mkdir(parents=True, exist_ok=True)
        project = folder / 'Regression.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                           '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                           '<AssemblyName>TeamSoda.Duckov.Core</AssemblyName><DefineConstants>' + define + '</DefineConstants>'
                           '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn>'
                           '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
        code = subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release', '--', str(folder / 'data')], cwd=ROOT)
        if code:
            return code
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
