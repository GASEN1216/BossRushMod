using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.UI;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(MapSelectionView), "WaitForConfirm")]
    internal static class BossRushInitialSpawnConfirmationPatch
    {
        [HarmonyPostfix]
        internal static void AfterConfirm(ref UniTask<bool> __result)
        {
            __result = Observe(__result);
        }

        internal static async UniTask<bool> Observe(UniTask<bool> confirmation)
        {
            bool confirmed = await confirmation;
            try { BossRushMapSelectionHelper.ConfirmInitialSpawnSelection(confirmed); }
            catch (Exception e) { Debug.LogWarning("[BossRush] 初始出生确认失败，保留原入场传送: " + e.Message); }
            return confirmed;
        }
    }

    // 仅持有当前 MultiSceneCore 的一次入场；随该关卡销毁，没有全局位置锁或逐帧工作。
    // 官方 InitLevel 先用 Location.position 创建玩家，再 LoadAndTeleport，最后再写一次 startPos。
    // 三处统一使用世界坐标；特殊 path 只供下面的配套补丁识别，不写官方缓存/存档。
    internal sealed class BossRushInitialSpawn : MonoBehaviour
    {
        internal const string LocationPath = "BossRush/InitialSpawn";
        internal string SceneId;
        internal Vector3 Position;
        internal bool PendingTeleport;
        internal bool Arrived;

        internal static bool HasArrived(Vector3 target)
        {
            var core = MultiSceneCore.Instance;
            var entry = core != null ? core.GetComponent<BossRushInitialSpawn>() : null;
            return entry != null && entry.Arrived && (entry.Position - target).sqrMagnitude < 0.01f;
        }

        internal async UniTask<bool> Load(MultiSceneCore core)
        {
            PendingTeleport = false;
            // 配置是世界坐标；true 会再经 MiniMapSettings 转换一次，地下图会落错位置。
            bool success = await core.LoadAndTeleport(SceneId, Position, false);
            if (this != null) Arrived = success;
            return success;
        }
    }

    [HarmonyPatch(typeof(LevelManager), "GetPlayerStartLocation")]
    internal static class BossRushInitialSpawnSelectionPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Select(LevelManager __instance, ref ValueTuple<string, SubSceneEntry.Location> __result)
        {
            try
            {
                var map = BossRushMapSelectionHelper.TakeInitialSpawnSelection();
                if (map == null || !SceneLoader.IsSceneLoading) return;
                var core = MultiSceneCore.Instance;
                var info = SceneInfoCollection.GetSceneInfo(map.sceneID);
                if (__instance == null || core == null || info == null || info.SceneReference == null
                    || !string.Equals(__instance.gameObject.scene.name, info.SceneReference.Name, StringComparison.Ordinal)) return;

                // 逐类安装可能部分失败。配套路由缺失时不能交给官方一个不存在的 path。
                var route = AccessTools.Method(typeof(MultiSceneCore), "LoadAndTeleport", new[] { typeof(MultiSceneLocation) });
                var prefix = AccessTools.Method(typeof(BossRushInitialSpawnTeleportPatch), "Route");
                var patches = route != null ? Harmony.GetPatchInfo(route) : null;
                if (patches == null || !patches.Prefixes.Any(p => p.PatchMethod == prefix))
                {
                    Debug.LogWarning("[BossRush] 初始出生路由未安装，保留原入场传送。");
                    return;
                }

                var subScene = core.SubScenes.FirstOrDefault(s => s != null && s.Info != null
                    && s.Info.SceneReference != null
                    && string.Equals(s.Info.SceneReference.Name, map.sceneName, StringComparison.Ordinal));
                if (subScene == null) return;
                var entry = core.GetComponent<BossRushInitialSpawn>() ?? core.gameObject.AddComponent<BossRushInitialSpawn>();
                entry.SceneId = subScene.sceneID;
                entry.Position = ModBehaviour.GetDefaultPositionForScene(map.sceneName);
                entry.PendingTeleport = true;
                entry.Arrived = false;
                __result = new ValueTuple<string, SubSceneEntry.Location>(entry.SceneId,
                    new SubSceneEntry.Location { path = BossRushInitialSpawn.LocationPath, position = entry.Position });
                ModBehaviour.DevLog("[BossRush] 初始出生使用竞技场目标: " + entry.SceneId + " " + entry.Position);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BossRush] 初始出生准备失败，保留原入场传送: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(MultiSceneCore), "LoadAndTeleport", new[] { typeof(MultiSceneLocation) })]
    internal static class BossRushInitialSpawnTeleportPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Route(MultiSceneCore __instance, MultiSceneLocation location, ref UniTask<bool> __result)
        {
            var entry = __instance.GetComponent<BossRushInitialSpawn>();
            if (entry == null || !entry.PendingTeleport
                || !string.Equals(location.LocationName, BossRushInitialSpawn.LocationPath, StringComparison.Ordinal)
                || !string.Equals(location.SceneID, entry.SceneId, StringComparison.Ordinal)) return true;
            __result = entry.Load(__instance);
            return false;
        }
    }
}
