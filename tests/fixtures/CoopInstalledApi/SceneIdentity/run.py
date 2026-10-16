"""Fresh real Harmony execution of the scene identity service and installed native IL."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent


def main(output=None):
    out = Path(output) if output else ROOT / 'Build/runtime-regressions/CoopInstalledApi'
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='scene-identity-', dir=out))
    transport = ROOT / 'Common/Coop/BossRushCoopTransport.cs'
    text = transport.read_text(encoding='utf-8-sig')
    start = '    internal sealed class BossRushCoopSceneIdentityBridge : IDisposable'
    end = '    internal sealed class BossRushCoopSceneLoadCompatibility : IDisposable'
    assert text.count(start) == text.count(end) == 1
    # Extract the complete production class verbatim; no method replacement or gate substitution.
    bridge = text[text.index(start):text.index(end)]
    bridge = bridge[:bridge.rfind('    /// <summary>')]
    extracted = fresh / 'Bridge.cs'
    extracted.write_text('using System; using System.Collections.Generic; using System.Reflection; using System.Reflection.Emit; using HarmonyLib;\nnamespace BossRush {\n' + bridge + '\n}\n', encoding='utf-8')
    sources = [extracted, ROOT / 'Common/Coop/BossRushCoopSceneIdentity.cs',
        ROOT / 'Common/Coop/BossRushCoopJson.cs', ROOT / 'Common/Data/BossRushJsonValue.cs',
        ROOT / 'Utilities/SimpleJsonHelper.cs', HERE.parent / 'SceneLoadLease/Stubs.cs', HERE / 'Program.cs']
    workshop = Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH', 'D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    game = Path(os.environ.get('GAME_PATH', 'D:/software/steam/steamapps/common/Escape from Duckov')) / 'Duckov_Data/Managed'
    dependency = Path(os.environ.get('BOSSRUSH_COOP_DEPENDENCY_PATH', str(workshop.parent / '3591339491')))
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    native = workshop / 'EscapeFromDuckovCoopMod.dll'
    if os.name != 'nt' or not native.is_file() or not harmony.is_file() or not game.is_dir():
        raise SystemExit('Requires Windows, installed native COOP/game/Harmony; no stub-only PASS.')
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    compiler = sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    target = fresh / 'SceneIdentity.exe'
    args = ['/nologo', '/target:exe', '/langversion:7.3', '/optimize+', '/nostdlib+', '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    args += ['"' + str(path) + '"' for path in sources]
    response = fresh / 'compile.rsp'; response.write_text('\n'.join(args), encoding='utf-8-sig')
    shutil.copy2(harmony, fresh / '0Harmony.dll')
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    result = dict(freshTargetPath=str(target), sources={str(path): sha(path) for path in sources + [transport]},
        nativePath=str(native), nativeSha256=sha(native), harmonySha256=sha(harmony),
        completeNativeBodies=['Server_BroadcastBeginSceneLoad', 'Client_OnBeginSceneLoad', 'RPCScene.HandleSceneBeginLoad', 'RPCModApi.HandleModApiMessage'],
        noGameProcess=True, noPlayerSaveAccess=True)
    built = subprocess.run(['dotnet', str(compiler), '/noconfig', '@' + str(response)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (fresh / 'compile.log').write_bytes(built.stdout); result['compileExit'] = built.returncode
    print(built.stdout.decode('utf-8', errors='replace'), flush=True)
    if built.returncode == 0:
        result['targetSha256'] = sha(target)
        ran = subprocess.run([str(target), str(workshop), str(game), str(dependency)], cwd=fresh, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (fresh / 'run.log').write_bytes(ran.stdout); result['runExit'] = ran.returncode
        print('Execute fresh TargetPath: ' + str(target) + '\nSHA-256: ' + result['targetSha256'], flush=True)
        print(ran.stdout.decode('utf-8', errors='replace'), flush=True)
    (fresh / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    (out / 'scene-identity-results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return built.returncode or result.get('runExit', 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output'); args = parser.parse_args()
    raise SystemExit(main(args.output))
