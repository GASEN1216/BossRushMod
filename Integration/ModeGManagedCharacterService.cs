using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    internal static class ModeGManagedCharacterService
    {
        #region Mode G Managed Adapter Shared Helpers

        internal static bool IsManagedOwnerValid(ManagedBossSpawnContext ctx)
        {
            if (ctx == null || ctx.IsOwnerValid == null) return true;
            try { return ctx.IsOwnerValid(); } catch { return false; }
        }

        internal static async UniTask<CharacterMainControl> CreateModeGManagedCharacterAsync(
            CharacterRandomPreset basePreset, Vector3 position, ManagedBossSpawnContext ctx,
            string runtimeNameKey, string runtimePresetName)
        {
            if (basePreset == null || ctx == null || !IsManagedOwnerValid(ctx)) return null;
            ModeGRunState state = ModeGRunContext.Current;
            // 鸭王杯群战借同一条托管工厂：没有 Mode G run，staging 身份由 Mode H 生成 owner 自己登记与回收
            bool modeHOwner = ctx.Owner == ManagedBossOwner.ModeH;
            if (state == null && !modeHOwner) return null;

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
                if (state != null)
                {
                    stagingPresetRegistered = state.RegisterStagingPreset(stagingPreset);
                    if (!stagingPresetRegistered) return null;
                }

                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await stagingPreset.CreateCharacterAsync(
                    position, Vector3.forward, relatedScene, null, false);

                // Factory 返回后的首个同步段：先登记 exact 身份，再冻结对象。
                if (character == null || character.Health == null) return null;
                if (state != null) stagingBossRegistered = state.RegisterStagingBoss(character.Health, character);
                if ((state != null && !stagingBossRegistered) || !IsManagedOwnerValid(ctx) || character.Health.IsDead
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

                if (state != null) state.UnregisterStagingPreset(stagingPreset);
                stagingPresetRegistered = false;
                UnityEngine.Object.Destroy(stagingPreset);
                stagingPreset = null;
                preparedSuccessfully = true;
                return character;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] managed factory/configure 失败: " + e.Message);
                return null;
            }
            finally
            {
                if (stagingPresetRegistered && state != null) state.UnregisterStagingPreset(stagingPreset);
                if (stagingPreset != null) UnityEngine.Object.Destroy(stagingPreset);
                if (!preparedSuccessfully && stagingBossRegistered && state != null)
                    state.UnregisterStagingBoss(character != null ? character.Health : null);
                if (character != null && !preparedSuccessfully)
                    DestroyManagedCharacterQuiet(character);
            }
        }

        internal static bool HasModeGPlayerAuthoredBuff(CharacterMainControl character)
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

        internal static void BeginActivateModeGManagedCharacter(CharacterMainControl character)
        {
            character.gameObject.SetActive(true);
            character.SetTeam(Teams.wolf);
        }

        internal static void CompleteActivateModeGManagedCharacter(ModBehaviour owner, CharacterMainControl character)
        {
            owner.SetupAIAggro(character);
            if (character.Health != null)
            {
                character.Health.RequestHealthBar();
                character.Health.SetInvincible(false);
            }
        }

        internal static void ActivateModeGManagedCharacter(ModBehaviour owner, CharacterMainControl character)
        {
            BeginActivateModeGManagedCharacter(character);
            CompleteActivateModeGManagedCharacter(owner, character);
        }

        internal static void CleanupModeGManagedCharacter(ModBehaviour owner, CharacterMainControl character,
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
                owner.UnregisterDragonDescendantEnemyRecovery(character);
                owner.ClearDragonDescendantBossRandomLootTracking(character);
                owner.FinalizeDragonDescendantBossRushLootboxPathTracking(character);
                BossCleanupHelpers.DestroyRuntimePreset(
                    character, runtimeNameKey, runtimePresetName, logTag);
                DestroyManagedCharacterQuiet(character);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] managed 精确清理异常: " + e.Message);
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
