"""Achievement trigger state, event delegates and medal stock belong to the registered runtime."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
TRIGGERS = Path('Achievement/AchievementTriggers.cs')
RUNTIME = Path('Achievement/AchievementRuntimeModule.cs')
MEDAL = Path('Achievement/AchievementMedalItem.cs')
BRIDGE = Path('Achievement/AchievementRuntimeHooks.cs')
REGISTRATION = Path('ModBehaviourRuntimeModules.cs')
HOST = Path('ModBehaviour.cs')
INTEGRATION = Path('Integration/BossRushIntegration_StartAndScene.cs')


def read(relative):
    return clean_source((ROOT / relative).read_text(encoding='utf-8-sig'))


def method(source, signature):
    assert source.count(signature) == 1, 'missing or duplicate method: ' + signature
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[opening + 1:end - 1]


def compact(source):
    return re.sub(r'\s+', '', source)


def order(source, statements, message):
    source = compact(source)
    positions = [source.find(compact(statement)) for statement in statements]
    assert all(position >= 0 for position in positions) and positions == sorted(positions), message


def main():
    try:
        triggers, runtime, medal, bridge = [read(path) for path in (TRIGGERS, RUNTIME, MEDAL, BRIDGE)]
        registration, host, integration = [read(path) for path in (REGISTRATION, HOST, INTEGRATION)]
        for name, source in [('triggers', triggers), ('runtime', runtime), ('medal', medal)]:
            assert 'internal sealed partial class AchievementRuntimeModule' in source and 'partial class ModBehaviour' not in source, name + ' must belong to AchievementRuntimeModule'
        for field in ['achievementSystemInitialized', 'achievementCountedBossKills', 'modeGCountedAchievementReports',
                      'modeGAchievementSessionActive', 'lastPlayerHealth', 'achievementObservedHealth', 'achievementEventsSubscribed']:
            assert re.search(r'private[^;{}]*\b' + field + r'\b[^;{}]*;', triggers), 'trigger state must stay in module: ' + field
            assert field not in bridge, 'host must not own trigger state: ' + field
        for field in ['injectedMedalEntry', 'cachedMedalStock']:
            assert re.search(r'private static[^;{}]*\b' + field + r'\b[^;{}]*;', medal), 'medal static lifetime must stay unchanged: ' + field
        assert not re.search(r'\bowner\s*\.', triggers + runtime + medal), 'achievement policy must use bound queries rather than host business members'
        order(registration, [
            'runtimeModuleHost.Register(new SkyIslandRuntimeModule());',
            'achievementRuntime = new AchievementRuntimeModule();',
            'achievementRuntime.BindRuntimeQueries(() => IsActive, () => modeDRuntime.IsActive, () => wavesArenaRuntime.InfiniteHellMode, () => wavesArenaRuntime.BossesPerWave, () => config != null, () => config.achievementHotkey);',
            'achievementRuntime.BindMedalShopQueries(IsBaseHubNormalMerchantShop, BaseSceneName);',
            'runtimeModuleHost.Register(achievementRuntime);',
            'runtimeModuleHost.Register(new CommonNpcRuntimeModule());'],
            'same achievement instance must bind original live queries before registration at its original slot')
        assert registration.count('new AchievementRuntimeModule()') == 1, 'achievement runtime must be constructed once'
        assert 'InitializeAchievementRuntime' not in method(runtime, 'public override void OnAwake('), 'OnAwake must not initialize achievement early'
        assert compact(method(runtime, 'public override void OnUpdate(')) == compact('if (owner == null) return; TickAchievementRuntime(deltaTime, unscaledDeltaTime);'), 'module update must execute its own tick once'
        order(method(runtime, 'private void InitializeAchievementRuntimeCore()'), [
            'InitializeAchievementSystem();', 'AchievementView.EnsureInstance();', 'SteamAchievementPopup.EnsureInstance();',
            'BossRushEventBus.Subscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);',
            'Health.OnHurt += OnPlayerHurtForAchievement;'], 'achievement initialization order changed')
        assert (runtime + triggers).count('SteamAchievementPopup.EnsureInstance();') == 2, 'both original popup initialization requests must remain'
        order(method(runtime, 'private void CleanupAchievementRuntimeCore()'), [
            'Health.OnHurt -= OnPlayerHurtForAchievement;',
            'BossRushEventBus.Unsubscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);',
            'ResetAchievementBossKillTracking();', 'UnsubscribeAchievementEvents();',
            'SafeRuntime.Run("AchievementView.Shutdown", AchievementView.Shutdown);',
            'SafeRuntime.Run("SteamAchievementPopup.Shutdown", SteamAchievementPopup.Shutdown);'], 'achievement cleanup order changed')
        order(method(runtime, 'internal void CleanupAchievementRuntime()'), [
            'if (achievementRuntimeCleaned) return;', 'achievementRuntimeCleaned = true;', 'CleanupAchievementRuntimeCore();'],
            'achievement cleanup must have a single idempotent owner')
        assert 'UnsubscribeMedalStockEvents' not in method(runtime, 'private void CleanupAchievementRuntimeCore()'), 'early achievement cleanup must not move medal unsubscription'
        order(method(host, 'void OnDestroy()'), ['CleanupAchievementRuntime();',
            'SafeRuntime.Run("BossRushAchievementManager.ResetStaticCaches", () => BossRushAchievementManager.ResetStaticCaches());',
            'SafeRuntime.Run("AchievementIconLoader.ResetStaticCaches", () => AchievementIconLoader.ResetStaticCaches());',
            'runtimeModuleHost.OnDestroy();'], 'actual achievement cleanup must precede manager reset and later registry cleanup')
        order(method(integration, 'void Start_Integration()'), ['bossRushIntegrationRuntime.SubscribeJournalStockEvents();',
            'achievementRuntime.SubscribeMedalStockEvents();', 'bossRushIntegrationRuntime.SubscribeBrickStoneStockEvents();'], 'medal subscribe slot changed')
        order(method(integration, 'void OnDestroy_Integration()'), ['bossRushIntegrationRuntime.UnsubscribeJournalStockEvents();',
            'achievementRuntime.UnsubscribeMedalStockEvents();', 'bossRushIntegrationRuntime.UnsubscribeBrickStoneStockEvents();'], 'medal unsubscribe slot changed')
        for name, gate, assignment, op in [('Subscribe', 'if (medalStockEventsSubscribed) return;', 'medalStockEventsSubscribed = true;', '+='),
                                          ('Unsubscribe', 'if (!medalStockEventsSubscribed) return;', 'medalStockEventsSubscribed = false;', '-=')]:
            order(method(runtime, 'internal void ' + name + 'MedalStockEvents()'), [gate, assignment,
                'SavesSystem.OnCollectSaveData ' + op + ' OnCollectSaveData_MedalStock;',
                'SavesSystem.OnSetFile ' + op + ' OnSetFile_MedalStock;'], 'medal events must preserve ordered named delegates with an owner gate')
        for signature, statement in [
            ('internal void InitializeAchievementRuntime()', 'achievementRuntime.InitializeAchievementRuntime();'),
            ('internal void CleanupAchievementRuntime()', 'achievementRuntime.CleanupAchievementRuntime();'),
            ('private bool CheckBossKillAchievementsOnce(', 'return achievementRuntime.CheckBossKillAchievementsOnce(bossMain, bossTypeOverride);'),
            ('internal void BeginModeGAchievementSession()', 'achievementRuntime.BeginModeGAchievementSession();'),
            ('internal void EndModeGAchievementSession()', 'achievementRuntime.EndModeGAchievementSession();'),
            ('internal void ReportModeGBossKillAchievement(', 'achievementRuntime.ReportModeGBossKillAchievement(token, bossType, wasFlawlessAtDeath);'),
            ('internal bool TryInjectAchievementMedalIntoShop(', 'return achievementRuntime.TryInjectAchievementMedalIntoShop(shop);')]:
            assert compact(method(bridge, signature)) == compact(statement), 'host compatibility entry must be a single forward: ' + signature
        assert 'SavesSystem.Save<int>(AchievementMedalConfig.STOCK_SAVE_KEY, stockToSave);' in medal, 'medal persistence must keep its original save key'
    except (AssertionError, ValueError, OSError) as error:
        print('AchievementRuntimeOwnershipGuard: FAIL - ' + str(error))
        return 1
    print('AchievementRuntimeOwnershipGuard: PASS')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
