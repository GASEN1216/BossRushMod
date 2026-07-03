using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZombieBoomAttachmentTypeName = "AISpecialAttachment_BoomCar";
        private const string ZombieSelfDestructionSkillTypeName = "Skill_Grenade";

        internal void SanitizeBossRushZombieSpawn(CharacterMainControl character, string spawnOwner)
        {
            if (character == null || character.gameObject == null)
            {
                return;
            }

            try
            {
                RemoveBossRushZombieSelfDestructionSkills(character, spawnOwner);
                RemoveBossRushZombieBoomAttachments(character, spawnOwner);
            }
            catch (Exception e)
            {
                DevLog("[ZombieSpawnSanitizer] Sanitize failed (" + spawnOwner + "): " + e.Message);
            }
        }

        private void RemoveBossRushZombieBoomAttachments(CharacterMainControl character, string spawnOwner)
        {
            if (ShouldKeepBossRushZombieSelfDestructionSkill(character))
            {
                return;
            }

            CharacterRandomPreset preset = character.characterPreset;
            if (preset == null || preset.specialAttachmentBases == null || preset.specialAttachmentBases.Count <= 0)
            {
                return;
            }

            AISpecialAttachmentBase[] attachments = character.GetComponentsInChildren<AISpecialAttachmentBase>(true);
            if (attachments == null || attachments.Length <= 0)
            {
                return;
            }

            for (int i = 0; i < attachments.Length; i++)
            {
                AISpecialAttachmentBase attachment = attachments[i];
                if (attachment == null)
                {
                    continue;
                }

                if (!string.Equals(attachment.GetType().Name, ZombieBoomAttachmentTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    attachment.enabled = false;
                    Destroy(attachment);
                }
                catch (Exception destroyEx)
                {
                    DevLog("[ZombieSpawnSanitizer] Destroy boom attachment failed (" + spawnOwner + "): " + destroyEx.Message);
                }
            }
        }

        private void RemoveBossRushZombieSelfDestructionSkills(CharacterMainControl character, string spawnOwner)
        {
            if (ShouldKeepBossRushZombieSelfDestructionSkill(character))
            {
                return;
            }

            AICharacterController ai = character.GetComponentInChildren<AICharacterController>(true);
            if (ai == null)
            {
                return;
            }

            SkillBase skillInstance = ai.skillInstance;
            SkillBase skillPrefab = ai.skillPfb;
            bool hasGrenadeInstance =
                skillInstance != null &&
                string.Equals(skillInstance.GetType().Name, ZombieSelfDestructionSkillTypeName, StringComparison.Ordinal);
            bool hasGrenadePrefab =
                skillPrefab != null &&
                string.Equals(skillPrefab.GetType().Name, ZombieSelfDestructionSkillTypeName, StringComparison.Ordinal);

            if (!hasGrenadeInstance && !hasGrenadePrefab)
            {
                return;
            }

            try
            {
                ai.hasSkill = false;
                ai.skillPfb = null;
                ai.skillInstance = null;

                CharacterMainControl owner = ai.CharacterMainControl != null ? ai.CharacterMainControl : character;
                if (owner != null)
                {
                    owner.SetSkill(SkillTypes.characterSkill, null, null);
                }

                if (skillInstance != null)
                {
                    skillInstance.enabled = false;
                    Destroy(skillInstance.gameObject);
                }
            }
            catch (Exception destroyEx)
            {
                DevLog("[ZombieSpawnSanitizer] Destroy self-destruction skill failed (" + spawnOwner + "): " + destroyEx.Message);
            }
        }

        private static bool ShouldKeepBossRushZombieSelfDestructionSkill(CharacterMainControl character)
        {
            if (character == null || character.gameObject == null)
            {
                return false;
            }

            ZombieModeEnemyRuntimeMarker marker = character.GetComponent<ZombieModeEnemyRuntimeMarker>();
            return marker != null &&
                !marker.IsBoss &&
                marker.EnemyKind == ZombieModeEnemyKind.Special &&
                marker.SpecialKind == ZombieModeSpecialKind.OfficialExploder;
        }
    }
}
