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
        private static bool pendingZombieMapLoadStarted = false;
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
            // 使用空数组而不是 null，避免下游代码在访问 cost.items 时引发 NRE。
            // Cost.IsFree 兼容 null，但注入到原版 MapSelectionEntry 后，CostDisplay.Setup
            // 等其他路径不一定全部对 null 友好，保守起见使用空数组。
            cost.items = new Cost.ItemEntry[0];
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

            MapSelectionView mapView = null;
            try
            {
                mapView = MapSelectionView.Instance;
            }
            catch (Exception e)
            {
                inst.CancelZombieModeMapSelectionPhase1();
                failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                ModBehaviour.DevLog("[ZombieMode] 获取 MapSelectionView.Instance 失败: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return false;
            }

            if (mapView == null)
            {
                inst.CancelZombieModeMapSelectionPhase1();
                failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                ModBehaviour.DevLog("[ZombieMode] MapSelectionView.Instance 为 null，无法打开地图选择 UI");
                return false;
            }

            try
            {
                if (!InjectZombieModeEntries(mapView))
                {
                    inst.CancelZombieModeMapSelectionPhase1();
                    failureReason = L10n.T("BossRush_ZombieMode_NoMaps");
                    return false;
                }
            }
            catch (Exception e)
            {
                inst.CancelZombieModeMapSelectionPhase1();
                // 注入失败时尽量恢复原版条目，避免地图选择 UI 残留隐藏条目。
                try
                {
                    MapSelectionEntryInjectionHelper.RestoreHiddenEntries(hiddenEntries);
                    MapSelectionEntryInjectionHelper.DestroyCreatedEntries(zombieEntryObjects);
                }
                catch (Exception cleanupEx)
                {
                    ModBehaviour.DevLog("[ZombieMode] 注入失败后清理也失败: " + cleanupEx.Message);
                }
                failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                ModBehaviour.DevLog("[ZombieMode] InjectZombieModeEntries 抛出异常: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return false;
            }

            pendingZombieEntryFromMapSelection = true;
            pendingZombieMapEntryIndex = -1;
            pendingZombieMapConfirmed = false;

            try
            {
                mapView.Open(null);
            }
            catch (Exception e)
            {
                inst.CancelZombieModeMapSelectionPhase1();
                try
                {
                    MapSelectionEntryInjectionHelper.RestoreHiddenEntries(hiddenEntries);
                    MapSelectionEntryInjectionHelper.DestroyCreatedEntries(zombieEntryObjects);
                }
                catch { }
                ClearPendingZombieEntry();
                failureReason = L10n.T("BossRush_ZombieMode_OpenMapFailed");
                ModBehaviour.DevLog("[ZombieMode] mapView.Open 抛出异常: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return false;
            }

            try
            {
                inst.StartCoroutine(WatchMapSelectionViewClose(mapView));
                inst.StartCoroutine(DelayedRefreshDisplayNames());
            }
            catch (Exception e)
            {
                // 协程启动失败不影响 UI 已打开，仅记录。
                ModBehaviour.DevLog("[ZombieMode] 启动监控协程失败: " + e.GetType().Name + ": " + e.Message);
            }

            ModBehaviour.DevLog("[ZombieMode] 已打开独立地图选择界面");
            return true;
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
            if (!pendingZombieEntryFromMapSelection ||
                index < 0 ||
                pendingZombieCashPromptOpen ||
                pendingZombieMapConfirmed ||
                pendingZombieMapLoadStarted)
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
            inst.ShowZombieModeCashInvestmentPrompt(
                delegate
                {
                    pendingZombieCashPromptOpen = false;
                    if (!pendingZombieEntryFromMapSelection ||
                        pendingZombieMapEntryIndex != index ||
                        pendingZombieMapConfirmed ||
                        pendingZombieMapLoadStarted)
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
                },
                delegate
                {
                    // 玩家在现金弹窗按"返回"：仅释放弹窗占用标记 + 清掉这一次的待选索引，
                    // 让玩家可以继续在 MapSelectionView 里挑选其他地图，而不是关闭整个地图选择 UI。
                    pendingZombieCashPromptOpen = false;
                    pendingZombieMapEntryIndex = -1;
                    pendingZombieMapConfirmed = false;
                    confirmationWatchToken++;
                    ModBehaviour.DevLog("[ZombieMode] 玩家在现金弹窗按返回，已撤销本次条目选择并保留地图选择 UI");
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
            pendingZombieMapLoadStarted = false;
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
                OnZombieModeEntryCreated,
                out _);
            return createdCount > 0;
        }

        // 与 BossRushMapSelectionHelper.OnBossRushEntryCreated 等价：在 SetActive+Setup 之后挂上自定义预览图，
        // 让条目背景图与 F9 BossRush 入场 UI 视觉一致。
        private static void OnZombieModeEntryCreated(
            MapSelectionEntry uiEntry,
            GameObject entryObject,
            ModBehaviour.BossRushMapConfig mapConfig,
            int entryIndex,
            MapSelectionEntry template,
            Transform targetParent)
        {
            if (uiEntry == null || mapConfig == null)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(mapConfig.previewImageName))
                {
                    BossRushMapSelectionHelper.UpdateEntryThumbnailWithImage(uiEntry, mapConfig.previewImageName);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] OnZombieModeEntryCreated 预览图加载失败 (sceneID=" + mapConfig.sceneID + "): " + e.Message);
            }
        }

        private static void ConfigureZombieModeEntry(MapSelectionEntry entry, ModBehaviour.BossRushMapConfig mapConfig, int entryIndex)
        {
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "sceneID", mapConfig.sceneID);
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "beaconIndex", mapConfig.beaconIndex);
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "cost", CreateZombieModeFreeMapEntryCost());
            MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "conditions", null);
            entry.enabled = false;

            // 注意：不要在这里调用 SetupZombieModeCostDisplay。本回调在 cloned.SetActive(true) 之前
            // 执行，CostDisplay.Awake 尚未跑过，其内部 ItemPool / moneyContainer 等运行时状态还没就绪，
            // 调用 costDisplay.Setup(...) 会抛 NullReferenceException。
            // CostDisplay 的统一设置交给 InjectEntries 的 setupCostDisplay 回调（在 SetActive + Setup 之后执行）。

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
            return MapSelectionEntryInjectionHelper.DelayedRefreshDisplayNames(RefreshAllEntryDisplayNames);
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
                ModBehaviour.DevLog("[ZombieMode] StartZombieModeConfirmedMapLoad 终止: entryIndex=" + entryIndex + " 超出 mapConfigs 范围 (Length=" + (mapConfigs != null ? mapConfigs.Length : -1) + ")");
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                }
                ClearPendingZombieEntry();
                return;
            }

            ModBehaviour.BossRushMapConfig mapConfig = mapConfigs[entryIndex];
            if (mapConfig == null || string.IsNullOrEmpty(mapConfig.sceneID))
            {
                ModBehaviour.DevLog("[ZombieMode] StartZombieModeConfirmedMapLoad 终止: mapConfig 或 sceneID 为空 (entryIndex=" + entryIndex + ")");
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                }
                ClearPendingZombieEntry();
                return;
            }

            // 在协程外提取必要字段，方便日志/异步函数捕获。
            string sceneID = mapConfig.sceneID;
            int beaconIndex = mapConfig.beaconIndex;
            pendingZombieMapLoadStarted = true;
            ModBehaviour.DevLog("[ZombieMode] 开始加载目标场景: sceneID=" + sceneID + ", beaconIndex=" + beaconIndex + ", entryIndex=" + entryIndex);

            // 完整复用 Duckov.UI.MapSelectionView.LoadTask 的进图模板：
            //   1) 设置 LevelManager.loadLevelBeaconIndex
            //   2) 等 0.5s 视觉确认
            //   3) SceneLoader.LoadScene(sceneID, null, clickToConinue: true)
            // 不手动 Close MapSelectionView，让 SceneLoader 的黑幕/加载流程接管 UI 关闭，
            // 否则提前 Close 会让 View.ActiveView 变 null，破坏后续场景切换流程。
            RunZombieModeSceneLoad(sceneID, beaconIndex).Forget();
        }

        private static async UniTask RunZombieModeSceneLoad(string sceneID, int beaconIndex)
        {
            try
            {
                LevelManager.loadLevelBeaconIndex = beaconIndex;
                ModBehaviour.DevLog("[ZombieMode] LevelManager.loadLevelBeaconIndex = " + beaconIndex);

                // 与 vanilla LoadTask 相同的 0.5s 视觉确认延迟，保证淡入/特效播放完毕。
                await UniTask.WaitForSeconds(0.5f, true);

                if (SceneLoader.Instance == null)
                {
                    ModBehaviour.DevLog("[ZombieMode] SceneLoader.Instance 为 null，加载失败");
                    ModBehaviour inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        inst.AbortZombieModeMapLoadPhase1(ZombieModeFailureReason.MapLoadFailed);
                    }
                    ClearPendingZombieEntry();
                    return;
                }

                // 与 vanilla 完全一致：overrideCurtainScene=null（fallback 到 SceneLoader.defaultCurtainScene），
                // clickToConinue=true（玩家在加载完成画面里点击继续，与 boat NPC 入图体验一致）。
                ModBehaviour.DevLog("[ZombieMode] 调用 SceneLoader.LoadScene(\"" + sceneID + "\", null, clickToConinue:true)");
                await SceneLoader.Instance.LoadScene(sceneID, null, true);
                ModBehaviour.DevLog("[ZombieMode] SceneLoader.LoadScene 已返回 (sceneID=" + sceneID + ")");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] RunZombieModeSceneLoad 异常: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
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
