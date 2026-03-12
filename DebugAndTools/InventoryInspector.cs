using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// DevMode 下按 F11 打开背包检查窗口，并把关键字段输出到 DevLog。
    /// </summary>
    public class InventoryInspector : MonoBehaviour
    {
        private bool windowVisible;
        private Vector2 scrollPosition;
        private Rect windowRect = new Rect(10f, 10f, 920f, 820f);

        private readonly List<ItemInfo> cachedItems = new List<ItemInfo>();
        private int cachedItemCount;
        private string statusMessage = string.Empty;

        private struct ItemInfo
        {
            public int index;
            public int typeID;
            public string displayName;
            public string displayNameKey;
            public string internalName;
            public string tags;
            public string category;
            public string quality;
            public string displayQuality;
            public string value;
            public string stackCount;
            public string durability;
            public string totalRawValue;
            public string caliber;
            public string routeHint;
            public string flags;
            public string descriptionPreview;
        }

        private void Update()
        {
            if (!ModBehaviour.DevModeEnabled)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                windowVisible = !windowVisible;
                if (windowVisible)
                {
                    RefreshInventory(true);
                }
            }
        }

        private void OnGUI()
        {
            if (!ModBehaviour.DevModeEnabled || !windowVisible)
            {
                return;
            }

            windowRect = GUI.Window(19870611, windowRect, DrawWindow, "背包物品详细信息 (F11)");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新背包", GUILayout.Height(28f)))
            {
                RefreshInventory(true);
            }

            if (GUILayout.Button("输出到日志", GUILayout.Height(28f)))
            {
                LogAllItemsToDevLog();
            }

            if (GUILayout.Button("关闭", GUILayout.Height(28f)))
            {
                windowVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(statusMessage);
            GUILayout.Space(5f);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            if (cachedItems.Count == 0)
            {
                GUILayout.Label("背包为空或无法读取。");
            }
            else
            {
                foreach (var info in cachedItems)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(string.Format("#{0}  {1}  (TypeID: {2})", info.index, info.displayName, info.typeID));
                    GUILayout.Label(string.Format("  DisplayNameKey: {0}", info.displayNameKey));
                    GUILayout.Label(string.Format("  InternalName: {0}", info.internalName));
                    GUILayout.Label(string.Format("  Tags: [{0}]", info.tags));
                    GUILayout.Label(string.Format("  Category: {0}   Flags: {1}", info.category, info.flags));
                    GUILayout.Label(string.Format("  Caliber: {0}   RouteHint: {1}", info.caliber, info.routeHint));
                    GUILayout.Label(string.Format("  Quality: {0}   DisplayQuality: {1}", info.quality, info.displayQuality));
                    GUILayout.Label(string.Format("  Value: {0}   TotalRawValue: {1}   StackCount: {2}", info.value, info.totalRawValue, info.stackCount));
                    GUILayout.Label(string.Format("  Durability: {0}", info.durability));
                    GUILayout.Label(string.Format("  Desc: {0}", info.descriptionPreview));
                    GUILayout.EndVertical();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 25f));
        }

        private void RefreshInventory(bool autoDumpToLog = false)
        {
            cachedItems.Clear();
            cachedItemCount = 0;
            statusMessage = string.Empty;

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
                    if (item == null)
                    {
                        continue;
                    }

                    ItemMetaData meta = ReadMeta(item);
                    var info = new ItemInfo
                    {
                        index = idx,
                        typeID = SafeRead(() => item.TypeID, -1),
                        displayName = SafeRead(() => item.DisplayName, "(null)"),
                        displayNameKey = SafeRead(() => meta.DisplayNameKey, "?"),
                        internalName = SafeRead(() => meta.Name, "?"),
                        tags = ReadTags(item),
                        category = SafeRead(() => meta.Catagory, "?"),
                        quality = SafeRead(() => item.Quality.ToString(), "?"),
                        displayQuality = SafeRead(() => item.DisplayQuality.ToString(), "?"),
                        value = SafeRead(() => item.Value.ToString(), "?"),
                        totalRawValue = SafeRead(() => item.GetTotalRawValue().ToString(), "?"),
                        stackCount = SafeRead(() => item.StackCount.ToString(), "?"),
                        durability = SafeRead(() => FormatDurability(item), "?"),
                        caliber = ReadCaliber(meta),
                        flags = ReadFlags(item),
                        descriptionPreview = ReadDescriptionPreview(meta)
                    };

                    info.routeHint = BuildRouteHint(info.caliber, info.flags, info.tags, info.category);

                    cachedItems.Add(info);
                    idx++;
                }

                cachedItemCount = idx;
                statusMessage = "共 " + cachedItemCount + " 个物品 (刷新时间: " + DateTime.Now.ToString("HH:mm:ss") + ")";
                ModBehaviour.DevLog("[InventoryInspector] 已刷新，共 " + cachedItemCount + " 个物品");
                if (autoDumpToLog)
                {
                    DumpCachedItemsToDevLog();
                }
            }
            catch (Exception e)
            {
                statusMessage = "读取异常: " + e.Message;
                ModBehaviour.DevLog("[InventoryInspector] 刷新异常: " + e);
            }
        }

        private void LogAllItemsToDevLog()
        {
            RefreshInventory(false);
            DumpCachedItemsToDevLog();

            try
            {
                Duckov.UI.NotificationText.Push("[InventoryInspector] 已输出 " + cachedItemCount + " 个物品到日志");
            }
            catch
            {
            }
        }

        private void DumpCachedItemsToDevLog()
        {
            ModBehaviour.DevLog("========== [InventoryInspector] 背包物品详细信息 ==========");
            foreach (var info in cachedItems)
            {
                ModBehaviour.DevLog(string.Format(
                    "[{0}] TypeID={1}, Name={2}, DisplayNameKey={3}, InternalName={4}, Tags=[{5}], Category={6}, Caliber={7}, RouteHint={8}, Flags={9}, Quality={10}, DisplayQuality={11}, Value={12}, TotalRawValue={13}, StackCount={14}, Durability={15}, Desc={16}",
                    info.index,
                    info.typeID,
                    info.displayName,
                    info.displayNameKey,
                    info.internalName,
                    info.tags,
                    info.category,
                    info.caliber,
                    info.routeHint,
                    info.flags,
                    info.quality,
                    info.displayQuality,
                    info.value,
                    info.totalRawValue,
                    info.stackCount,
                    info.durability,
                    info.descriptionPreview));
            }

            ModBehaviour.DevLog("========== 共 " + cachedItemCount + " 个物品 ==========");
        }

        private static ItemMetaData ReadMeta(Item item)
        {
            try
            {
                return new ItemMetaData(item);
            }
            catch
            {
                return default(ItemMetaData);
            }
        }

        private static string ReadTags(Item item)
        {
            try
            {
                var tags = item.Tags;
                if (tags == null || tags.Count == 0)
                {
                    return "(no tags)";
                }

                var names = new List<string>();
                foreach (var tag in tags)
                {
                    if (tag != null)
                    {
                        names.Add(tag.name);
                    }
                }

                return names.Count > 0 ? string.Join(", ", names.ToArray()) : "(empty tags)";
            }
            catch
            {
                return "(read tags failed)";
            }
        }

        private static string ReadCaliber(ItemMetaData meta)
        {
            try
            {
                return string.IsNullOrEmpty(meta.caliber) ? "(none)" : meta.caliber;
            }
            catch
            {
                return "?";
            }
        }

        private static string ReadFlags(Item item)
        {
            try
            {
                bool isBullet = item.GetBool("IsBullet", false);
                return isBullet ? "IsBullet" : "-";
            }
            catch
            {
                return "?";
            }
        }

        private static string ReadDescriptionPreview(ItemMetaData meta)
        {
            try
            {
                string description = meta.Description;
                if (string.IsNullOrEmpty(description))
                {
                    return "(none)";
                }

                description = description.Replace("\r", " ").Replace("\n", " ");
                if (description.Length > 120)
                {
                    return description.Substring(0, 120) + "...";
                }

                return description;
            }
            catch
            {
                return "(read description failed)";
            }
        }

        private static string BuildRouteHint(string caliber, string flags, string tags, string category)
        {
            bool looksLikeBullet =
                string.Equals(flags, "IsBullet", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(tags) && tags.IndexOf("Bullet", StringComparison.OrdinalIgnoreCase) >= 0) ||
                string.Equals(category, "Bullet", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeBullet)
            {
                return "-";
            }

            switch (caliber)
            {
                case "SMG":
                    return "BossGun:S";
                case "AR_S":
                    return "BossGun:AR";
                case "BR":
                    return "BossGun:L";
                case "Sniper":
                    return "BossGun:Sniper";
                case "ShotGun":
                    return "BossGun:Shotgun";
                case "Rocket":
                    return "BossGun:Rocket";
                case "(none)":
                    return "SpecialBullet:NoCaliber";
                default:
                    return "SpecialBullet:" + caliber;
            }
        }

        private static string FormatDurability(Item item)
        {
            float cur = item.Durability;
            float max = item.MaxDurability;
            return string.Format("{0:F1} / {1:F1}", cur, max);
        }

        private static string SafeRead(Func<string> getter, string fallback)
        {
            try
            {
                string value = getter();
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private static int SafeRead(Func<int> getter, int fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
