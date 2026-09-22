"""Replay actual Mode H entry/resume and Legacy coroutine ordering without Unity or player saves."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/modeh-scene-entry'


def extract(path, signature, hashes):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    code = text[start:end]
    hashes.append({'path': path, 'method': signature, 'sha256': hashlib.sha256(code.encode()).hexdigest()})
    return code


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = []
    scene = 'ModeH/ModeHRuntimeModule_SceneFlow.cs'
    recovery = 'ModeH/ModeHRuntimeModule_Recovery.cs'
    methods = [('ModeH/ModeHRuntimeModule.cs', 'public override void OnSceneLoaded(')]
    methods += [(scene, signature) for signature in (
        'partial void OnSceneLoadedInternal(', 'private void ScheduleSceneReadyWait(',
        'private IEnumerator WaitForModeHSceneReady(', 'private bool IsSceneReadyRequestCurrent(',
        'private static bool IsModeHSceneReady(', 'private bool HasSceneReadyWait(',
        'private void FailSceneReadyWait(', 'private void CancelSceneReadyWait()',
        'private void BeginNewRunSession()', 'private static string ResolveSceneId(',
        'private void BeginSeasonSetup(', 'private static string ComposeRunId(',
        'private static long ComposeRunSeed(')]
    methods += [(recovery, signature) for signature in (
        'private async UniTask LoadSeasonResumeScene(', 'private bool IsSeasonResumeRequestCurrent(',
        'private bool TryHandleSeasonResumeScene(', 'private void CompleteSeasonResumeScene()',
        'private void CancelSeasonResume()')]
    code = ['using System; using System.Collections; using System.Collections.Generic; using System.Threading.Tasks; using UnityEngine; using UnityEngine.SceneManagement; using Duckov.Scenes; namespace BossRush { partial class ModeHRuntimeModule {']
    for path, signature in methods:
        method = extract(path, signature, hashes)
        # Only declaration syntax adapts to the fixture host; every production method body is verbatim.
        method = method.replace('partial void OnSceneLoadedInternal(', 'private void OnSceneLoadedInternal(', 1)
        method = method.replace('public override void OnSceneLoaded(', 'public void OnSceneLoaded(', 1)
        method = method.replace('private async UniTask LoadSeasonResumeScene(', 'private async Task LoadSeasonResumeScene(', 1)
        code.append(method)
    code.append('} partial class ModBehaviour {')
    code.append(extract('ModeH/ModeHEntry.cs', 'private bool ShouldSkipLegacySceneSetupForModeH()', hashes))
    code.append(extract('Integration/BossRushIntegration_TravelAndSetup.cs',
                        'private System.Collections.IEnumerator TeleportPlayerToCustomPosition(', hashes))
    code.append('}}')
    generated = OUT / 'Extracted.cs'
    generated.write_text('\n'.join(code), encoding='utf-8')
    (OUT / 'source-hashes.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    map_names = [{'scene': data['sceneName'], 'id': data['sceneID']} for data in
                 (json.loads(p.read_text(encoding='utf-8-sig')) for p in sorted((ROOT / 'Assets/SpawnPoints').glob('*.json')))]
    (OUT / 'maps.json').write_text(json.dumps(map_names), encoding='utf-8')
    files = [generated, HERE / 'Stubs.cs', HERE / 'Program.cs']
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0169;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    (OUT / 'Regression.csproj').write_text(project, encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(OUT / 'Regression.csproj'),
                            '--configuration', 'Release', '--', str(OUT / 'maps.json')], cwd=ROOT)


if __name__ == '__main__':
    sys.exit(main())
