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

        private bool ApplyZombieModeMapIsolationShell(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            DisableZombieModeOriginalSpawners(runId);
            DisableZombieModeOriginalExtractionPoints(runId);
            ClearZombieModeOriginalEnemies();
            return true;
        }

        private void RestoreZombieModeMapIsolationShell()
        {
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
            int disabledCount = 0;
            int destroyedCount = 0;
            int preservedLightsCount = 0;
            int skippedCount = 0;
            CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
            if (spawners == null)
            {
                return;
            }

            for (int i = 0; i < spawners.Length; i++)
            {
                CharacterSpawnerRoot spawner = spawners[i];
                if (spawner == null || spawner.gameObject == null)
                {
                    continue;
                }

                if (ShouldSkipZombieModeOriginalSpawner(spawner))
                {
                    skippedCount++;
                    continue;
                }

                if (!_createdFieldCached)
                {
                    _cachedCreatedField = typeof(CharacterSpawnerRoot).GetField("created",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _createdFieldCached = true;
                }

                if (_cachedCreatedField != null)
                {
                    try
                    {
                        _cachedCreatedField.SetValue(spawner, true);
                        disabledCount++;
                    }
                    catch (Exception e)
                    {
                        DevLog("[ZombieMode] [WARNING] 设置原版 spawner created=true 失败: " + e.Message);
                    }
                }

                try
                {
                    Light[] lights = spawner.gameObject.GetComponentsInChildren<Light>(true);
                    if (lights != null)
                    {
                        for (int j = 0; j < lights.Length; j++)
                        {
                            Light light = lights[j];
                            if (light == null || light.gameObject == null)
                            {
                                continue;
                            }

                            light.transform.SetParent(null);
                            preservedLightsCount++;
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] [WARNING] 保留原版 spawner 灯光失败: " + e.Message);
                }

                try
                {
                    RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, spawner.gameObject, spawner, null);
                    Destroy(spawner.gameObject);
                    destroyedCount++;
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] [WARNING] 销毁原版 spawner 失败: " + e.Message);
                }
            }

            DevLog("[ZombieMode] 已禁用并销毁原版刷怪器: disabled=" + disabledCount
                + ", destroyed=" + destroyedCount
                + ", preservedLights=" + preservedLightsCount
                + ", scanned=" + spawners.Length
                + ", skipped=" + skippedCount);
        }

        private bool ShouldSkipZombieModeOriginalSpawner(CharacterSpawnerRoot spawner)
        {
            GameObject go = spawner != null ? spawner.gameObject : null;
            if (go == null)
            {
                return true;
            }

            string name = go.name ?? string.Empty;
            return name.StartsWith("ZombieMode_", StringComparison.Ordinal) ||
                   name.StartsWith("ModeE_", StringComparison.Ordinal) ||
                   name.StartsWith("ModeF_", StringComparison.Ordinal) ||
                   go.GetComponent<ZombieModeEnemyRuntimeMarker>() != null;
        }

        private void ClearZombieModeOriginalEnemies()
        {
            CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>(true);
            if (characters == null)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            for (int i = 0; i < characters.Length; i++)
            {
                CharacterMainControl character = characters[i];
                if (character == null || character.gameObject == null || character.gameObject.scene != scene)
                {
                    continue;
                }

                if (character.IsMainCharacter || character.Team == Teams.player)
                {
                    continue;
                }

                if (ShouldPreserveZombieModeOriginalCharacter(character))
                {
                    continue;
                }

                if (character.GetComponent<ZombieModeEnemyRuntimeMarker>() != null)
                {
                    continue;
                }

                try
                {
                    Destroy(character.gameObject);
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] [WARNING] 清理原版敌人 Destroy 失败: " + e.Message);
                }
            }
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
                MonoBehaviour[] behaviours = character.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    if (behaviour is IMerchant)
                    {
                        return true;
                    }

                    string typeName = behaviour.GetType().Name;
                    if (!string.IsNullOrEmpty(typeName) &&
                        (typeName.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("Merchant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 中立 NPC 类型扫描失败: " + e.Message);
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
