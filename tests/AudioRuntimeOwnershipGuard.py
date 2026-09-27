"""Audio hooks forward to one service; original cache and cleanup lifetimes stay intact."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SERVICE = Path('Audio/BossRushAudioRuntimeService.cs')
HOOKS = Path('Audio/BossRushAudioHooks.cs')
REGISTRATION = Path('ModBehaviourRuntimeModules.cs')
HOST = Path('ModBehaviour.cs')
CLEANUP = Path('Utilities/AlwaysOnRuntimeHooks.cs')
RANDOM = Path('RandomEvents/RandomEventsRuntimeModuleHostBridge.cs')
ARENA = Path('WavesArena/WavesArena.cs')


def read(path):
    return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))


def compact(source):
    return re.sub(r'\s+', '', source)


def body(source, signature):
    assert source.count(signature) == 1, 'missing or duplicate audio method: ' + signature
    start = source.index('{', source.index(signature))
    end, depth = start + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start + 1:end - 1]


def main():
    service, hooks, registration, host, cleanup, random, arena = map(read,
        (SERVICE, HOOKS, REGISTRATION, HOST, CLEANUP, RANDOM, ARENA))
    assert 'internal sealed class BossRushAudioRuntimeService' in service and 'partial class ModBehaviour' not in service, 'audio behavior must belong to the independent service'
    assert 'owner.' not in service and 'ModBehaviour.Instance' not in service, 'audio service must consume narrow queries only'
    for field in (
        'private static SpawnEgg cachedSpawnEggBehavior = null;',
        'private static CharacterRandomPreset eggSpawnPreset = null;',
        'private static readonly Dictionary<string, bool> _cachedSoundFileExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);',
        'private static PostCustomSfxDelegate _cachedPostCustomSfx;',
        'private static bool _postCustomSfxResolved = false;',
    ):
        assert compact(field) in compact(service), 'audio static cache lifetime changed: ' + field
    assert 'cachedSpawnEggBehavior' not in host and 'eggSpawnPreset' not in host, 'root must not retain audio cache fields'
    assert compact('private readonly BossRushAudioRuntimeService audioRuntime = new BossRushAudioRuntimeService();') in compact(registration), 'host must retain one audio service instance'
    register = compact(body(registration, 'private void RegisterRuntimeModules()'))
    assert register.startswith(compact('audioRuntime.BindRuntimeQueries(() => playerCharacter, () => info.path);')), 'audio queries must bind before runtime callbacks without sampling'
    bind = compact(body(service, 'internal void BindRuntimeQueries(Func<MonoBehaviour> playerCharacterQuery, Func<string> modPathQuery)'))
    assert bind == compact('getPlayerCharacter = playerCharacterQuery; getModPath = modPathQuery;'), 'audio binding must only assign live queries'
    for signature, call in (
        ('public void TrySpawnEggForPlayer()', 'audioRuntime.TrySpawnEggForPlayer();'),
        ('private void TryPlayNgmSound()', 'audioRuntime.TryPlayNgmSound();'),
        ('private static void ResetBossRushAudioHooksStaticCaches()', 'BossRushAudioRuntimeService.ResetBossRushAudioHooksStaticCaches();'),
        ('public void PlaySoundEffect(string filePath)', 'audioRuntime.PlaySoundEffect(filePath);'),
    ):
        assert compact(body(hooks, signature)) == compact(call), 'audio compatibility entry must be a single forward: ' + signature
    reset = compact(body(service, 'internal static void ResetBossRushAudioHooksStaticCaches()'))
    assert reset == compact('cachedSpawnEggBehavior = null; eggSpawnPreset = null; _cachedSoundFileExists.Clear(); _cachedPostCustomSfx = null; _postCustomSfxResolved = false;'), 'audio reset must retain all original cache writes and order'
    teardown = compact(body(cleanup, 'internal void CleanupAlwaysOnRuntimeOnDestroy()'))
    assert teardown.index('MagicBlendInitializationOrderPatch.ResetStaticCaches();') < teardown.index('ModBehaviour.ResetBossRushAudioHooksStaticCaches();') < teardown.index('BossRush.Utils.NPCUIAssetCache.ResetStaticCaches();'), 'audio reset must retain its original always-on cleanup slot'
    for source, expression in (
        (random, 'internal SpawnEgg RandomEventSpawnEggBehaviorForRuntime { get { return BossRushAudioRuntimeService.CachedSpawnEggBehavior; } set { BossRushAudioRuntimeService.CachedSpawnEggBehavior = value; } }'),
        (random, 'internal CharacterRandomPreset RandomEventEggSpawnPresetForRuntime { set { BossRushAudioRuntimeService.EggSpawnPreset = value; } }'),
        (arena, 'internal CharacterRandomPreset ArenaEggSpawnPreset { get { return BossRushAudioRuntimeService.EggSpawnPreset; } }'),
    ):
        assert compact(expression) in compact(source), 'random-event and arena bridges must share the audio static cache'
    spawn = body(service, 'public void TrySpawnEggForPlayer()')
    assert spawn.index('TryPlayNgmSound();') < spawn.index('CharacterMainControl.Main') < spawn.index('Resources.FindObjectsOfTypeAll<SpawnEgg>()') < spawn.index('egg.Init('), 'audio spawn operation order changed'
    assert spawn.count('getPlayerCharacter()') == 2, 'audio player fallback must retain both original field reads'
    ngm = body(service, 'internal void TryPlayNgmSound()')
    assert 'baseDir = getModPath();' in ngm and 'SoundFileExistsCached(' not in ngm, 'ngm path must remain a live uncached resource read'
    print('AudioRuntimeOwnershipGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('AudioRuntimeOwnershipGuard: FAIL - ' + str(error))
        raise SystemExit(1)
