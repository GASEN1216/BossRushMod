// ============================================================================
// InventoryInspector.cs - 背包物品详细信息查看工具
// ============================================================================
// 模块说明：
//   DevMode 下按 F11 打开/关闭背包物品检查窗口。
//   显示每个物品的 TypeID、名称、Tags、品质、价值、堆叠数、耐久等详细信息，
//   同时将完整信息输出到日志。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 背包物品详细信息检查工具（DevMode 下 F11 打开）
    /// </summary>
    public class InventoryInspector : MonoBehaviour
    {
        private bool windowVisible = false;
        private Vector2 scrollPosition;
        private Rect windowRect = new Rect(10, 10, 620, 700);

        private List<ItemInfo> cachedItems = new List<ItemInfo>();
        private int cachedItemCount = 0;
        private string statusMessage = "";

        private struct ItemInfo
        {
            public int index;
            public int typeID;
            public string displayName;
            public string tags;
            public string quality;
            public string displayQuality;
            public string value;
            public string stackCount;
            public string durability;
            public string totalRawValue;
        }

        void Update()
        {
            if (!ModBehaviour.DevModeEnabled) return;

            if (Input.GetKeyDown(KeyCode.F11))
            {
                windowVisible = !windowVisible;
                if (windowVisible)
                {
                    RefreshInventory();
                }
            }
        }

        void OnGUI()
        {
            if (!ModBehaviour.DevModeEnabled || !windowVisible) return;
            windowRect = GUI.Window(19870611, windowRect, DrawWindow, "背包物品详细信息 (F11) - 可拖动");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新背包", GUILayout.Height(28)))
            {
                RefreshInventory();
            }
            if (GUILayout.Button("输出到日志", GUILayout.Height(28)))
            {
                LogAllItemsToDevLog();
            }
            if (GUILayout.Button("关闭", GUILayout.Height(28)))
            {
                windowVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(statusMessage);
            GUILayout.Space(5);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            if (cachedItems.Count == 0)
            {
                GUILayout.Label("背包为空或无法读取");
            }
            else
            {
                foreach (var info in cachedItems)
                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    GUILayout.Label(string.Format("#{0}  {1}  (TypeID: {2})",
                        info.index, info.displayName, info.typeID));

                    GUILayout.Label(string.Format(
                        "  Tags: [{0}]", info.tags));
                    GUILayout.Label(string.Format(
                        "  Quality: {0}   DisplayQuality: {1}", info.quality, info.displayQuality));
                    GUILayout.Label(string.Format(
                        "  Value: {0}   TotalRawValue: {1}   StackCount: {2}", info.value, info.totalRawValue, info.stackCount));
                    GUILayout.Label(string.Format(
                        "  Durability: {0}", info.durability));

                    GUILayout.EndVertical();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 25));
        }

        private void RefreshInventory()
        {
            cachedItems.Clear();
            cachedItemCount = 0;
            statusMessage = "";

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null)
                {
                    statusMessage = "玩家或 CharacterItem 为空";
                    return;
                }

                var inventory = player.CharacterItem.Inventory;
                if (inventory == null)
                {
                    statusMessage = "背包 Inventory 为空";
                    return;
                }

                int idx = 0;
                foreach (var item in inventory)
                {
                    if (item == null) continue;

                    var info = new ItemInfo();
                    info.index = idx;

                    try { info.typeID = item.TypeID; } catch { info.typeID = -1; }
                    try { info.displayName = item.DisplayName ?? "(null)"; } catch { info.displayName = "(读取失败)"; }
                    info.tags = ReadTags(item);
                    info.quality = SafeRead(() => item.Quality.ToString(), "?");
                    info.displayQuality = SafeRead(() => item.DisplayQuality.ToString(), "?");
                    info.value = SafeRead(() => item.Value.ToString(), "?");
                    info.totalRawValue = SafeRead(() => item.GetTotalRawValue().ToString(), "?");
                    info.stackCount = SafeRead(() => item.StackCount.ToString(), "?");
                    info.durability = SafeRead(() =>
                    {
                        float cur = item.Durability;
                        float max = item.MaxDurability;
                        return string.Format("{0:F1} / {1:F1}", cur, max);
                    }, "?");

                    cachedItems.Add(info);
                    idx++;
                }

                cachedItemCount = idx;
                statusMessage = "共 " + cachedItemCount + " 个物品  (刷新时间: " + DateTime.Now.ToString("HH:mm:ss") + ")";

                ModBehaviour.DevLog("[InventoryInspector] 已刷新，共 " + cachedItemCount + " 个物品");
            }
            catch (Exception e)
            {
                statusMessage = "读取异常: " + e.Message;
                ModBehaviour.DevLog("[InventoryInspector] 刷新异常: " + e.Message);
            }
        }

        private void LogAllItemsToDevLog()
        {
            RefreshInventory();

            ModBehaviour.DevLog("========== [InventoryInspector] 背包物品详细信息 ==========");
            foreach (var info in cachedItems)
            {
                ModBehaviour.DevLog(string.Format(
                    "[{0}] TypeID={1}, Name={2}, Tags=[{3}], Quality={4}, DisplayQuality={5}, Value={6}, TotalRawValue={7}, StackCount={8}, Durability={9}",
                    info.index, info.typeID, info.displayName, info.tags,
                    info.quality, info.displayQuality, info.value, info.totalRawValue,
                    info.stackCount, info.durability));
            }
            ModBehaviour.DevLog("========== 共 " + cachedItemCount + " 个物品 ==========");

            try
            {
                Duckov.UI.NotificationText.Push("[InventoryInspector] 已输出 " + cachedItemCount + " 个物品到日志");
            }
            catch { }
        }

        private static string ReadTags(Item item)
        {
            try
            {
                var tags = item.Tags;
                if (tags == null || tags.Count == 0) return "(无Tags)";

                var names = new List<string>();
                foreach (var tag in tags)
                {
                    if (tag != null) names.Add(tag.name);
                }
                return names.Count > 0 ? string.Join(", ", names.ToArray()) : "(Tags为空集合)";
            }
            catch
            {
                return "(读取Tags失败)";
            }
        }

        private static string SafeRead(Func<string> getter, string fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }
    }
}
