using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BossRush
{
    public static class ZombieModeMapSelectionHelper
    {
        private static int pendingZombieMapEntryIndex = -1;
        private static bool pendingZombieEntryFromMapSelection = false;
        private static bool pendingZombieMapConfirmed = false;
        private static bool pendingZombieCashPromptOpen = false;
        private static int confirmationWatchToken = 0;
        private static readonly List<GameObject> zombieEntryObjects = new List<GameObject>();
        private static readonly List<GameObject> hiddenEntries = new List<GameObject>();

        public static bool HasPendingZombieEntry
        {
            get { return pendingZombieEntryFromMapSelection && pendingZombieMapConfirmed && pendingZombieMapEntryIndex >= 0; }
        }

        public static int PendingZombieMapEntryIndex
        {
            get { return pendingZombieMapEntryIndex; }
        }

        public static string GetPendingTargetSubSceneName()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingZombieMapEntryIndex >= 0 && configs != null && pendingZombieMapEntryIndex < configs.Length)
            {
                return configs[pendingZombieMapEntryIndex].sceneName;
            }
            return null;
        }

        public static string GetPendingMainSceneName()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingZombieMapEntryIndex >= 0 && configs != null && pendingZombieMapEntryIndex < configs.Length)
            {
                return configs[pendingZombieMapEntryIndex].sceneID;
            }
            return null;
        }

        public static Vector3? GetPendingCustomPosition()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingZombieMapEntryIndex >= 0 && configs != null && pendingZombieMapEntryIndex < configs.Length)
            {
                return configs[pendingZombieMapEntryIndex].customSpawnPos;
            }
            return null;
        }

        public static Cost CreateZombieModeCost()
        {
            Cost cost = new Cost();
            cost.money = 0L;
            cost.items = new Cost.ItemEntry[1];
            cost.items[0].id = BossRushItemIds.ZombieTideInvitation;
            cost.items[0].amount = 1L;
            return cost;
        }

        public static Cost CreateZombieModeFreeMapEntryCost()
        {
            Cost cost = new Cost();
            cost.money = 0L;
            cost.items = null;
            return cost;
        }

        public static bool CanOpenZombieModeMapSelection(out string failureReason)
        {
            failureReason = null;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null)
            {
                failureReason = L10n.T("BossRush_ZombieMode_NotInitialized");
                return false;
            }

            if (!inst.CanStartZombieModeMapSelectionPhase1(out failureReason))
            {
                return false;
            }

            Cost cost = CreateZombieModeCost();
            if (!cost.Enough)
            {
                failureReason = L10n.T("BossRush_ZombieMode_NoInvitation");
                return false;
            }

            return true;
        }

        public static bool ShowZombieModeMapSelection(out string failureReason)
        {
            failureReason = null;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null)
            {
                failureReason = L10n.T("BossRush_ZombieMode_NotInitialized");
                return false;
            }

            if (!inst.TryBeginZombieModeMapSelectionPhase1(out failureReason))
            {
                return false;
            }

            try
            {
                MapSelectionView mapView = MapSelectionView.Instance;
                if (mapView == null)
                {
                    inst.CancelZombieModeMapSelectionPhase1();
                    failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                    return false;
                }

                if (!InjectZombieModeEntries(mapView))
                {
                    inst.CancelZombieModeMapSelectionPhase1();
                    failureReason = L10n.T("BossRush_ZombieMode_NoMaps");
                    return false;
                }

                pendingZombieEntryFromMapSelection = true;
                pendingZombieMapEntryIndex = -1;
                pendingZombieMapConfirmed = false;
                mapView.Open(null);
                inst.StartCoroutine(WatchMapSelectionViewClose(mapView));
                inst.StartCoroutine(DelayedRefreshDisplayNames());
                ModBehaviour.DevLog("[ZombieMode] 已打开独立地图选择界面");
                return true;
            }
            catch (Exception e)
            {
                inst.CancelZombieModeMapSelectionPhase1();
                failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                ModBehaviour.DevLog("[ZombieMode] ShowZombieModeMapSelection failed: " + e.Message);
                return false;
            }
        }

        public static void SetPendingZombieMapEntryIndex(int index)
        {
            pendingZombieMapEntryIndex = index;
            pendingZombieMapConfirmed = false;
            confirmationWatchToken++;
            ModBehaviour.DevLog("[ZombieMode] 设置待确认地图条目索引: " + index);
        }

        public static void ConfirmZombieModeMapEntry(int index)
        {
            if (!pendingZombieEntryFromMapSelection || index < 0 || pendingZombieCashPromptOpen)
            {
                return;
            }

            SetPendingZombieMapEntryIndex(index);
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null)
            {
                return;
            }

            pendingZombieCashPromptOpen = true;
            inst.ShowZombieModeCashInvestmentPrompt(delegate
            {
                pendingZombieCashPromptOpen = false;
                if (!pendingZombieEntryFromMapSelection ||
                    pendingZombieMapEntryIndex != index ||
                    pendingZombieMapConfirmed)
                {
                    return;
                }

                pendingZombieMapConfirmed = true;
                inst.MarkZombieModeMapConfirmedPhase1();
                if (!inst.IsZombieModeMapLoadReadyPhase1())
                {
                    pendingZombieMapConfirmed = false;
                    ClearPendingZombieEntry();
                    return;
                }
                StartZombieModeConfirmedMapLoad(index);
            });

            ModBehaviour.DevLog("[ZombieMode] 玩家选择末日丧尸模式地图条目，等待现金投入: " + index);
        }

        public static void MarkTargetSceneLoadStarted()
        {
            pendingZombieEntryFromMapSelection = true;
        }

        public static void ClearPendingZombieEntry()
        {
            pendingZombieEntryFromMapSelection = false;
            pendingZombieMapEntryIndex = -1;
            pendingZombieMapConfirmed = false;
            pendingZombieCashPromptOpen = false;
            confirmationWatchToken++;
        }

        private static bool InjectZombieModeEntries(MapSelectionView mapView)
        {
            ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
            MapSelectionEntryInjectionHelper.RestoreHiddenEntries(hiddenEntries);
            MapSelectionEntryInjectionHelper.DestroyCreatedEntries(zombieEntryObjects);

            int createdCount = MapSelectionEntryInjectionHelper.InjectEntries(
                mapView,
                "ZombieMode_MapSelectionEntry_",
                zombieEntryObjects,
                hiddenEntries,
                mapConfigs,
                ConfigureZombieModeEntry,
                GetDisplayName,
                SetupZombieModeCostDisplay,
                null,
                out _);
            return createdCount > 0;
        }

        private static void ConfigureZombieModeEntry(MapSelectionEntry entry, ModBehaviour.BossRushMapConfig mapConfig, int entryIndex)
        {
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "sceneID", mapConfig.sceneID);
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "beaconIndex", mapConfig.beaconIndex);
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "cost", CreateZombieModeFreeMapEntryCost());
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "conditions", null);
            entry.enabled = false;

            CostDisplay costDisplay = entry.GetComponentInChildren<CostDisplay>(true);
            if (costDisplay != null)
            {
                SetupZombieModeCostDisplay(costDisplay);
            }

            ZombieModeMapEntryClickHandler clickHandler = entry.gameObject.GetComponent<ZombieModeMapEntryClickHandler>();
            if (clickHandler == null)
            {
                clickHandler = entry.gameObject.AddComponent<ZombieModeMapEntryClickHandler>();
            }
            clickHandler.entryIndex = entryIndex;
        }

        private static void SetupZombieModeCostDisplay(GameObject entryObject)
        {
            if (entryObject == null)
            {
                return;
            }

            SetupZombieModeCostDisplay(entryObject.GetComponentInChildren<CostDisplay>(true));
        }

        private static void SetupZombieModeCostDisplay(CostDisplay costDisplay)
        {
            if (costDisplay == null)
            {
                return;
            }

            MapSelectionEntryInjectionHelper.ClearCostDisplayItems(costDisplay);
            costDisplay.Setup(CreateZombieModeCost(), 1);
            costDisplay.gameObject.SetActive(true);
        }

        private static IEnumerator WatchMapSelectionViewClose(MapSelectionView mapView)
        {
            while (mapView != null && mapView.open)
            {
                yield return null;
            }

            MapSelectionEntryInjectionHelper.RestoreHiddenEntries(hiddenEntries);
            MapSelectionEntryInjectionHelper.DestroyCreatedEntries(zombieEntryObjects);
            if (!pendingZombieMapConfirmed)
            {
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.CancelZombieModeMapSelectionPhase1();
                }
                ClearPendingZombieEntry();
            }
        }

        private static IEnumerator DelayedRefreshDisplayNames()
        {
            yield return new WaitForEndOfFrame();
            RefreshAllEntryDisplayNames();
            yield return new WaitForSeconds(0.1f);
            RefreshAllEntryDisplayNames();
            yield return new WaitForSeconds(0.2f);
            RefreshAllEntryDisplayNames();
        }

        private static void RefreshAllEntryDisplayNames()
        {
            ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
            if (mapConfigs == null)
            {
                return;
            }

            for (int i = 0; i < zombieEntryObjects.Count && i < mapConfigs.Length; i++)
            {
                if (zombieEntryObjects[i] != null)
                {
                    MapSelectionEntryInjectionHelper.SetEntryDisplayNameDirect(zombieEntryObjects[i], GetDisplayName(mapConfigs[i]));
                }
            }
        }

        private static string GetDisplayName(ModBehaviour.BossRushMapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                return L10n.T("BossRush_ZombieMode");
            }

            string baseName = mapConfig.displayName;
            if (string.IsNullOrEmpty(baseName))
            {
                SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(mapConfig.sceneID);
                baseName = sceneInfo != null ? sceneInfo.DisplayName : mapConfig.sceneID;
            }

            return string.Format(L10n.T("BossRush_ZombieMode_MapEntryPrefix"), baseName);
        }

        private static void StartZombieModeConfirmedMapLoad(int entryIndex)
        {
            ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
            if (mapConfigs == null || entryIndex < 0 || entryIndex >= mapConfigs.Length)
            {
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                }
                ClearPendingZombieEntry();
                return;
            }

            try
            {
                ModBehaviour.BossRushMapConfig mapConfig = mapConfigs[entryIndex];
                if (mapConfig == null || string.IsNullOrEmpty(mapConfig.sceneID))
                {
                    ModBehaviour inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                    }
                    ClearPendingZombieEntry();
                    return;
                }

                LevelManager.loadLevelBeaconIndex = mapConfig.beaconIndex;
                MapSelectionView mapView = MapSelectionView.Instance;
                if (mapView != null)
                {
                    mapView.Close();
                }

                if (SceneLoader.Instance != null)
                {
                    SceneLoader.Instance.LoadScene(mapConfig.sceneID, null, true).Forget();
                }
                else
                {
                    ModBehaviour inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                    }
                    ClearPendingZombieEntry();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] StartZombieModeConfirmedMapLoad failed: " + e.Message);
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                }
                ClearPendingZombieEntry();
            }
        }

    }

    public class ZombieModeMapEntryClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public int entryIndex = -1;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (entryIndex >= 0)
            {
                ZombieModeMapSelectionHelper.ConfirmZombieModeMapEntry(entryIndex);
            }
        }
    }
}
