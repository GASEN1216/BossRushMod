"""Execute the exact production registration method and installed native registry/handler IL."""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent


def main(output=None):
    out = Path(output) if output else ROOT / 'Build/runtime-regressions/CoopInstalledApi'
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='spectator-registration-', dir=out))
    production = ROOT / 'Common/Coop/BossRushCoopTransport.cs'
    source = production.read_text(encoding='utf-8-sig')
    start = '        private static void EnsureSpectatorForceEndRegistration(Assembly nativeAssembly)'
    end = '        private void OnRawMessage(object[] args)'
    assert source.count(start) == 1 and source.count(end) == 1
    # Keep the production method verbatim, with no substituted statements or test-only branches.
    method = source[source.index(start):source.index(end)]
    sys.path.insert(0, str(ROOT / 'tests'))
    from cs_source_util import clean_source
    clean = clean_source(source)
    bind = clean[clean.index('internal bool TryBind()'):clean.index(start.strip())]
    assert bind.index('EnsureSpectatorForceEndRegistration(serviceType.Assembly);') < bind.index('apiType.GetMethod("RegisterHandler"')
    extracted = fresh / 'Production.cs'
    extracted.write_text('using System; using System.Reflection;\nnamespace BossRush { internal sealed class BossRushCoopTransport {\n' + method + '\n} }\n', encoding='utf-8')
    program = fresh / 'Program.cs'; shutil.copy2(HERE / 'Program.cs', program)
    workshop = Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH', 'D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    game = Path(os.environ.get('GAME_PATH', 'D:/software/steam/steamapps/common/Escape from Duckov')) / 'Duckov_Data/Managed'
    dependency = Path(os.environ.get('BOSSRUSH_COOP_DEPENDENCY_PATH', str(workshop.parent / '3591339491')))
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    native = workshop / 'EscapeFromDuckovCoopMod.dll'
    if os.name != 'nt' or not native.is_file() or not harmony.is_file() or not game.is_dir():
        raise SystemExit('Requires Windows, actual installed COOP/game/Harmony DLLs; no stub-only PASS.')
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    compiler = sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    target = fresh / 'SpectatorRegistration.exe'
    args = ['/nologo', '/target:exe', '/langversion:7.3', '/optimize+', '/nostdlib+', '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    args += ['"' + str(path) + '"' for path in (extracted, program)]
    response = fresh / 'compile.rsp'; response.write_text('\n'.join(args), encoding='utf-8-sig')
    shutil.copy2(harmony, fresh / '0Harmony.dll')
    built = subprocess.run(['dotnet', str(compiler), '/noconfig', '@' + str(response)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (fresh / 'compile.log').write_bytes(built.stdout)
    print(built.stdout.decode('utf-8', errors='replace'), flush=True)
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    result = dict(utc=datetime.now(timezone.utc).isoformat(), compileExit=built.returncode, runExit=None,
        freshTargetPath=str(target), targetSha256=sha(target) if built.returncode == 0 else None,
        productionSha256=sha(production), extractedMethodSha256=hashlib.sha256(method.encode('utf-8')).hexdigest(),
        programSha256=sha(program), nativeSha256=sha(native), nativePath=str(native), harmonySha256=sha(harmony),
        noGameProcess=True, noPlayerSaveAccess=True, completeNativeHandlerIL=True)
    if built.returncode == 0:
        ran = subprocess.run([str(target), str(workshop), str(game), str(dependency)], cwd=fresh, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (fresh / 'run.log').write_bytes(ran.stdout); result['runExit'] = ran.returncode
        print('Execute fresh TargetPath: ' + str(target) + '\nSHA-256: ' + result['targetSha256'], flush=True)
        print(ran.stdout.decode('utf-8', errors='replace'), flush=True)
    (fresh / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    (out / 'spectator-registration-results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return built.returncode or result['runExit']


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output'); args = parser.parse_args()
    raise SystemExit(main(args.output))
