using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal async UniTask<ManagedBossPrepareResult> PrepareManagedDragonDescendantAsync(
            Vector3 position, ManagedBossSpawnContext ctx)
        {
            CharacterMainControl character = null;
            DragonDescendantAbilityController controller = null;
            try
            {
                CharacterRandomPreset basePreset = FindQuestionMarkPreset() ?? FindFallbackPreset();
                character = await CreateModeGManagedCharacterAsync(
                    basePreset, position, ctx, DragonDescendantConfig.BOSS_NAME_KEY,
                    "DragonDescendant_Preset");
                if (character == null) return null;

                SetupBossAttributes(character);
                ApplyBossStatMultiplier(character);
                OriginalWeaponData originalWeaponData = GetWeaponDataFromEquippedWeapon(character);
                await EquipDragonDescendant(character);
                if (!IsManagedOwnerValid(ctx))
                {
                    CleanupModeGManagedCharacter(character, DragonDescendantConfig.BOSS_NAME_KEY,
                        "DragonDescendant_Preset", "[DragonDescendant]");
                    return null;
                }

                controller = character.gameObject.AddComponent<DragonDescendantAbilityController>();
                bool activated = false;
                bool setBonusRegistered = false;
                CharacterMainControl capturedCharacter = character;
                DragonDescendantAbilityController capturedController = controller;

                ManagedBossRuntimeHandle handle = new ManagedBossRuntimeHandle
                {
                    Character = character,
                    AchievementBossType = "DragonDescendant",
                    Activate = () =>
                    {
                        if (activated) return true;
                        if (!IsManagedOwnerValid(ctx) || capturedCharacter == null
                            || capturedCharacter.Health == null || capturedCharacter.Health.IsDead) return false;

                        BeginActivateModeGManagedCharacter(capturedCharacter);
                        dragonDescendantInstance = capturedCharacter;
                        dragonDescendantAbilities = capturedController;
                        capturedController.Initialize(capturedCharacter, originalWeaponData);
                        RegisterDragonDescendantSetBonus();
                        setBonusRegistered = true;
                        CompleteActivateModeGManagedCharacter(capturedCharacter);
                        activated = true;
                        return true;
                    },
                    CleanupAfterDeath = info =>
                    {
                        if (setBonusRegistered)
                        {
                            UnregisterDragonDescendantSetBonus();
                            setBonusRegistered = false;
                        }
                    },
                    Cleanup = reason =>
                    {
                        if (setBonusRegistered)
                        {
                            UnregisterDragonDescendantSetBonus();
                            setBonusRegistered = false;
                        }
                        if (dragonDescendantInstance == capturedCharacter) dragonDescendantInstance = null;
                        if (dragonDescendantAbilities == capturedController) dragonDescendantAbilities = null;
                        CleanupModeGManagedCharacter(capturedCharacter,
                            DragonDescendantConfig.BOSS_NAME_KEY, "DragonDescendant_Preset",
                            "[DragonDescendant]");
                    }
                };
                return new ManagedBossPrepareResult { Character = character, Handle = handle };
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] PrepareManagedDragonDescendantAsync 异常: " + e.Message);
                CleanupModeGManagedCharacter(character, DragonDescendantConfig.BOSS_NAME_KEY,
                    "DragonDescendant_Preset", "[DragonDescendant]");
                return null;
            }
        }

        #region Mode G Managed Adapter Shared Helpers

        internal static bool IsManagedOwnerValid(ManagedBossSpawnContext ctx)
        {
            if (ctx == null || ctx.IsOwnerValid == null) return true;
            try { return ctx.IsOwnerValid(); } catch { return false; }
        }

        private async UniTask<CharacterMainControl> CreateModeGManagedCharacterAsync(
            CharacterRandomPreset basePreset, Vector3 position, ManagedBossSpawnContext ctx,
            string runtimeNameKey, string runtimePresetName)
        {
            if (basePreset == null || ctx == null || !IsManagedOwnerValid(ctx)) return null;
            ModeGRunState state = ModeGRunContext.Current;
            if (state == null) return null;

            CharacterRandomPreset stagingPreset = null;
            CharacterMainControl character = null;
            bool stagingPresetRegistered = false;
            bool stagingBossRegistered = false;
            bool preparedSuccessfully = false;
            try
            {
                stagingPreset = UnityEngine.Object.Instantiate(basePreset);
                stagingPreset.name = "ModeG_Staging_" + runtimePresetName;
                stagingPreset.team = Teams.middle;
                stagingPreset.dropBoxOnDead = false;
                stagingPreset.setActiveByPlayerDistance = false;
                stagingPreset.canDieIfNotRaidMap = true;
                stagingPreset.exp = 0;
                ctx.FactoryPresetOverride = stagingPreset;
                stagingPresetRegistered = state.RegisterStagingPreset(stagingPreset);
                if (!stagingPresetRegistered) return null;

                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await stagingPreset.CreateCharacterAsync(
                    position, Vector3.forward, relatedScene, null, false);

                // Factory 返回后的首个同步段：先登记 exact 身份，再冻结对象。
                if (character == null || character.Health == null) return null;
                stagingBossRegistered = state.RegisterStagingBoss(character.Health, character);
                if (!stagingBossRegistered || !IsManagedOwnerValid(ctx) || character.Health.IsDead
                    || !character.Health.CanDieIfNotRaidMap || HasModeGPlayerAuthoredBuff(character))
                    return null;

                character.Health.SetInvincible(true);
                character.gameObject.SetActive(false);
                if (character.gameObject.activeSelf || !character.Health.Invincible) return null;

                CharacterRandomPreset runtimePreset = UnityEngine.Object.Instantiate(stagingPreset);
                runtimePreset.name = runtimePresetName;
                runtimePreset.nameKey = runtimeNameKey;
                runtimePreset.showName = true;
                runtimePreset.showHealthBar = true;
                runtimePreset.team = Teams.wolf;
                runtimePreset.dropBoxOnDead = false;
                runtimePreset.exp = basePreset.exp;
                character.characterPreset = runtimePreset;
                character.Health.showHealthBar = true;
                character.Health.CanDieIfNotRaidMap = true;
                if (character.CharacterItem != null) character.CharacterItem.SetInt("Exp", basePreset.exp, true);

                state.UnregisterStagingPreset(stagingPreset);
                stagingPresetRegistered = false;
                UnityEngine.Object.Destroy(stagingPreset);
                stagingPreset = null;
                preparedSuccessfully = true;
                return character;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] managed factory/configure 失败: " + e.Message);
                return null;
            }
            finally
            {
                if (stagingPresetRegistered) state.UnregisterStagingPreset(stagingPreset);
                if (stagingPreset != null) UnityEngine.Object.Destroy(stagingPreset);
                if (!preparedSuccessfully && stagingBossRegistered)
                    state.UnregisterStagingBoss(character != null ? character.Health : null);
                if (character != null && !preparedSuccessfully)
                    DestroyManagedCharacterQuiet(character);
            }
        }

        private static bool HasModeGPlayerAuthoredBuff(CharacterMainControl character)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (character == null || player == null || character.GetBuffManager() == null) return false;
                foreach (var buff in character.GetBuffManager().Buffs)
                {
                    if (buff != null && ReferenceEquals(buff.fromWho, player)) return true;
                }
            }
            catch { return true; }
            return false;
        }

        private void BeginActivateModeGManagedCharacter(CharacterMainControl character)
        {
            character.gameObject.SetActive(true);
            character.SetTeam(Teams.wolf);
        }

        private void CompleteActivateModeGManagedCharacter(CharacterMainControl character)
        {
            SetupAIAggro(character);
            if (character.Health != null)
            {
                character.Health.RequestHealthBar();
                character.Health.SetInvincible(false);
            }
        }

        private void ActivateModeGManagedCharacter(CharacterMainControl character)
        {
            BeginActivateModeGManagedCharacter(character);
            CompleteActivateModeGManagedCharacter(character);
        }

        private void CleanupModeGManagedCharacter(CharacterMainControl character,
            string runtimeNameKey, string runtimePresetName, string logTag)
        {
            if (character == null) return;
            try
            {
                ModeGRunState state = ModeGRunContext.Current;
                if (state != null)
                {
                    state.UnregisterStagingBoss(character.Health);
                    state.UnregisterTrackedBoss(character.Health);
                }
                UnregisterEnemyRecovery(character);
                ClearBossRandomLootTracking(character);
                FinalizeBossRushLootboxPathTracking(character);
                BossCleanupHelpers.DestroyRuntimePreset(
                    character, runtimeNameKey, runtimePresetName, logTag);
                DestroyManagedCharacterQuiet(character);
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] managed 精确清理异常: " + e.Message);
            }
        }

        internal static void DestroyManagedCharacterQuiet(CharacterMainControl character)
        {
            try
            {
                if (character != null && character.gameObject != null)
                    UnityEngine.Object.Destroy(character.gameObject);
            }
            catch { }
        }

        #endregion
    }
}
