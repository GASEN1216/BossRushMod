"""Execute production area targeting/delivery and current installed native damage IL."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def member(path, signature):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def main(out):
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='zone-effects-', dir=out))
    pollution = 'ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs'
    boss = 'ZombieMode/ZombieModeRuntimeModule_BossController.cs'
    coop = 'ZombieMode/ZombieModeRuntimeModule_Coop.cs'
    generated = 'using System; using UnityEngine; using Duckov.Buffs; namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    for path, signature in (
        (pollution, 'internal void TryApplyZombieModePlayerSlowInArea('),
        (pollution, 'internal void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin,'),
        (pollution, 'internal void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source,'),
        (boss, 'internal void TryApplyZombieModePlayerSlow('),
        (coop, 'private static string CoopZombieParticipantKey('),
        (coop, 'private bool IsCoopZombieParticipant('),
        (coop, 'private bool IsCoopZombieLocalAssetCurrent('),
    ):
        generated += member(path, signature) + '\n'
    extracted = fresh / 'Production.cs'
    generated += '} public sealed partial class ModBehaviour {' + member('ZombieMode/ZombieModeCombatHostBridge.cs', 'internal void TryApplyZombieModePlayerSlowInPersistentArea(') + '}'
    generated += member('ZombieMode/ZombieModeBossController.cs', 'public abstract class ZombieModeTimedRunScopedRuntime')
    generated += member('ZombieMode/ZombieModeBossController.cs', 'public sealed class ZombieModeAreaTickRuntime')
    extracted.write_text(generated + '}', encoding='utf-8')
    sources = [extracted, HERE / 'Program.cs', HERE / 'PersistentZones.cs', HERE / 'NativeDamage.cs'] + [ROOT / path for path in (
        'ZombieMode/ZombieModeCoopZoneEffects.cs', 'Utilities/CoopPersonalDelivery.cs',
        'Common/Coop/BossRushCoopJson.cs', 'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs')]
    workshop = Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH', 'D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    game = Path(os.environ.get('GAME_PATH', 'D:/software/steam/steamapps/common/Escape from Duckov')) / 'Duckov_Data/Managed'
    dependency = Path(os.environ.get('BOSSRUSH_COOP_DEPENDENCY_PATH', str(workshop.parent / '3591339491')))
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    native = workshop / 'EscapeFromDuckovCoopMod.dll'
    if os.name != 'nt' or not native.is_file() or not harmony.is_file() or not game.is_dir():
        raise SystemExit('Requires Windows and actual installed COOP/game/Harmony; no stub-only PASS.')
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    compiler = sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    target = fresh / 'ZombieCoopZoneEffects.exe'
    args = ['/nologo', '/target:exe', '/langversion:7.3', '/optimize+', '/nostdlib+', '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    args += ['"' + str(path) + '"' for path in sources]
    response = fresh / 'compile.rsp'; response.write_text('\n'.join(args), encoding='utf-8-sig')
    shutil.copy2(harmony, fresh / '0Harmony.dll')
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    result = dict(freshTargetPath=str(target), sources={str(path): sha(path) for path in sources + [ROOT / pollution, ROOT / boss, ROOT / coop, ROOT / 'ZombieMode/ZombieModeBossController.cs', ROOT / 'ZombieMode/ZombieModeCombatHostBridge.cs']},
                  nativePath=str(native), nativeSha256=sha(native), harmonySha256=sha(harmony))
    built = subprocess.run(['dotnet', str(compiler), '/noconfig', '@' + str(response)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (fresh / 'compile.log').write_bytes(built.stdout); result['compileExit'] = built.returncode
    print(built.stdout.decode('utf-8', errors='replace'), flush=True)
    if built.returncode == 0:
        result['targetSha256'] = sha(target)
        ran = subprocess.run([str(target), str(workshop), str(game), str(dependency)], cwd=fresh, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (fresh / 'run.log').write_bytes(ran.stdout); result['runExit'] = ran.returncode
        print('Execute fresh TargetPath: ' + str(target) + '\nSHA-256: ' + result['targetSha256'], flush=True)
        print(ran.stdout.decode('utf-8', errors='replace'), flush=True)
    (out / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return built.returncode or result.get('runExit', 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output', type=Path, default=ROOT / 'Build/runtime-regressions/ZombieCoopZoneEffects')
    raise SystemExit(main(parser.parse_args().output.resolve()))
