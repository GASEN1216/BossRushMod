using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal async UniTask<ManagedBossPrepareResult> PrepareManagedDragonKingAsync(
            Vector3 position, ManagedBossSpawnContext ctx)
        {
            CharacterMainControl character = null;
            DragonKingAbilityController controller = null;
            try
            {
                character = await CreateModeGManagedCharacterAsync(
                    FindDragonKingBasePreset(), position, ctx, DragonKingConfig.BossNameKey,
                    "DragonKing_Preset");
                if (character == null) return null;

                character.gameObject.name = "BossRush_ModeG_DragonKing";
                SetupDragonKingAttributes(character);
                ApplyBossStatMultiplier(character);
                await EquipDragonKing(character);
                if (!IsManagedOwnerValid(ctx))
                {
                    CleanupModeGManagedDragonKing(character, null, false);
                    return null;
                }

                DisableDragonKingOriginalAI(character);
                controller = character.gameObject.AddComponent<DragonKingAbilityController>();
                // Mode G 消费点（规格 §20 第 18 条）：传播 PreserveLinkedKillAttribution 至控制器，
                // 孩儿护我联动死亡的击杀来源归因给玩家；Legacy 路径开关默认 false 不受影响。
                controller.ModeGPreserveLinkedKillAttribution = ctx.PreserveLinkedKillAttribution;
                CharacterMainControl capturedCharacter = character;
                DragonKingAbilityController capturedController = controller;
                bool activated = false;
                bool setBonusRegistered = false;
                bool assetReferenceAdded = false;

                ManagedBossRuntimeHandle handle = new ManagedBossRuntimeHandle
                {
                    Character = character,
                    AchievementBossType = "DragonKing",
                    Activate = () =>
                    {
                        if (activated) return true;
                        if (!IsManagedOwnerValid(ctx) || capturedCharacter == null
                            || capturedCharacter.Health == null || capturedCharacter.Health.IsDead) return false;

                        BeginActivateModeGManagedCharacter(capturedCharacter);
                        int referenceCountBefore = DragonKingAssetManager.ActiveReferenceCount;
                        try
                        {
                            capturedController.Initialize(capturedCharacter);
                        }
                        finally
                        {
                            // Initialize may throw after LoadAssets has acquired this instance's bundle reference.
                            assetReferenceAdded = DragonKingAssetManager.ActiveReferenceCount > referenceCountBefore;
                        }
                        dragonKingInstances[capturedCharacter] = capturedController;
                        RegisterDragonKingSetBonus(capturedCharacter);
                        setBonusRegistered = true;
                        CompleteActivateModeGManagedCharacter(capturedCharacter);
                        BossRushAudioManager.Instance.PlayDragonKingBGM();
                        activated = true;
                        return true;
                    },
                    CleanupAfterDeath = info =>
                    {
                        if (capturedController != null) capturedController.OnBossDeath();
                        if (setBonusRegistered)
                        {
                            UnregisterDragonKingSetBonus(capturedCharacter);
                            setBonusRegistered = false;
                        }
                    },
                    Cleanup = reason =>
                    {
                        if (capturedController != null && reason != ManagedBossCleanupReason.Death)
                            capturedController.OnBossDeath();
                        if (setBonusRegistered)
                        {
                            UnregisterDragonKingSetBonus(capturedCharacter);
                            setBonusRegistered = false;
                        }
                        dragonKingInstances.Remove(capturedCharacter);
                        dragonKingDeathEventHandlers.Remove(capturedCharacter);
                        dragonKingLootEventHandlers.Remove(capturedCharacter);
                        CleanupModeGManagedDragonKing(capturedCharacter, capturedController, assetReferenceAdded);
                    }
                };
                return new ManagedBossPrepareResult { Character = character, Handle = handle };
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] PrepareManagedDragonKingAsync 异常: " + e.Message);
                CleanupModeGManagedDragonKing(character, controller, false);
                return null;
            }
        }

        private void CleanupModeGManagedDragonKing(CharacterMainControl character,
            DragonKingAbilityController controller, bool assetReferenceAdded)
        {
            if (character == null) return;
            try
            {
                dragonKingInstances.Remove(character);
                dragonKingDeathEventHandlers.Remove(character);
                dragonKingLootEventHandlers.Remove(character);
                CleanupModeGManagedCharacter(character, DragonKingConfig.BossNameKey,
                    "DragonKing_Preset", "[DragonKing]");
                if (assetReferenceAdded) ReleaseDragonKingInstance();
                BossRushAudioManager.Instance?.ResetDragonKingBGMState();
            }
            catch { }
        }
    }
}
