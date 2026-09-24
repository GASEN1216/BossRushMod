"""Execute the production affix selection refresh and lifecycle against a UI host."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/affix-selection-ui'
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return source[start:index + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ui = ROOT / 'Integration/Reforge/ReforgeUIManager_AffixForge.cs'
    shared = ROOT / 'Integration/Reforge/ReforgeUIManager.cs'
    lifecycle = ROOT / 'Integration/Reforge/ReforgeUIManager_RuntimeAndCleanup.cs'
    state = ROOT / 'Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs'
    feel = ROOT / 'Integration/Reforge/ReforgeUIManager_Feel.cs'
    text = ui.read_text(encoding='utf-8-sig')
    shared_text = clean_source(shared.read_text(encoding='utf-8-sig'))
    entry = member(shared_text, 'private static void OnItemSelectionChanged(')
    assert 'if (AffixForge_HandleSelectionChanged()) return;' in entry
    assert entry.index('selectedItem = newItem;') < entry.index('AffixForge_HandleSelectionChanged()')
    cleanup = member(clean_source(lifecycle.read_text(encoding='utf-8-sig')), 'public static void Cleanup(')
    assert 'CleanupAffixForgeUI();' in cleanup
    signatures = ('internal static bool AffixForge_HandleSelectionChanged(',
        'private static void ScheduleAffixSelectionRefresh(', 'private static void StopAffixSelectionRefresh(',
        'private static System.Collections.IEnumerator RefreshAffixSelectionAfterOfficialUI(',
        'internal static bool AffixForge_HandleButtonState(', 'internal static void CleanupAffixForgeUI(',
        'private static void ApplyAffixButtonText(', 'private static void UpdateAffixButtonInteractable(',
        'private static bool CanAffordAffixRoll(', 'private static bool HasUnlockedAffixSlot(',
        'private static void FixCannotForgeIndicator(', 'private static void FixNoItemSelectedIndicator(')
    production = ('using System; using System.Globalization; using System.Reflection; using System.Collections.Generic; '
                  'using UnityEngine; using UnityEngine.UI; using TMPro; using Duckov.UI; '
                  'using ItemStatsSystem; namespace BossRush { public static partial class ReforgeUIManager {\n')
    production += '\n'.join(member(text, sig) for sig in signatures)
    production += '\n' + member(shared_text, 'private static FieldInfo CannotDecomposeField')
    production += '\n' + member(state.read_text(encoding='utf-8-sig'), 'private static void UpdateReforgeButtonInteractable(')
    # 2026-09-24（A-07）：「随机词缀 · 价钱」按钮与重铸共用千分位格式
    production += '\n' + member(feel.read_text(encoding='utf-8-sig'), 'private static string FormatReforgeAmount(')
    production += '\n}}'
    extracted = OUT / 'Production.cs'
    extracted.write_text(production, encoding='utf-8')
    inputs = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in (ui, shared, lifecycle, state, feel)}
    (OUT / 'production-sha256.json').write_text(json.dumps(inputs, indent=2), encoding='utf-8')
    files = [extracted, HERE / 'Program.cs', HERE / 'Stubs.cs']
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    path = OUT / 'Regression.csproj'
    path.write_text(project, encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(path), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
