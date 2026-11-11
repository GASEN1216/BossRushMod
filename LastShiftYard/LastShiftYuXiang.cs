using System;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 余响：剪票处台边的捏脸 NPC（蓝图 `lastshift_yuxiang`，`Assets/Data/DuckNpcs.json`，非永久、不进好感与婚姻）。
    /// 跟着剪票处一起生成与回收（scenes 为空，通用刷新不会碰她）。第七轮 owner 采纳的入口引导也在这里：
    /// 靠近时喊一声「末班车，剪票上车」（每趟一次）、没去过编组站时仓库区小地图标出台子、每趟塞一卷台边绷带。
    /// </summary>
    internal static class LastShiftYuXiang
    {
        internal const string BlueprintId = "lastshift_yuxiang";
        /// <summary>台的东侧 2 m（2026-11-07 离线选址：余响 (150, 0, 245)），面朝台子；站位与朝向待实机复核。</summary>
        internal static readonly Vector3 Position = new Vector3(150f, 0f, 245f);
        private static readonly Vector3 Facing = Vector3.left;
        private const float CalloutRadius = 9f;
        private const string MapKey = "BossRush_LastShiftYard_MapDesk";
        private static int generation;
        private static bool spawning;
        private static float retryAt;
        private static CharacterMainControl owned;
        private static uint calledRaid, bandageRaid;
        private static int bandageTypeId = -2;

        internal static void Spawn(Scene scene)
        {
            if (spawning || owned != null || Time.unscaledTime < retryAt) return;
            int request = ++generation;
            SpawnAsync(scene, request).Forget();
        }

        private static async UniTaskVoid SpawnAsync(Scene scene, int request)
        {
            spawning = true;
            try
            {
                CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(BlueprintId, Position, Facing);
                if (npc == null) { retryAt = Time.unscaledTime + 30f; return; }
                owned = npc;
                if (request != generation || !scene.IsValid() || !scene.isLoaded) { Despawn(); return; }
                // 跟着仓库区子场景一起卸载（口径同潮闸镇闸伯）。
                if (npc.transform.parent == null && npc.gameObject.scene.handle != scene.handle)
                    SceneManager.MoveGameObjectToScene(npc.gameObject, scene);
                Debug.Log(LastShiftRaidScene.LogTag + " YUXIANG_READY at=" + npc.transform.position);
            }
            catch (Exception e) { retryAt = Time.unscaledTime + 30f; Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响没能生成，30 秒后再试：" + e.Message); }
            finally { spawning = false; }
        }

        internal static void Despawn()
        {
            generation++;
            CharacterMainControl npc = owned;
            owned = null;
            if (npc == null) return;
            try { DuckNpcSpawner.Despawn(npc); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响回收失败：" + e.Message); }
        }

        /// <summary>模块每秒调：玩家走近台子时喊一声，每趟出击一次。</summary>
        internal static void Tick(CharacterMainControl player)
        {
            if (owned == null || player == null) return;
            uint raid = RaidUtilities.CurrentRaid.ID;
            if (raid == calledRaid) return;
            Vector3 offset = player.transform.position - owned.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > CalloutRadius * CalloutRadius) return;
            calledRaid = raid;
            try { DialogueBubblesManager.Show(LastShiftText.YuXiangCallout, owned.transform, 3f, false, false, -1f, 1.2f).Forget(); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响喊话失败：" + e.Message); }
        }

        /// <summary>没去过编组站时在仓库区小地图标出台子（第七轮：「第一次进仓库区时在小地图标出余响的台子」）。根节点激活前加、激活后 Setup。</summary>
        internal static GameObject AddMapMarker(Transform root)
        {
            if (LastShiftStory.Runs > 0) return null;
            LocalizationHelper.InjectLocalization(MapKey, L10n.T("末班车剪票处", "Last train ticket desk"));
            var marker = new GameObject("LastShiftMapMarker");
            marker.transform.SetParent(root, false);
            SimplePointOfInterest poi = marker.AddComponent<SimplePointOfInterest>();
            poi.Color = BossRushUIColors.WarningText;
            poi.ScaleFactor = 1.25f;
            poi.IsArea = false;
            return marker;
        }

        internal static void SetupMapMarker(GameObject marker)
        {
            if (marker == null) return;
            SimplePointOfInterest poi = marker.GetComponent<SimplePointOfInterest>();
            if (poi != null) poi.Setup(null, MapKey);
        }

        /// <summary>台边绷带箱：每趟出击一卷，剪票时塞给你（第七轮：带伤上车回不去，台边先给一卷）。返回给没给成。</summary>
        internal static bool GiveBandage(CharacterMainControl player)
        {
            uint raid = RaidUtilities.CurrentRaid.ID;
            if (player == null || raid == bandageRaid) return false;
            int typeId = BandageTypeId();
            if (typeId <= 0) return false;
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null) return false;
                ItemUtilities.SendToPlayer(item, false, false);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 绷带投递通知：" + e.Message); }
            if (item == null) return false;
            if (item.InInventory == null && item.PluggedIntoSlot == null)
            {
                try { item.Drop(player, true); }
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 绷带落地失败：" + e.Message); return false; }
            }
            bandageRaid = raid;
            return true;
        }

        /// <summary>官方止血绷带按本地化键 `Item_Bandage` 查（不硬编码官方 TypeID）；查不到就不发，记一次日志。</summary>
        private static int BandageTypeId()
        {
            if (bandageTypeId != -2) return bandageTypeId;
            bandageTypeId = -1;
            try
            {
                ItemAssetsCollection collection = ItemAssetsCollection.Instance;
                if (collection != null && collection.entries != null)
                    foreach (ItemAssetsCollection.Entry entry in collection.entries)
                        if (entry != null && entry.metaData.DisplayNameKey == "Item_Bandage") { bandageTypeId = entry.typeID; break; }
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 查官方绷带失败：" + e.Message); }
            if (bandageTypeId <= 0) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 官方物品表里找不到 Item_Bandage，台边绷带箱本次不发"); bandageTypeId = -1; }
            return bandageTypeId;
        }

        internal static void ResetStaticCaches()
        {
            Despawn();
            spawning = false;
            retryAt = 0f;
            calledRaid = bandageRaid = 0;
            bandageTypeId = -2;
        }
    }
}
