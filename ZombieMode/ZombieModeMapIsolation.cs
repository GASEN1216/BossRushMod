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

        private readonly List<GameObject> zombieModeDisabledMapObjects = new List<GameObject>();
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

            for (int i = zombieModeDisabledMapObjects.Count - 1; i >= 0; i--)
            {
                GameObject obj = zombieModeDisabledMapObjects[i];
                if (obj != null)
                {
                    try { obj.SetActive(true); } catch { }
                }
            }

            zombieModeDisabledMapObjects.Clear();
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
                        catch { }
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
            try { OriginalExtractionPointIsolationHelper.Restore(zombieModeDisabledOriginalExtractionObjects, zombieModeOriginalExtractionActiveStateByObjectId); } catch { }
            if (zombieModeRunState.MapProfile != null)
            {
                zombieModeRunState.MapProfile.DisabledExtractionAreaIds = new int[0];
            }
        }

        private void DisableZombieModeOriginalSpawners(int runId)
        {
            CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
            if (spawners == null)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            for (int i = 0; i < spawners.Length; i++)
            {
                CharacterSpawnerRoot spawner = spawners[i];
                if (spawner == null || spawner.gameObject == null || spawner.gameObject.scene != scene)
                {
                    continue;
                }

                if (!spawner.gameObject.activeSelf)
                {
                    continue;
                }

                GameObject disabledObject = spawner.gameObject;
                disabledObject.SetActive(false);
                zombieModeDisabledMapObjects.Add(disabledObject);
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, null, spawner, delegate
                {
                    try
                    {
                        if (disabledObject != null)
                        {
                            disabledObject.SetActive(true);
                        }
                    }
                    catch { }
                });
            }
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
                catch { }
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
            catch { }

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
            catch { }

            try
            {
                if (character.GetComponentInChildren<Duckov.Economy.StockShop>(true) != null)
                {
                    return true;
                }
            }
            catch { }

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
            catch { }

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
