using Cysharp.Threading.Tasks;
using Duckov.Economy.UI;
using Duckov.Economy;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using ItemStatsSystem;
using SodaCraft.StringUtilities;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private bool modeEActive { get { return modeERuntime.modeEActive; } set { modeERuntime.modeEActive = value; } }

        private int modeESessionToken { get { return modeERuntime.modeESessionToken; } set { modeERuntime.modeESessionToken = value; } }

        private List<CharacterMainControl> modeEAliveEnemies { get { return modeERuntime.modeEAliveEnemies; } }

        private HashSet<CharacterMainControl> modeEAliveEnemySet { get { return modeERuntime.modeEAliveEnemySet; } }

        private static Teams[] ModeEAvailableFactions { get { return ModeERuntimeModule.ModeEAvailableFactions; } }

        public bool IsModeEActive { get { return modeERuntime.IsModeEActive; } }

        public Teams ModeEPlayerFaction { get { return modeERuntime.ModeEPlayerFaction; } }

        internal int CurrentModeESessionToken { get { return modeERuntime.CurrentModeESessionToken; } }

        public List<CharacterMainControl> ModeEAliveEnemies { get { return modeERuntime.ModeEAliveEnemies; } }

        private bool modeEDragonDescendantSpawned { get { return modeERuntime.modeEDragonDescendantSpawned; } set { modeERuntime.modeEDragonDescendantSpawned = value; } }

        private bool modeEDragonKingSpawned { get { return modeERuntime.modeEDragonKingSpawned; } set { modeERuntime.modeEDragonKingSpawned = value; } }

        private void EnsureModeEFSpawnPoolsReady(string sourceTag)
        { modeERuntime.EnsureModeEFSpawnPoolsReady(sourceTag); }

        public UniTaskVoid ModeESpawnAllBosses(
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1)
        { return modeERuntime.ModeESpawnAllBosses(modeFSessionToken, modeFRelatedScene, modeESessionToken, modeESessionRelatedScene); }

        private void SyncModeEDragonDescendantSpawnFlag(bool reservedDragonDescendantSlot, EnemyPresetInfo actualPreset, string modeTag)
        { modeERuntime.SyncModeEDragonDescendantSpawnFlag(reservedDragonDescendantSlot, actualPreset, modeTag); }

        private void SetModeEMerchantHealth(CharacterMainControl character)
        { modeERuntime.SetModeEMerchantHealth(character); }

        private void RegisterModeEEnemyDeath(CharacterMainControl enemy)
        { modeERuntime.RegisterModeEEnemyDeath(enemy); }

        private void UnregisterModeEEnemyDeath(CharacterMainControl enemy)
        { modeERuntime.UnregisterModeEEnemyDeath(enemy); }

        private void UnregisterModeEEnemyLootHandler(CharacterMainControl enemy)
        { modeERuntime.UnregisterModeEEnemyLootHandler(enemy); }

        private void RemoveModeEScalingModifiers(CharacterMainControl enemy)
        { modeERuntime.RemoveModeEScalingModifiers(enemy); }

        public void ModeEScalingBatchUpdate()
        { modeERuntime.ModeEScalingBatchUpdate(); }

        private void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl character)
        { modeERuntime.UnregisterModeEEnemyFromSpawnerRoot(character); }

        private void RegisterModeEEnemyToSpawnerRoot(CharacterMainControl character)
        { modeERuntime.RegisterModeEEnemyToSpawnerRoot(character); }

        private List<MonoBehaviour> GetModeEBossRegenCache()
        { return modeERuntime.GetModeEBossRegenCache(); }

        private void ModeEGiveColdWeatherGear()
        { modeERuntime.ModeEGiveColdWeatherGear(); }

        public string GetModeEFactionSuffix(Teams faction)
        { return modeERuntime.GetModeEFactionSuffix(faction); }

        internal void ApplyModeEHealthBarNameOverride(HealthBar healthBar, TextMeshProUGUI nameText)
        { modeERuntime.ApplyModeEHealthBarNameOverride(healthBar, nameText); }

        private void TrackModeEAliveEnemy(CharacterMainControl enemy, Teams faction)
        { modeERuntime.TrackModeEAliveEnemy(enemy, faction); }

        private void UntrackModeEAliveEnemy(CharacterMainControl enemy, Teams? faction = null)
        { modeERuntime.UntrackModeEAliveEnemy(enemy, faction); }

        internal bool DebugSettleModeEExtractionForValidation(out string metrics, out string reason)
        { return modeERuntime.DebugSettleModeEExtractionForValidation(out metrics, out reason); }

        public void EndModeE(bool showEndMessage = true)
        { modeERuntime.EndModeE(showEndMessage); }

        internal bool TryGetModeELotteryUiState(StockShop shop, out int price)
        { return modeERuntime.TryGetModeELotteryUiState(shop, out price); }

        internal UniTask<bool> BuyModeELotteryAsync(
            StockShop shop,
            long uiBindingID)
        { return modeERuntime.BuyModeELotteryAsync(shop, uiBindingID); }

        internal bool CanInteractWithModeEBossHireOffer(CharacterMainControl character)
        { return modeERuntime.CanInteractWithModeEBossHireOffer(character); }

        internal string DescribeModeEBossHireFailure(CharacterMainControl character)
        { return modeERuntime.DescribeModeEBossHireFailure(character); }

        internal bool TryHireModeEBoss(CharacterMainControl character)
        { return modeERuntime.TryHireModeEBoss(character); }

        internal void AttributeModeEHiredBossKillToOwner(ref DamageInfo damageInfo)
        { modeERuntime.AttributeModeEHiredBossKillToOwner(ref damageInfo); }

        internal bool ShouldBlockModeEHiredBossTeamChange(
            CharacterMainControl character,
            Teams requestedTeam)
        { return modeERuntime.ShouldBlockModeEHiredBossTeamChange(character, requestedTeam); }

        public InteractableBase ModeEMerchantMainInteract { get { return modeERuntime.ModeEMerchantMainInteract; } }

        private UniTaskVoid SpawnModeEMerchant(
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1)
        { return modeERuntime.SpawnModeEMerchant(modeFSessionToken, modeFRelatedScene, modeESessionToken, modeESessionRelatedScene); }

        private CharacterRandomPreset GetModeEMerchantPreset()
        { return modeERuntime.GetModeEMerchantPreset(); }

        private List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        { return modeERuntime.GetModeEMerchantCategories(tagsData); }

        internal void PrewarmModeEMerchantCaches()
        { modeERuntime.PrewarmModeEMerchantCaches(); }

        private List<int> ModeESearchItemsMultiTag(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        { return modeERuntime.ModeESearchItemsMultiTag(tags, excludeTags); }

        internal int[] GetModeEMerchantCategoryPoolIds(string suffix)
        { return modeERuntime.GetModeEMerchantCategoryPoolIds(suffix); }

        private void CleanupModeEMerchant()
        { modeERuntime.CleanupModeEMerchant(); }

        internal static void ResetModeEMerchantStaticCaches()
        { ModeERuntimeModule.ResetModeEMerchantStaticCaches(); }

        internal bool VerifyModeEShellPatchInstallation()
        { return modeERuntime.VerifyModeEShellPatchInstallation(); }

        internal bool IsCurrentModeEShellCapability(StockShop shop)
        { return modeERuntime.IsCurrentModeEShellCapability(shop); }

        internal ModeEShellShopPatchDisposition GetModeEShellShopPatchDisposition(StockShop shop)
        { return modeERuntime.GetModeEShellShopPatchDisposition(shop); }

        internal int CurrentModeEShellBalance { get { return modeERuntime.CurrentModeEShellBalance; } }

        internal Sprite CurrentModeEShellIcon { get { return modeERuntime.CurrentModeEShellIcon; } }

        internal bool IsModeEShellTransactionGateBusy { get { return modeERuntime.IsModeEShellTransactionGateBusy; } }

        internal void SubscribeModeEShellUiEvents(
            Action<ModeEShellBalanceChangedEvent> balance,
            Action<ModeEShellPriceCacheChangedEvent> price,
            Action<ModeEShellTransactionGateChangedEvent> gate)
        { modeERuntime.SubscribeModeEShellUiEvents(balance, price, gate); }

        internal void UnsubscribeModeEShellUiEvents(
            Action<ModeEShellBalanceChangedEvent> balance,
            Action<ModeEShellPriceCacheChangedEvent> price,
            Action<ModeEShellTransactionGateChangedEvent> gate)
        { modeERuntime.UnsubscribeModeEShellUiEvents(balance, price, gate); }

        internal bool TryAttachModeEShellUi(StockShop shop, out long uiBindingID)
        { return modeERuntime.TryAttachModeEShellUi(shop, out uiBindingID); }

        internal void DetachModeEShellUi(StockShop shop, long uiBindingID, string reason)
        { modeERuntime.DetachModeEShellUi(shop, uiBindingID, reason); }

        internal bool IsCurrentModeEShellUiBinding(StockShop shop, long uiBindingID)
        { return modeERuntime.IsCurrentModeEShellUiBinding(shop, uiBindingID); }

        internal void InvalidateAndResetModeEShellSession(string reason)
        { modeERuntime.InvalidateAndResetModeEShellSession(reason); }

        internal void DestroyModeEShellRuntimeState()
        { modeERuntime.DestroyModeEShellRuntimeState(); }

        internal bool AreAllModeEShopOfficialSamplesReady(StockShop shop)
        { return modeERuntime.AreAllModeEShopOfficialSamplesReady(shop); }

        internal void ShowModeEShopLoadingFeedback()
        { modeERuntime.ShowModeEShopLoadingFeedback(); }

        internal static bool IsModeEBulletStackShop(StockShop shop)
        { return ModeERuntimeModule.IsModeEBulletStackShop(shop); }

        internal void NormalizeModeEShellStackForShop(StockShop shop, Item item)
        { modeERuntime.NormalizeModeEShellStackForShop(shop, item); }

        internal bool TryGetModeEShellPrice(StockShop shop, int itemTypeID, out int price)
        { return modeERuntime.TryGetModeEShellPrice(shop, itemTypeID, out price); }

        internal void EnsureModeEShellPriceScheduled(StockShop shop, int itemTypeID)
        { modeERuntime.EnsureModeEShellPriceScheduled(shop, itemTypeID); }

        internal bool ShouldBypassModeEShellSellPatch(StockShop shop)
        { return modeERuntime.ShouldBypassModeEShellSellPatch(shop); }

        internal UniTask WrapModeEShellSellAsync(StockShop shop, Item item)
        { return modeERuntime.WrapModeEShellSellAsync(shop, item); }

        internal bool TryBeginModeEShellSellAll(StockShop shop, out long transactionID)
        { return modeERuntime.TryBeginModeEShellSellAll(shop, out transactionID); }

        internal UniTask SellModeEItemWithinSellAllAsync(
            StockShop shop,
            Item item,
            long transactionID)
        { return modeERuntime.SellModeEItemWithinSellAllAsync(shop, item, transactionID); }

        internal void EndModeEShellSellAll(StockShop shop, long transactionID)
        { modeERuntime.EndModeEShellSellAll(shop, transactionID); }

        internal UniTask<bool> BuyModeEShellItemAsync(
            StockShop shop,
            int itemTypeID,
            int amount)
        { return modeERuntime.BuyModeEShellItemAsync(shop, itemTypeID, amount); }

        internal void ApplyModeEShellItemEntryUi(
            StockShopItemEntry itemEntry,
            StockShopView master,
            StockShop.Entry entry)
        { modeERuntime.ApplyModeEShellItemEntryUi(itemEntry, master, entry); }

        internal void ApplyModeEShellInteractionButtonUi(StockShopView view)
        { modeERuntime.ApplyModeEShellInteractionButtonUi(view); }

        internal void RefreshOpenModeEShellUi(
            StockShop shop,
            long uiBindingID,
            int? itemTypeID)
        { modeERuntime.RefreshOpenModeEShellUi(shop, uiBindingID, itemTypeID); }

        internal bool IsCurrentModeEShellBalanceEvent(
            ModeEShellBalanceChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        { return modeERuntime.IsCurrentModeEShellBalanceEvent(evt, shop, uiBindingID); }

        internal bool IsCurrentModeEShellPriceEvent(
            ModeEShellPriceCacheChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        { return modeERuntime.IsCurrentModeEShellPriceEvent(evt, shop, uiBindingID); }

        internal bool IsCurrentModeEShellGateEvent(
            ModeEShellTransactionGateChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        { return modeERuntime.IsCurrentModeEShellGateEvent(evt, shop, uiBindingID); }

        private Dictionary<CharacterMainControl, float> modeEPendingAggroTraceDistance { get { return modeERuntime.modeEPendingAggroTraceDistance; } }

        internal bool CanQueryUseModeERespawnItem()
        { return modeERuntime.CanQueryUseModeERespawnItem(); }

        internal bool CanUseModeERespawnItem(bool showFailureFeedback)
        { return modeERuntime.CanUseModeERespawnItem(showFailureFeedback); }

        public void UseTauntSmoke()
        { modeERuntime.UseTauntSmoke(); }

        public void UseChaosDetonator()
        { modeERuntime.UseChaosDetonator(); }

        private bool TryForceActivateModeEEnemy(CharacterMainControl enemy, out bool wokeInactiveEnemy)
        { return modeERuntime.TryForceActivateModeEEnemy(enemy, out wokeInactiveEnemy); }

        public void UseBosscallWhistle()
        { modeERuntime.UseBosscallWhistle(); }

        public void UseAllKingsBanner()
        { modeERuntime.UseAllKingsBanner(); }

        internal void TickModeERuntime(float deltaTime)
        { modeERuntime.TickModeERuntime(deltaTime); }

        private Dictionary<Teams, List<Vector3>> modeESpawnAllocation { get { return modeERuntime.modeESpawnAllocation; } set { modeERuntime.modeESpawnAllocation = value; } }

        private Vector3[] modeECachedSpawnerPositions { get { return modeERuntime.modeECachedSpawnerPositions; } set { modeERuntime.modeECachedSpawnerPositions = value; } }

        private string modeECachedSpawnerSceneName { get { return modeERuntime.modeECachedSpawnerSceneName; } set { modeERuntime.modeECachedSpawnerSceneName = value; } }

        private Vector3[] GetModeEFlattenedSpawnPoints()
        { return modeERuntime.GetModeEFlattenedSpawnPoints(); }

        private void AllocateSpawnPoints()
        { modeERuntime.AllocateSpawnPoints(); }

        private void TeleportPlayerToSafePosition()
        { modeERuntime.TeleportPlayerToSafePosition(); }

        public void PreCacheMapSpawnerPositions()
        { modeERuntime.PreCacheMapSpawnerPositions(); }

        private void ResetModeESharedRuntimeState(bool clearSpawnAllocation, bool clearSpawnerCache, bool stopWarmupCoroutine)
        { modeERuntime.ResetModeESharedRuntimeState(clearSpawnAllocation, clearSpawnerCache, stopWarmupCoroutine); }

        internal bool IsModeESessionStillValid(int sessionToken, int relatedScene)
        { return modeERuntime.IsModeESessionStillValid(sessionToken, relatedScene); }

        internal bool IsModeEOrModeFSpawnSessionStillValid(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        { return modeERuntime.IsModeEOrModeFSpawnSessionStillValid(modeFSessionToken, modeFRelatedScene, modeESessionToken, modeESessionRelatedScene); }

        private void ScheduleModeEStartupWarmup(string reason)
        { modeERuntime.ScheduleModeEStartupWarmup(reason); }

        private void StopModeEStartupWarmupIfPending()
        { modeERuntime.StopModeEStartupWarmupIfPending(); }

        public (Teams? faction, Item flagItem) DetectFactionFlag()
        { return modeERuntime.DetectFactionFlag(); }

        private bool TryConsumeModeEntryItem(Item item, string modeTag, string itemLabel)
        { return modeERuntime.TryConsumeModeEntryItem(item, modeTag, itemLabel); }

        private System.Collections.IEnumerator WaitForModeEStartupVerification(Action<bool> onCompleted)
        { return modeERuntime.WaitForModeEStartupVerification(onCompleted); }

        public bool TryStartModeE()
        { return modeERuntime.TryStartModeE(); }

        private bool StartModeE(Teams faction)
        { return modeERuntime.StartModeE(faction); }

        internal string GetModeEPlayerName()
        { return modeERuntime.GetModeEPlayerName(); }

        internal string GetModeEActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        { return modeERuntime.GetModeEActorDisplayName(actor, treatNullAsPlayer); }

        internal void RegisterModeEHealthBar(HealthBar healthBar)
        { modeERuntime.RegisterModeEHealthBar(healthBar); }
    }
}
