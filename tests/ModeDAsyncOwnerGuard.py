"""Mode D deferred dispatch and completion must retain their runtime generation."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))

def body(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if not depth:
            return re.sub(r'\s+', ' ', source[opening:index + 1])
    raise ValueError(signature)

def require(source, statement, message):
    if statement not in source:
        raise AssertionError(message)

def main():
    waves = read('ModeD/ModeDRuntimeModule_Waves.cs')
    queue = body(waves, 'private System.Collections.IEnumerator SpawnModeDWaveEnemiesCoroutine(')
    require(queue, 'ModeDRuntimeModule.CaptureValidity(owner, true)', 'queue must start its own wave generation')
    require(queue, 'for (int i = 0; i < reusableSpawnQueue.Count; i++) { if (!isQueueCurrent()) yield break; var info = reusableSpawnQueue[i];', 'stale queue must stop before consuming reused dispatch entries')
    spawn = body(waves, 'private void SpawnModeDEnemy(')
    require(spawn, 'ModeDRuntimeModule.CaptureValidity(owner, false)', 'individual spawn must retain runtime ownership')
    require(spawn, 'isActiveCheck: isSpawnCurrent,', 'spawn core must receive the runtime ownership gate')
    if spawn.count('if (isSpawnCurrent()) ResolveModeDSpawnCount(waveToken);') != 2:
        raise AssertionError('success/failure completion must both validate runtime ownership')
    resolution = read('ModeD/ModeDRuntimeModule_WaveResolution.cs')
    require(spawn, 'ResolveModeDSpawnCount(waveToken);', 'spawn result must reach its module completion method')
    resolved = body(resolution, 'internal void ResolveModeDSpawnCount(')
    require(resolved, 'if (modeDActive && modeDWaveIndex == waveToken)', 'late spawn results must not cross a wave boundary')
    require(resolved, 'modeDSpawnResolvedInCurrentWave++;', 'current spawn result must increment the owner count')
    completed = body(resolution, 'internal void TryResolveModeDWaveComplete(')
    require(completed, 'if (modeDSpawnResolvedInCurrentWave < modeDExpectedEnemiesInCurrentWave)', 'wave must wait for all spawn results')
    require(completed, 'OnModeDWaveComplete();', 'wave completion must retain the original host conclusion entry')
    auto_source = read('ModeD/ModeDRuntimeModule_AutoNext.cs')
    require(body(waves, 'internal void OnModeDWaveComplete('), 'ScheduleAutoNextWave(interval);', 'module completion must schedule on the same owner')
    auto = body(auto_source, 'private System.Collections.IEnumerator ModeDAutoNextWave(')
    require(auto, 'ModeDRuntimeModule.CaptureValidity(owner, false)', 'auto-next-wave must retain its owner')
    if auto.count('if (!isAutoNextCurrent()) yield break;') != 2:
        raise AssertionError('auto-next-wave must stop during wait and before final shared state mutation')
    lifecycle = read('ModeD/ModeDRuntimeModule_Lifecycle.cs')
    for method in ('internal void StartModeD()', 'internal void EndModeD()'):
        require(body(lifecycle, method), '{ ModeDRuntimeModule.Invalidate(owner);', 'entry and cleanup must invalidate previous run: ' + method)
    runtime = read('ModeD/ModeDRuntimeModule.cs')
    capture = body(runtime, 'internal static System.Func<bool> CaptureValidity(')
    require(capture, 'int wave = runtime.modeDWaveIndex;', 'Mode D captured wave must come from the module owner')
    require(capture, 'runtime.generation == capturedGeneration && runtime.modeDActive', 'Mode D activity must come from the module owner')
    require(capture, 'runtime.modeDWaveIndex == wave', 'Mode D wave validation must read module state')
    if 'expectedOwner.IsModeDActive' in capture or 'expectedOwner.ModeDWaveIndex' in capture:
        raise AssertionError('Mode D validity still reads host state')
    for method in ('public override void OnSceneLoaded(', 'public override void OnDestroy('):
        require(body(runtime, method), 'generation++;', 'scene and destruction must invalidate runtime: ' + method)
    print('ModeDAsyncOwnerGuard: PASS')

if __name__ == '__main__':
    try:
        main()
    except (AssertionError, ValueError) as error:
        raise SystemExit('ModeDAsyncOwnerGuard: FAIL - ' + str(error))
