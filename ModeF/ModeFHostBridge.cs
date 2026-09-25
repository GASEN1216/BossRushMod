using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.ItemUsage;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using ItemStatsSystem;
using System.Collections.Generic;
using System.Reflection;
using System;
using TMPro;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private bool modeFActive { get { return modeFRuntime.modeFActive; } set { modeFRuntime.modeFActive = value; } }
        private ModeFState modeFState { get { return modeFRuntime.modeFState; } }
        public bool IsModeFActive { get { return modeFRuntime.IsModeFActive; } }
        public bool IsModeFPreparationPhase { get { return modeFRuntime.IsModeFPreparationPhase; } }

        private List<BossRushStatModifierRecord> modeFBloodfireModifiers { get { return modeFRuntime.modeFBloodfireModifiers; } }

        internal bool DebugValidateModeFBloodfire(out string metrics)
        { return modeFRuntime.DebugValidateModeFBloodfire(out metrics); }

        private bool HasModeFPlayerBountyKillLatch(int victimId)
        { return modeFRuntime.HasModeFPlayerBountyKillLatch(victimId); }

        private bool ConsumeModeFPlayerBountyKillLatch(int victimId)
        { return modeFRuntime.ConsumeModeFPlayerBountyKillLatch(victimId); }

        private void GenerateBountyList()
        { modeFRuntime.GenerateBountyList(); }

        private void OnModeFBossKilledByPlayer(CharacterMainControl victim)
        { modeFRuntime.OnModeFBossKilledByPlayer(victim); }

        internal int ModeFPlayerBountyMarksForValidation { get { return modeFRuntime.ModeFPlayerBountyMarksForValidation; } }

        internal bool DebugAwardModeFBountyMarkForValidation(out string reason)
        { return modeFRuntime.DebugAwardModeFBountyMarkForValidation(out reason); }

        private bool TryHandleModeFBossPreLootPlunder(CharacterMainControl killer, CharacterMainControl victim)
        { return modeFRuntime.TryHandleModeFBossPreLootPlunder(killer, victim); }

        private int ConsumeModeFBossCarriedHighQualityLootCount(CharacterMainControl boss)
        { return modeFRuntime.ConsumeModeFBossCarriedHighQualityLootCount(boss); }

        private int ConsumeModeFBossPendingHighQualityLootPenaltyCount(CharacterMainControl boss)
        { return modeFRuntime.ConsumeModeFBossPendingHighQualityLootPenaltyCount(boss); }

        internal int CurrentModeFSessionToken { get { return modeFRuntime.CurrentModeFSessionToken; } }

        internal bool IsModeFSessionStillValid(int sessionToken, int relatedScene)
        { return modeFRuntime.IsModeFSessionStillValid(sessionToken, relatedScene); }

        private Item DetectBloodhuntTransponder()
        { return modeFRuntime.DetectBloodhuntTransponder(); }

        private Item DetectBossRushTicketItem()
        { return modeFRuntime.DetectBossRushTicketItem(); }

        private bool IsPlayerNakedForModeF()
        { return modeFRuntime.IsPlayerNakedForModeF(); }

        public bool TryStartModeF()
        { return modeFRuntime.TryStartModeF(); }

        private bool StartModeF()
        { return modeFRuntime.StartModeF(); }

        private void SpawnFinalExtractionPoint()
        { modeFRuntime.SpawnFinalExtractionPoint(); }

        internal bool ModeFExtractionPointSpawnedForValidation { get { return modeFRuntime.ModeFExtractionPointSpawnedForValidation; } }

        internal int ModeFSuccessfulExtractionCountForValidation { get { return modeFRuntime.ModeFSuccessfulExtractionCountForValidation; } }

        internal bool DebugTriggerModeFExtractionForValidation(out string reason)
        { return modeFRuntime.DebugTriggerModeFExtractionForValidation(out reason); }

        private void OnModeFExtractionSuccess()
        { modeFRuntime.OnModeFExtractionSuccess(); }

        public bool UseModeFFortificationItem(FortificationType type)
        { return modeFRuntime.UseModeFFortificationItem(type); }

        public bool UseModeFRepairSpray()
        { return modeFRuntime.UseModeFRepairSpray(); }

        internal void UpdateFortPlacementMode()
        { modeFRuntime.UpdateFortPlacementMode(); }

        internal void UpdateModeFRepairSelection()
        { modeFRuntime.UpdateModeFRepairSelection(); }

        internal void CancelFortPlacement()
        { modeFRuntime.CancelFortPlacement(); }

        internal void CleanupZombieModeFortificationInteractionState()
        { modeFRuntime.CleanupZombieModeFortificationInteractionState(); }

        internal bool CanUseModeFRepairSpray(bool highlightNearest = true)
        { return modeFRuntime.CanUseModeFRepairSpray(highlightNearest); }

        private void UpdateModeFFortificationHighlights()
        { modeFRuntime.UpdateModeFFortificationHighlights(); }

        internal bool TryGiveItemToPlayerOrDrop(int typeId, string displayName, bool showRewardBubble = true, bool allowWorldDrop = true)
        { return modeFRuntime.TryGiveItemToPlayerOrDrop(typeId, displayName, showRewardBubble, allowWorldDrop); }

        internal void RefundModeFUtilityItem(int typeId, string reason)
        { modeFRuntime.RefundModeFUtilityItem(typeId, reason); }

        private void EnsureModeFFortificationCharacterBlocker(GameObject fortObj)
        { modeFRuntime.EnsureModeFFortificationCharacterBlocker(fortObj); }

        internal static int TryInjectModeFItemsIntoMerchantShop(StockShop shop)
        { return ModeFRuntimeModule.TryInjectModeFItemsIntoMerchantShop(shop); }

        private void TickModeF(float deltaTime)
        { modeFRuntime.TickModeF(deltaTime); }

        internal ModeFPhase ModeFCurrentPhaseForValidation { get { return modeFRuntime.ModeFCurrentPhaseForValidation; } }

        internal bool DebugAdvanceModeFPhaseForValidation(out string reason)
        { return modeFRuntime.DebugAdvanceModeFPhaseForValidation(out reason); }

        private void ExitModeF(bool showEndMessage = true)
        { modeFRuntime.ExitModeF(showEndMessage); }

        internal static string GetModeFPhaseName(ModeFPhase phase)
        { return ModeFRuntimeModule.GetModeFPhaseName(phase); }

        private void ApplyModeFPressureToBoss(CharacterMainControl boss)
        { modeFRuntime.ApplyModeFPressureToBoss(boss); }

        private HashSet<CharacterMainControl> modeFActiveBossSet { get { return modeFRuntime.modeFActiveBossSet; } }

        internal Teams ResolveModeFBossCombatTeam(Teams requestedFaction, EnemyPresetInfo preset, Vector3 spawnPos)
        { return modeFRuntime.ResolveModeFBossCombatTeam(requestedFaction, preset, spawnPos); }

        private List<MonoBehaviour> GetModeFBossRegenCache()
        { return modeFRuntime.GetModeFBossRegenCache(); }

        internal void RegisterModeFBoss(CharacterMainControl boss)
        { modeFRuntime.RegisterModeFBoss(boss); }

        internal void TickModeFRuntime(float deltaTime)
        { modeFRuntime.TickModeFRuntime(deltaTime); }

        internal void CleanupModeFForSceneChange()
        { modeFRuntime.CleanupModeFForSceneChange(); }

        private void ResetModeFUiCaches()
        { modeFRuntime.ResetModeFUiCaches(); }

        internal void MarkModeFHealthBarNamesDirty()
        { modeFRuntime.MarkModeFHealthBarNamesDirty(); }

        internal void RegisterModeFHealthBar(HealthBar healthBar)
        { modeFRuntime.RegisterModeFHealthBar(healthBar); }

        private void ScanAndCacheModeFHealthBars(bool force = false)
        { modeFRuntime.ScanAndCacheModeFHealthBars(force); }

        internal string GetModeFPlayerName()
        { return modeFRuntime.GetModeFPlayerName(); }

        internal void SetModeFBossDisplayName(CharacterMainControl actor, string displayName, Teams originalFaction, string nameKey = null)
        { modeFRuntime.SetModeFBossDisplayName(actor, displayName, originalFaction, nameKey); }

        internal string GetModeFActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        { return modeFRuntime.GetModeFActorDisplayName(actor, treatNullAsPlayer); }

        public string GetModeFBountyMarkSuffix(CharacterMainControl character)
        { return modeFRuntime.GetModeFBountyMarkSuffix(character); }

        public string GetModeFPlayerMarkSuffix()
        { return modeFRuntime.GetModeFPlayerMarkSuffix(); }

        internal bool ShouldForceModeFHealthBarName(CharacterMainControl character)
        { return modeFRuntime.ShouldForceModeFHealthBarName(character); }

        internal void EnsureModeFBossNameTag(CharacterMainControl boss)
        { modeFRuntime.EnsureModeFBossNameTag(boss); }

        internal bool ApplyModeFHealthBarNameOverride(HealthBar healthBar, TextMeshProUGUI nameText = null)
        { return modeFRuntime.ApplyModeFHealthBarNameOverride(healthBar, nameText); }

        public void RefreshModeFActorNameText(CharacterMainControl actor)
        { modeFRuntime.RefreshModeFActorNameText(actor); }

        private HealthBar FindModeFHealthBar(Health health)
        { return modeFRuntime.FindModeFHealthBar(health); }

        private HealthBar FindModeFPlayerHealthBar(Health health)
        { return modeFRuntime.FindModeFPlayerHealthBar(health); }
    }
}
