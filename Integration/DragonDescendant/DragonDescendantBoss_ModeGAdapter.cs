using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DragonDescendantRuntimeModule
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
                ModBehaviour.OriginalWeaponData originalWeaponData = GetWeaponDataFromEquippedWeapon(character);
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
                        if (ctx.Role == ManagedBossRole.Primary)
                        {
                            dragonDescendantInstance = capturedCharacter;
                            dragonDescendantAbilities = capturedController;
                        }
                        capturedController.Initialize(capturedCharacter, originalWeaponData);
                        RegisterDragonDescendantSetBonus(capturedCharacter);
                        setBonusRegistered = true;
                        CompleteActivateModeGManagedCharacter(capturedCharacter);
                        activated = true;
                        return true;
                    },
                    CleanupAfterDeath = info =>
                    {
                        if (setBonusRegistered)
                        {
                            UnregisterDragonDescendantSetBonus(capturedCharacter);
                            setBonusRegistered = false;
                        }
                    },
                    Cleanup = reason =>
                    {
                        if (setBonusRegistered)
                        {
                            UnregisterDragonDescendantSetBonus(capturedCharacter);
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

    }
}
