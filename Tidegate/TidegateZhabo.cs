using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 闸伯：永久捏脸 NPC（蓝图 `tidegate_zhabo`，`Assets/Data/DuckNpcs.json`），站在地堡下水道走道上、他的竹筏旁边。
    /// 蓝图的 scenes 写的是 `TidegateRaid`，通用刷新与永久 NPC 刷新都不会碰他；由竹筏入口跟着下水道子场景生成与回收
    /// （口径同天空岛居民：`PermanentDuckNpcModule.ForceSpawnAtAsync`）。好感、送礼、商店与剧情对话走永久 NPC 的通用件。
    /// </summary>
    internal static class TidegateZhabo
    {
        internal const string BlueprintId = "tidegate_zhabo";
        /// <summary>
        /// 下水道西侧走道上、竹筏交互点南边约 6.6 m（离井盖交互约 5.6 m、钓鱼笔记约 6.6 m），面朝来人的南边。
        /// 官方按离玩家最近只选一个交互目标，闸伯的交互体半径 0.6 m，隔开这么远就不会和竹筏上的两项抢。
        /// </summary>
        internal static readonly Vector3 Position = new Vector3(96.2f, 0.02f, -45.5f);
        private static int generation;
        private static bool spawning;
        /// <summary>本模块生成的那一个。只回收它：婚姻等别的 owner 在别处生成的闸伯不归这里管。</summary>
        private static CharacterMainControl owned;

        internal static void Spawn(Scene scene)
        {
            if (spawning || owned != null || PermanentDuckNpcRegistry.GetInstance(BlueprintId) != null) return;
            // 结过婚的闸伯归婚姻 owner（教堂站桩 / 跟随），不在竹筏边再刷一只。
            if (AffinityManager.IsMarriedToPlayer(BlueprintId)) return;
            int request = ++generation;
            SpawnAsync(scene, request).Forget();
        }

        private static async UniTaskVoid SpawnAsync(Scene scene, int request)
        {
            spawning = true;
            try
            {
                PermanentDuckNpcModule.RegisterAllAffinityConfigs();
                CharacterMainControl npc = await PermanentDuckNpcModule.ForceSpawnAtAsync(BlueprintId, Position, true,
                    delegate { return request == generation && scene.IsValid() && scene.isLoaded && TidegateStory.RouteUnlocked; });
                if (npc == null) return;
                owned = npc;
                if (request != generation || !scene.IsValid() || !scene.isLoaded)
                {
                    Despawn();
                    return;
                }
                // 跟着下水道子场景一起卸载：玩家走出下水道时官方卸掉这张子场景，闸伯不能留在主城的同一坐标上。
                if (npc.transform.parent == null && npc.gameObject.scene.handle != scene.handle)
                    SceneManager.MoveGameObjectToScene(npc.gameObject, scene);
                Debug.Log(TidegateRaidScene.LogTag + " ZHABO_READY at=" + Position);
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 闸伯没能生成：" + e.Message); }
            finally { spawning = false; }
        }

        internal static void Despawn()
        {
            generation++;
            CharacterMainControl npc = owned;
            owned = null;
            if (npc == null) return;
            try
            {
                bool registered = PermanentDuckNpcRegistry.GetInstance(BlueprintId) == npc;
                DuckNpcSpawner.Despawn(npc);
                if (registered) PermanentDuckNpcRegistry.UnregisterInstance(BlueprintId);
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 闸伯回收失败：" + e.Message); }
        }
    }
}
