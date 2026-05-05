using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static readonly string[] ZombieModeOriginalExtractionExcludedNamePrefixes =
        {
            "ZombieMode_",
            "ModeF_",
            "BossRush_"
        };

        private readonly List<GameObject> zombieModeDisabledOriginalExtractionObjects = new List<GameObject>();
        private readonly Dictionary<int, bool> zombieModeOriginalExtractionActiveStateByObjectId = new Dictionary<int, bool>();
        private readonly List<OriginalCharacterIsolationRecord> zombieModeDisabledOriginalCharacterRecords = new List<OriginalCharacterIsolationRecord>();

        private bool ApplyZombieModeMapIsolationShell(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            PreCacheMapSpawnerPositions();
            DisableZombieModeOriginalSpawners(runId);
            DisableZombieModeOriginalExtractionPoints(runId);
            DisableZombieModeOriginalCharacters();
            return true;
        }

        private void RestoreZombieModeMapIsolationShell()
        {
            RestoreZombieModeOriginalCharacters();
            RestoreZombieModeOriginalSpawners();
            RestoreZombieModeOriginalExtractionPoints();
        }

        private void DisableZombieModeOriginalExtractionPoints(int runId)
        {
            bool usedExitCreatorSnapshot;
            int[] disabledIds;
            OriginalExtractionPointIsolationHelper.Disable(
                zombieModeDisabledOriginalExtractionObjects,
                zombieModeOriginalExtractionActiveStateByObjectId,
                ShouldSkipZombieModeOriginalExtractionArea,
                ZombieModeOriginalExtractionExcludedNamePrefixes,
                delegate(CountDownArea area, GameObject disabledObject, bool wasActive)
                {
                    RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, null, area, delegate
                    {
                        try
                        {
                            if (disabledObject != null)
                            {
                                disabledObject.SetActive(wasActive);
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[ZombieMode] [WARNING] 恢复原版撤离点 SetActive 失败: " + e.Message);
                        }
                    });
                },
                out usedExitCreatorSnapshot,
                out disabledIds);

            if (zombieModeRunState.MapProfile != null)
            {
                zombieModeRunState.MapProfile.DisabledExtractionAreaIds = disabledIds;
            }

            DevLog("[ZombieMode] 已禁用原版撤离点: " + disabledIds.Length
                + " (source=" + (usedExitCreatorSnapshot ? "ExitCreator" : "fallback") + ")");
        }

        private bool ShouldSkipZombieModeOriginalExtractionArea(CountDownArea area)
        {
            return zombieModeRunState.ActiveExtractionArea != null && area == zombieModeRunState.ActiveExtractionArea;
        }

        private void RestoreZombieModeOriginalExtractionPoints()
        {
            try
            {
                OriginalExtractionPointIsolationHelper.Restore(zombieModeDisabledOriginalExtractionObjects, zombieModeOriginalExtractionActiveStateByObjectId);
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 恢复原版撤离点失败: " + e.Message);
            }
            if (zombieModeRunState.MapProfile != null)
            {
                zombieModeRunState.MapProfile.DisabledExtractionAreaIds = new int[0];
            }
        }

        private void DisableZombieModeOriginalSpawners(int runId)
        {
            // 这里直接复用 BossRush 进图时的原版刷怪器清理逻辑，避免 ZombieMode 再维护一套不一致的软隔离分支。
            spawnersDisabled = false;
            DisableAllSpawners();
            DevLog("[ZombieMode] 已复用 BossRush 进图逻辑清理原版刷怪器");
        }

        private void RestoreZombieModeOriginalSpawners()
        {
            // BossRush 同源逻辑会直接销毁原版 CharacterSpawnerRoot；这里只重置标志，允许下次新场景重新扫描。
            spawnersDisabled = false;
        }

        private void DisableZombieModeOriginalCharacters()
        {
            int disabledCount = OriginalCharacterIsolationHelper.Disable(
                zombieModeDisabledOriginalCharacterRecords,
                ShouldSkipZombieModeOriginalCharacter);
            DevLog("[ZombieMode] 已隔离原版角色: disabled=" + disabledCount
                + ", tracked=" + zombieModeDisabledOriginalCharacterRecords.Count);
        }

        private void RestoreZombieModeOriginalCharacters()
        {
            try
            {
                OriginalCharacterIsolationHelper.Restore(zombieModeDisabledOriginalCharacterRecords);
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 恢复原版角色失败: " + e.Message);
            }
        }

        private bool ShouldSkipZombieModeOriginalCharacter(CharacterMainControl character)
        {
            if (character == null || character.gameObject == null)
            {
                return true;
            }

            if (character.IsMainCharacter || character.Team == Teams.player)
            {
                return true;
            }

            if (character.GetComponent<ZombieModeEnemyRuntimeMarker>() != null)
            {
                return true;
            }

            return ShouldPreserveZombieModeOriginalCharacter(character);
        }

        private bool ShouldPreserveZombieModeOriginalCharacter(CharacterMainControl character)
        {
            if (character == null)
            {
                return true;
            }

            try
            {
                if (!Team.IsEnemy(Teams.player, character.Team))
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] Team.IsEnemy 检查失败: " + e.Message);
            }

            if (IsZombieModeRetainedNeutralWhitelisted(character))
            {
                return true;
            }

            try
            {
                if (character.GetComponentInChildren<INPCController>(true) != null)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] INPCController 检查失败: " + e.Message);
            }

            try
            {
                if (character.GetComponentInChildren<NPCInteractableBase>(true) != null)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] NPCInteractableBase 检查失败: " + e.Message);
            }

            try
            {
                if (character.GetComponentInChildren<DuckovDialogueActor>(true) != null)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] DuckovDialogueActor 检查失败: " + e.Message);
            }

            try
            {
                if (character.GetComponentInChildren<Duckov.Economy.StockShop>(true) != null)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] StockShop 检查失败: " + e.Message);
            }

            try
            {
                if (character.GetComponentInChildren<IMerchant>(true) != null)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] IMerchant 检查失败: " + e.Message);
            }

            try
            {
                if (NPCModuleRegistry.ShouldSpawnAnyInScene(this, SceneManager.GetActiveScene().name))
                {
                    WeddingNpcResidentMarker weddingMarker = character.GetComponentInChildren<WeddingNpcResidentMarker>(true);
                    if (weddingMarker != null && !string.IsNullOrEmpty(weddingMarker.NpcId) && weddingMarker.NpcId != "__detached__")
                    {
                        return true;
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] WeddingNpcResidentMarker 检查失败: " + e.Message);
            }

            return false;
        }

        private bool IsZombieModeRetainedNeutralWhitelisted(CharacterMainControl character)
        {
            if (character == null || zombieModeRunState.MapProfile == null)
            {
                return false;
            }

            string[] whitelist = zombieModeRunState.MapProfile.RetainedNeutralWhitelistTypes;
            if (whitelist == null || whitelist.Length <= 0)
            {
                return false;
            }

            string objectName = character.gameObject != null ? character.gameObject.name : string.Empty;
            string presetName = character.characterPreset != null ? character.characterPreset.name : string.Empty;
            string presetKey = character.characterPreset != null ? character.characterPreset.nameKey : string.Empty;
            for (int i = 0; i < whitelist.Length; i++)
            {
                string token = whitelist[i];
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (StringEqualsIgnoreCase(objectName, token) ||
                    StringEqualsIgnoreCase(presetName, token) ||
                    StringEqualsIgnoreCase(presetKey, token))
                {
                    return true;
                }
            }

            return false;
        }

        private bool StringEqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
