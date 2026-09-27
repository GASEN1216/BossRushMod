using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    public static partial class ModeHLoadoutKitRegistry
    {
        // 每位候选独立随机流；只读官方槽位和物品元数据，预览与上场复用返回的计划。
        internal static List<ModeHResolvedKit> BuildPreparedKits(long seed, string identity)
        {
            List<ModeHResolvedKit> result = new List<ModeHResolvedKit>();
            Item template = ItemAssetsCollection.GetPrefab(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            if (template == null || template.Slots == null) return result;
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, "prepared_outfit|" + identity, 0);
            HashSet<int> exclusiveIds = new HashSet<int>();
            foreach (Slot slot in template.Slots)
            {
                if (slot == null || slot.requireTags == null || slot.requireTags.Count == 0) continue;
                int[] ids = ItemAssetsCollection.GetAllTypeIds(new ItemFilter
                {
                    requireTags = slot.requireTags.ToArray(), excludeTags = slot.excludeTags != null ? slot.excludeTags.ToArray() : null,
                    minQuality = 1, maxQuality = 6,
                });
                if (ids == null || ids.Length == 0) continue;
                Array.Sort(ids);
                int start = stream.NextInt(ids.Length);
                for (int offset = 0; offset < ids.Length; offset++)
                {
                    int id = ids[(start + offset) % ids.Length];
                    // 基础配装使用官方完整物品，防止自定义主动能力在临时角色上建立第二个 owner。
                    if (id <= 0 || id >= 500001) continue;
                    Item item = ItemAssetsCollection.GetPrefab(id);
                    if (item == null || item.TypeID != id || item.name == "FallbackItem" || item.Sticky
                        || item.GetStatValue("ControlMindType".GetHashCode()) != 0f) continue;
                    if (slot.ForbidItemsWithSameID && exclusiveIds.Contains(id)) continue;
                    if (!slot.CanPlug(item)) continue;
                    bool gunSlot = slot.Key == "PrimaryWeapon" || slot.Key == "SecondaryWeapon";
                    ItemSetting_Gun gun = item.GetComponent<ItemSetting_Gun>();
                    int ammoTypeId = gunSlot ? ResolvePreparedAmmoTypeId(item, gun) : 0;
                    if (gunSlot && ammoTypeId <= 0) continue;
                    ModeHKitSpec spec = new ModeHKitSpec
                    {
                        KitId = "prepared_" + slot.Key + "_" + id,
                        ReplaceSlot = slot.Key, TypeId = id, GameQuality = item.Quality,
                        AmmoTypeId = ammoTypeId,
                        AmmoCount = gunSlot ? 240 : 0,
                        NameKey = item.DisplayNameRaw,
                    };
                    result.Add(new ModeHResolvedKit
                    { Spec = spec, ResolvedTypeId = id, ResolvedQuality = item.Quality, Available = true });
                    if (slot.ForbidItemsWithSameID) exclusiveIds.Add(id);
                    break;
                }
            }
            return result;
        }
        // 默认 TargetBulletID=-1 的新枪仍是有效武器；弹药选择只在候选实际准备时发生。
        internal static int ResolvePreparedAmmoTypeId(Item weapon, ItemSetting_Gun gun)
        {
            if (weapon == null || gun == null) return 0;
            Item current = gun.TargetBulletID > 0 ? ItemAssetsCollection.GetPrefab(gun.TargetBulletID) : null;
            if (current != null && current.TypeID == gun.TargetBulletID && gun.IsValidBullet(current))
                return current.TypeID;
            int[] candidates = ItemAssetsCollection.GetAllTypeIds(new ItemFilter
            {
                requireTags = new[] { GameplayDataSettings.Tags.Bullet },
                minQuality = 1, maxQuality = 6,
                caliber = weapon.Constants != null ? weapon.Constants.GetString("Caliber", null) : null,
            });
            if (candidates == null) return 0;
            Array.Sort(candidates);
            // 固定首个兼容类型；最终 ID 写入 kit，预览和实战不各自再抽一次。
            foreach (int id in candidates)
            {
                if (id <= 0 || id >= 500001) continue;
                Item bullet = ItemAssetsCollection.GetPrefab(id);
                if (bullet != null && bullet.TypeID == id && gun.IsValidBullet(bullet)) return id;
            }
            return 0;
        }
    }
}
