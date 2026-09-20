"""Execute production registration/restore and summon request lifecycle members."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/manual-equipment-recovery'
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    # clean_source preserves strings but strips comments; these selected members
    # have no brace-containing literals. Return the original body unchanged.
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return source[start:index + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    inputs = {}

    def read(path):
        data = (ROOT / path).read_bytes()
        inputs[path] = hashlib.sha256(data).hexdigest()
        return data.decode('utf-8-sig')

    host = clean_source(read('Integration/BossRushIntegration.cs'))
    assert 'NewWeaponRuntime.RegisterRuntimeConfigs();' in member(host, 'private void RegisterCustomWeaponRuntimeConfigs(')
    restore = read('Integration/Reforge/ReforgeDataPersistence.cs')
    runtime = read('Integration/NewWeapons/Common/NewWeaponRuntime.cs')
    forge = read('Integration/AffixForge/AffixForgeSystem.cs')
    data = read('Integration/AffixForge/AffixItemData.cs')
    action = member(read('Integration/NewWeapons/SummonStaff/SummonStaffAction.cs'), 'public class SummonStaffAction')
    manager = read('Integration/NewWeapons/SummonStaff/SummonStaffManager.cs')
    generated = 'using System; using System.Collections.Generic; using ItemStatsSystem; using UnityEngine;\nnamespace BossRush {\n'
    generated += 'static class NewWeaponRuntime {\n' + member(runtime, 'internal static void RegisterRuntimeConfigs(') + '\n}\n'
    generated += 'static class AffixForgeSystem {\n' + member(forge, 'public static bool CanAffixForge(') + '\n}\n'
    generated += 'static partial class AffixItemData {\n' + member(data, 'public static AffixEquipMask GetEquipMask(') + '\n}\n'
    generated += 'static partial class CustomItemRuntimeStateHelper {\n'
    generated += member(restore, 'private sealed class RuntimeConfigEntry') + '\n'
    generated += 'private static readonly Dictionary<int, RuntimeConfigEntry> runtimeConfigEntries = new Dictionary<int, RuntimeConfigEntry>();\n'
    for signature in ('public static void RegisterRuntimeConfiguredItem(', 'public static void RegisterMeleeRuntimeConfiguredItem(',
                      'public static bool IsRuntimeConfiguredType(', 'public static bool EnsureCustomItemConfigured(',
                      'public static bool RestoreRuntimeState(', 'public static bool NeedsMeleeRuntimeConfig('):
        generated += member(restore, signature) + '\n'
    generated += '} partial class SummonStaffAction {\n'
    for signature in ('protected override bool OnAbilityStart(', 'protected override void OnAbilityUpdate(',
                      'protected override void OnAbilityStop(', 'internal void CancelPendingSummons(',
                      'private void OnDestroy(', 'private bool IsRequestValid('):
        generated += member(action, signature) + '\n'
    generated += '} partial class SummonStaffManager {\n'
    for signature in ('private void CancelPreparation(', 'private void OnHoldItemChanged('):
        generated += member(manager, signature) + '\n'
    generated += '}}'
    extracted = OUT / 'Production.cs'
    extracted.write_text(generated, encoding='utf-8')
    sources = [extracted, HERE / 'Program.cs', HERE / 'Stubs.cs',
               ROOT / 'Integration/NewWeapons/Common/NewWeaponIds.cs',
               ROOT / 'Integration/NewWeapons/Common/NewWeaponItemAttributes.cs']
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / 'Regression.csproj'
    path.write_text(project, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps(inputs, indent=2), encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(path), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
