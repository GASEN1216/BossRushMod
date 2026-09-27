using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    // 窄回调保留原宿主，直到迟到生成和 handle 回收结束；关停单局不清空绑定。
    // 入口服务不注册为模块；每次开局仍创建独立核心，Dispose 后不复用。
    internal sealed partial class ModeGEntryRuntime
    {
        internal delegate UniTask<EnemySpawnCoreResult> ModeGSpawnCore(
            EnemyPresetInfo preset, Vector3 position, bool isBoss, Func<bool> isActiveCheck,
            int waveIndex = 1, bool skipDragonDescendant = false, bool skipDragonKing = false,
            bool applyEquipment = true, bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null, bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true, bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null, EnemySpawnCoreOptions options = null);

        internal bool IsRunActiveForHost { get { return modeGActive; } }
        internal ModeGRuntimeModule CurrentRunForHost { get { return modeGRuntime; } }
        private bool IsActive { get { return getLegacyActive(); } }
        private bool modeDActive { get { return getModeDActive(); } }
        private bool modeEActive { get { return getModeEActive(); } }
        private bool modeFActive { get { return getModeFActive(); } }
        private bool IsZombieModeActive { get { return getZombieActive(); } }
        private Dictionary<string, CharacterRandomPreset> cachedCharacterPresets { get { return getCharacterPresets(); } }
        private bool bossRushArenaActive { set { setArenaActive(value); } }
        private bool bossRushArenaPlanned { set { setArenaPlanned(value); } }
        private bool spawnersDisabled { get { return getSpawnersDisabled(); } set { setSpawnersDisabled(value); } }

        private Func<bool> getLegacyActive;
        private Func<bool> getModeDActive;
        private Func<bool> getModeEActive;
        private Func<bool> getModeFActive;
        private Func<bool> getZombieActive;
        private Func<int> getAbandonHotkey;

        internal void BindEntryQueries(
            Func<bool> getLegacyActiveCallback,
            Func<bool> getModeDActiveCallback,
            Func<bool> getModeEActiveCallback,
            Func<bool> getModeFActiveCallback,
            Func<bool> getZombieActiveCallback,
            Func<int> getAbandonHotkeyCallback)
        {
            getLegacyActive = getLegacyActiveCallback;
            getModeDActive = getModeDActiveCallback;
            getModeEActive = getModeEActiveCallback;
            getModeFActive = getModeFActiveCallback;
            getZombieActive = getZombieActiveCallback;
            getAbandonHotkey = getAbandonHotkeyCallback;
        }

        private Func<Item> DetectBossRushTicketItem;
        private Func<(Teams? faction, Item flagItem)> DetectFactionFlag;
        private Func<Item> DetectBloodhuntTransponder;
        private Func<Item, string, string, bool> TryConsumeModeEntryItem;
        private Func<int> GetBossRushTicketTypeId;
        private Action<string> ShowMessage;
        private Action<string> ShowBigBanner;

        internal void BindEntryServices(
            Func<Item> DetectBossRushTicketItemCallback,
            Func<(Teams? faction, Item flagItem)> DetectFactionFlagCallback,
            Func<Item> DetectBloodhuntTransponderCallback,
            Func<Item, string, string, bool> TryConsumeModeEntryItemCallback,
            Func<int> GetBossRushTicketTypeIdCallback,
            Action<string> ShowMessageCallback,
            Action<string> ShowBigBannerCallback)
        {
            DetectBossRushTicketItem = DetectBossRushTicketItemCallback;
            DetectFactionFlag = DetectFactionFlagCallback;
            DetectBloodhuntTransponder = DetectBloodhuntTransponderCallback;
            TryConsumeModeEntryItem = TryConsumeModeEntryItemCallback;
            GetBossRushTicketTypeId = GetBossRushTicketTypeIdCallback;
            ShowMessage = ShowMessageCallback;
            ShowBigBanner = ShowBigBannerCallback;
        }

        private Action InitializeEnemyPresets;
        private Action InitializeBossPoolFilter;
        private Action EnsureCharacterPresetsCacheReady;
        private Func<List<EnemyPresetInfo>> GetFilteredEnemyPresets;
        private Func<Dictionary<string, CharacterRandomPreset>> getCharacterPresets;
        private Func<EnemyPresetInfo, bool> IsDragonDescendantPreset;
        private Func<EnemyPresetInfo, bool> IsDragonKingPreset;
        private Func<EnemyPresetInfo, bool> IsPhantomWitchPreset;
        private Func<EnemyPresetInfo, bool> IsManagedBossPreset;
        private Func<HashSet<int>> BuildGeneralBossLootCandidateIdSet;

        internal void BindBossPoolQueries(
            Action InitializeEnemyPresetsCallback,
            Action InitializeBossPoolFilterCallback,
            Action EnsureCharacterPresetsCacheReadyCallback,
            Func<List<EnemyPresetInfo>> GetFilteredEnemyPresetsCallback,
            Func<Dictionary<string, CharacterRandomPreset>> getCharacterPresetsCallback,
            Func<EnemyPresetInfo, bool> IsDragonDescendantPresetCallback,
            Func<EnemyPresetInfo, bool> IsDragonKingPresetCallback,
            Func<EnemyPresetInfo, bool> IsPhantomWitchPresetCallback,
            Func<EnemyPresetInfo, bool> IsManagedBossPresetCallback,
            Func<HashSet<int>> BuildGeneralBossLootCandidateIdSetCallback)
        {
            InitializeEnemyPresets = InitializeEnemyPresetsCallback;
            InitializeBossPoolFilter = InitializeBossPoolFilterCallback;
            EnsureCharacterPresetsCacheReady = EnsureCharacterPresetsCacheReadyCallback;
            GetFilteredEnemyPresets = GetFilteredEnemyPresetsCallback;
            getCharacterPresets = getCharacterPresetsCallback;
            IsDragonDescendantPreset = IsDragonDescendantPresetCallback;
            IsDragonKingPreset = IsDragonKingPresetCallback;
            IsPhantomWitchPreset = IsPhantomWitchPresetCallback;
            IsManagedBossPreset = IsManagedBossPresetCallback;
            BuildGeneralBossLootCandidateIdSet = BuildGeneralBossLootCandidateIdSetCallback;
        }

        private Func<CharacterRandomPreset> FindQuestionMarkPreset;
        private Func<CharacterRandomPreset> FindFallbackPreset;
        private Func<CharacterRandomPreset> FindDragonKingBasePreset;
        private Func<CharacterRandomPreset> FindPhantomWitchBasePreset;

        internal void BindSignatureQueries(
            Func<CharacterRandomPreset> FindQuestionMarkPresetCallback,
            Func<CharacterRandomPreset> FindFallbackPresetCallback,
            Func<CharacterRandomPreset> FindDragonKingBasePresetCallback,
            Func<CharacterRandomPreset> FindPhantomWitchBasePresetCallback)
        {
            FindQuestionMarkPreset = FindQuestionMarkPresetCallback;
            FindFallbackPreset = FindFallbackPresetCallback;
            FindDragonKingBasePreset = FindDragonKingBasePresetCallback;
            FindPhantomWitchBasePreset = FindPhantomWitchBasePresetCallback;
        }

        private ModeGSpawnCore SpawnEnemyCoreInternalAsync;
        private Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedDragonDescendantAsync;
        private Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedDragonKingAsync;
        private Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedPhantomWitchAsync;
        private Action<CharacterMainControl> ActivateModeGManagedCharacter;
        private Action<CharacterMainControl, string, string, string> CleanupModeGManagedCharacter;

        internal void BindSpawnServices(
            ModeGSpawnCore SpawnEnemyCoreInternalAsyncCallback,
            Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedDragonDescendantAsyncCallback,
            Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedDragonKingAsyncCallback,
            Func<Vector3, ManagedBossSpawnContext, UniTask<ManagedBossPrepareResult>> PrepareManagedPhantomWitchAsyncCallback,
            Action<CharacterMainControl> ActivateModeGManagedCharacterCallback,
            Action<CharacterMainControl, string, string, string> CleanupModeGManagedCharacterCallback)
        {
            SpawnEnemyCoreInternalAsync = SpawnEnemyCoreInternalAsyncCallback;
            PrepareManagedDragonDescendantAsync = PrepareManagedDragonDescendantAsyncCallback;
            PrepareManagedDragonKingAsync = PrepareManagedDragonKingAsyncCallback;
            PrepareManagedPhantomWitchAsync = PrepareManagedPhantomWitchAsyncCallback;
            ActivateModeGManagedCharacter = ActivateModeGManagedCharacterCallback;
            CleanupModeGManagedCharacter = CleanupModeGManagedCharacterCallback;
        }

        private Func<Vector3[]> GetCurrentSceneSpawnPoints;
        private Action<string> SetCurrentMapSpawnPoints;
        private Action InitializeItemValueCacheAsync;
        private Action TryCreateArenaDifficultyEntryPoint;
        private Action PreCacheMapSpawnerPositions;
        private Action DisableAllSpawners;
        private Action ClearEnemiesForBossRush;
        private Func<bool> getSpawnersDisabled;
        private Action<bool> setSpawnersDisabled;
        private Action<bool> setArenaActive;
        private Action<bool> setArenaPlanned;

        internal void BindArenaServices(
            Func<Vector3[]> GetCurrentSceneSpawnPointsCallback,
            Action<string> SetCurrentMapSpawnPointsCallback,
            Action InitializeItemValueCacheAsyncCallback,
            Action TryCreateArenaDifficultyEntryPointCallback,
            Action PreCacheMapSpawnerPositionsCallback,
            Action DisableAllSpawnersCallback,
            Action ClearEnemiesForBossRushCallback,
            Func<bool> getSpawnersDisabledCallback,
            Action<bool> setSpawnersDisabledCallback,
            Action<bool> setArenaActiveCallback,
            Action<bool> setArenaPlannedCallback)
        {
            GetCurrentSceneSpawnPoints = GetCurrentSceneSpawnPointsCallback;
            SetCurrentMapSpawnPoints = SetCurrentMapSpawnPointsCallback;
            InitializeItemValueCacheAsync = InitializeItemValueCacheAsyncCallback;
            TryCreateArenaDifficultyEntryPoint = TryCreateArenaDifficultyEntryPointCallback;
            PreCacheMapSpawnerPositions = PreCacheMapSpawnerPositionsCallback;
            DisableAllSpawners = DisableAllSpawnersCallback;
            ClearEnemiesForBossRush = ClearEnemiesForBossRushCallback;
            getSpawnersDisabled = getSpawnersDisabledCallback;
            setSpawnersDisabled = setSpawnersDisabledCallback;
            setArenaActive = setArenaActiveCallback;
            setArenaPlanned = setArenaPlannedCallback;
        }
    }
}
