using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal async UniTask<ManagedBossPrepareResult> PrepareManagedPhantomWitchAsync(
            Vector3 position, ManagedBossSpawnContext ctx)
        {
            CharacterMainControl character = null;
            PhantomWitchAbilityController controller = null;
            GameObject controllerObject = null;
            bool assetReferenceAdded = false;
            try
            {
                character = await CreateModeGManagedCharacterAsync(
                    FindPhantomWitchBasePreset(), position, ctx, PhantomWitchConfig.BossNameKey,
                    "PhantomWitch_Preset");
                if (character == null) return null;

                character.gameObject.name = "BossRush_ModeG_PhantomWitch";
                character.transform.localScale = Vector3.one * PhantomWitchConfig.BossModelScale;
                SetupPhantomWitchAttributes(character);
                ApplyBossStatMultiplier(character);
                EquipPhantomWitchWeapon(character);

                PhantomWitchAssetManager.AddReference();
                assetReferenceAdded = true;
                controllerObject = new GameObject("ModeG_PhantomWitch_AbilityController");
                controllerObject.SetActive(false);
                controller = controllerObject.AddComponent<PhantomWitchAbilityController>();

                CharacterMainControl capturedCharacter = character;
                PhantomWitchAbilityController capturedController = controller;
                GameObject capturedControllerObject = controllerObject;
                bool activated = false;
                bool controllerReleasedAsset = false;

                ManagedBossRuntimeHandle handle = new ManagedBossRuntimeHandle
                {
                    Character = character,
                    AchievementBossType = "PhantomWitch",
                    Activate = () =>
                    {
                        if (activated) return true;
                        if (!IsManagedOwnerValid(ctx) || capturedCharacter == null
                            || capturedCharacter.Health == null || capturedCharacter.Health.IsDead) return false;

                        BeginActivateModeGManagedCharacter(capturedCharacter);
                        capturedControllerObject.SetActive(true);
                        capturedController.Initialize(capturedCharacter, position);
                        // 激活前绑定托管辅助契约（规格 §20 第 16 条）：随从激活前原子提交，
                        // 父 owner/child handle 语义，迟到 ticket 不写 Legacy 单例。
                        capturedController.BindModeGAuxiliaryContract(
                            ctx.TryCommitAuxiliaryBeforeActivation, ctx.OnAuxiliaryReleased);
                        phantomWitchInstances[capturedCharacter] = capturedController;
                        CompleteActivateModeGManagedCharacter(capturedCharacter);
                        PhantomWitchAssetManager.CreateEffect(
                            position, PhantomWitchConfig.EffectDefaultDuration);
                        activated = true;
                        return true;
                    },
                    CleanupAfterDeath = info =>
                    {
                        if (capturedController != null)
                        {
                            capturedController.OnBossDeath();
                            controllerReleasedAsset = true;
                        }
                    },
                    Cleanup = reason =>
                    {
                        if (capturedController != null && !controllerReleasedAsset)
                        {
                            capturedController.OnBossDeath();
                            controllerReleasedAsset = true;
                        }
                        phantomWitchInstances.Remove(capturedCharacter);
                        if (!controllerReleasedAsset && assetReferenceAdded)
                            ReleasePhantomWitchInstance();
                        CleanupModeGManagedCharacter(capturedCharacter,
                            PhantomWitchConfig.BossNameKey, "PhantomWitch_Preset", "[PhantomWitch]");
                        if (capturedControllerObject != null)
                            UnityEngine.Object.Destroy(capturedControllerObject);
                    }
                };
                return new ManagedBossPrepareResult { Character = character, Handle = handle };
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] PrepareManagedPhantomWitchAsync 异常: " + e.Message);
                if (controller != null) controller.OnBossDeath();
                else if (assetReferenceAdded) ReleasePhantomWitchInstance();
                if (controllerObject != null) UnityEngine.Object.Destroy(controllerObject);
                CleanupModeGManagedCharacter(character, PhantomWitchConfig.BossNameKey,
                    "PhantomWitch_Preset", "[PhantomWitch]");
                return null;
            }
        }
    }
}
