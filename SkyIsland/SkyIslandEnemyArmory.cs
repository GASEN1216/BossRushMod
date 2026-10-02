using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛敌人的武器配装（owner 2026-09-30：岛上敌人至少品质 3，头目 / 岛主至少品质 5）。
    ///
    /// 官方 `CreateCharacterAsync` 装配完成后调用一次：主武器与近战低于本档下限就换成同槽位的官方武器；
    /// 达标的官方原装不动，空槽不补（底模是官方 Boss，空着主武器槽的是近战 Boss）。品质带见 <see cref="SkyIslandEnemyArmoryRules"/>。
    ///
    /// - 官方与其它 Mod 的武器都进池（owner 2026-09-30），口径不限；只排除本 Mod 自己的 500xxx 物品
    ///   （<see cref="SkyIslandEnemyArmoryRules.IsOwnModItem"/>），过官方掉落排除标签、全局黑名单与岛上物资池的单件价值上限；
    ///   控心武器、粘手物品不发。枪必须有弹匣、有同口径弹药——没弹药的枪在 AI 手里打不响。
    /// - 换上的枪装满弹匣；**手里每一把枪**（换上的、达标留下的官方原装、副武器）背包里都补足
    ///   <see cref="SkyIslandEnemyArmoryRules.ReserveRounds"/> 发同款弹药（<see cref="EnsureAmmo"/>）。
    ///   官方 `AddBullet` 只按 preset 的弹药品质表随机给一堆，Boss 预设的高品质表在不少口径上抽不到弹，
    ///   留下的原装枪于是一发备弹都没有，打空一匣就原地喊「没子弹了」反复换弹（owner 2026-10-01 实测）。
    ///   弹药品质不低于本档 `AmmoMin`，没有就退回这把枪能用的最高品质弹药；已经装着 / 背着同口径弹药时沿用那一种。
    ///   退回那一步**不设品质上限**：官方 Boss 专属枪（Alex、蝇蝇队长、机械雪人手里的那几把，品质码 9）的弹药品质码也可能在 7 以上，
    ///   旧写法只查 1–7，日志里就是「这把枪找不到可用弹药」，达标留下的专属枪一发都打不出来（owner 2026-10-02 实测；
    ///   钟守的守钟装置底模就是机械雪人）。弹药品质码的实际分布没有离线核对，UNVERIFIED，所以下一条兜底必须在。
    /// - 达标的原装枪若怎么都找不到同口径弹药，按低于下限处理、换一把本档的枪（<see cref="IsDryGun"/>）：打不响的枪比低品质的枪更糟。
    /// - 长战不断粮：配完弹的敌人挂 <see cref="SkyIslandAmmoKeeper"/>，每 3 秒看一眼，背包里同款备弹不足一匣就补回
    ///   <see cref="SkyIslandEnemyArmoryRules.ReserveRounds"/> 发；倒下后不再补，尸体箱里最多就是这一份。
    /// - 被换下的官方随机武器直接销毁（它不在敌人手里，不该出现在尸体箱里）；换上的武器随官方尸体箱掉落。
    /// - 可失败的装饰：池子为空或任一步失败时保留官方原装，照常参战。
    ///
    /// 候选池首次用到才建，进岛读条时由 <see cref="SkyIslandLootPools.Prewarm"/> 顺带预热；只有完整跑完的查询才缓存，
    /// 缓存只在模块销毁时复位（<see cref="ResetStaticCaches"/>）。
    /// </summary>
    internal static class SkyIslandEnemyArmory
    {
        private const string PrimarySlot = "PrimaryWeapon";
        private const string SecondarySlot = "SecondaryWeapon";
        private const string MeleeSlot = "MeleeWeapon";
        private const int MaxQuality = 7;
        /// <summary>退回「这把枪能用的任何弹药」时的品质上限：官方弹药的品质码有 9 以上的（见类注释）。</summary>
        private const int AnyAmmoMaxQuality = 99;
        private static readonly int ControlMindTypeHash = "ControlMindType".GetHashCode();
        // 官方同步写入弹匣不会让这份缓存失效；置 -1 后由公开 getter 重算（口径同 ModeHLoadoutKitApplicator）。
        private static readonly FieldInfo GunBulletCountCacheField =
            typeof(ItemSetting_Gun).GetField("_bulletCountCache", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly int[] Empty = new int[0];

        private static readonly Dictionary<string, int[]> weaponPools = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int[]> ammoPools = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

        internal static void Arm(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            Arm(character, SkyIslandEnemyArmoryRules.For(tier), tier.ToString(), SkyIslandEnemyArmoryRules.IsBossTier(tier));
        }

        internal static void ArmPatrol(CharacterMainControl character, int rank)
        {
            Arm(character, SkyIslandEnemyArmoryRules.ForPatrol(rank), "Patrol" + rank, false);
        }

        private static void Arm(CharacterMainControl character, SkyIslandWeaponBand band, string label, bool announce)
        {
            if (character == null) return;
            try
            {
                Item body = character.CharacterItem;
                if (body == null) return;
                System.Random random = new System.Random(unchecked(Environment.TickCount ^ character.GetInstanceID()));
                string fromKey = character.characterPreset != null ? character.characterPreset.nameKey : null;
                bool changed = EnsureWeapon(character, body, PrimarySlot, band, random, fromKey, label);
                changed |= EnsureWeapon(character, body, MeleeSlot, band, random, fromKey, label);
                // 换没换都补弹：达标留下的官方原装与副武器同样要有备弹（见类注释）。
                int primaryAmmo = EnsureAmmo(body, PrimarySlot, band, random, fromKey, label);
                int secondaryAmmo = EnsureAmmo(body, SecondarySlot, band, random, fromKey, label);
                if (primaryAmmo > 0 || secondaryAmmo > 0)
                    character.gameObject.AddComponent<SkyIslandAmmoKeeper>().Bind(character, primaryAmmo, secondaryAmmo, fromKey);
                if (changed) character.SwitchToFirstAvailableWeapon();
                string summary = "[SkyIslandArmory] ARMED tier=" + label + " primary=" + Describe(body, PrimarySlot)
                    + " melee=" + Describe(body, MeleeSlot) + " changed=" + changed;
                if (announce) Debug.Log(summary);
                else ModBehaviour.DevLog(summary);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandArmory] 配枪失败（官方原装参战）：" + label + " " + e.Message); }
        }

        /// <summary>这个槽位的武器低于本档下限（或主武器槽是空的）就换一把；返回是否换了。</summary>
        private static bool EnsureWeapon(CharacterMainControl character, Item body, string slotKey, SkyIslandWeaponBand band,
            System.Random random, string fromKey, string label)
        {
            Slot slot = SkyIslandBossForge.FindSlot(body, slotKey);
            if (slot == null) return false;
            Item current = slot.Content;
            if (current != null && band.Keeps(current.Quality) && !IsDryGun(body, current)) return false;
            // 空槽不补（2026-10-01 起底模全是官方 Boss）：主武器槽空着说明这位是近战 Boss（Tagilla、Killa、校霸），
            // 它的 AI 只会冲上来砍，塞一把枪进去它也不开，还会被 SwitchToFirstAvailableWeapon 换到手上、把锤子收起来。
            if (current == null) return false;
            int[] pool = Pool(slotKey, band.Min, band.Max);
            if (pool.Length == 0) pool = Pool(slotKey, band.Floor, band.Max);
            if (pool.Length == 0)
            {
                WarnOnce("empty|" + slotKey + "|" + band.Floor + "|" + band.Max,
                    "[SkyIslandArmory] 候选池为空，保留官方原装：" + slotKey + " q" + band.Floor + "-" + band.Max);
                return false;
            }
            Item created = null;
            try
            {
                created = ItemAssetsCollection.InstantiateSync(pool[random.Next(pool.Length)]);
                if (created == null || !slot.CanPlug(created)) throw new InvalidOperationException("实例化或槽位校验失败");
                if (!string.IsNullOrEmpty(fromKey)) created.FromInfoKey = fromKey;
                ItemSetting_Gun gun = created.GetComponent<ItemSetting_Gun>();
                int ammoTypeId = gun != null ? LoadMagazine(created, gun, band, random, fromKey) : 0;
                if (gun != null && ammoTypeId <= 0) throw new InvalidOperationException("没有可用弹药");
                if (current != null)
                {
                    // 正拿在手上的先放下：官方不会替已销毁的物品收回手持代理。
                    if (character.CurrentHoldItemAgent != null && character.CurrentHoldItemAgent.Item == current)
                        character.agentHolder.ChangeHoldItem(null);
                    current.Detach();
                    current.DestroyTree();
                }
                Item unplugged;
                if (!slot.Plug(created, out unplugged)) throw new InvalidOperationException("插槽失败");
                if (unplugged != null) unplugged.DestroyTree();
                created = null;
                // 备弹由 Arm 里随后的 EnsureAmmo 按弹匣里这一种补足，这里不另放。
                return true;
            }
            catch (Exception e)
            {
                WarnOnce("fail|" + label + "|" + slotKey + "|" + e.Message, "[SkyIslandArmory] 换武器失败：" + label + " " + slotKey + " " + e.Message);
                if (created != null) created.DestroyTree();
                return false;
            }
        }

        /// <summary>这把枪弹匣里、背包里都没有它能装的弹药，全游戏也找不到同口径弹药（AI 拿着它一发都打不出来）。</summary>
        private static bool IsDryGun(Item body, Item weapon)
        {
            try
            {
                ItemSetting_Gun gun = weapon.GetComponent<ItemSetting_Gun>();
                if (gun == null) return false;
                Item loaded = gun.GetCurrentLoadedBullet();
                if (loaded != null && IsValidBullet(gun, loaded.TypeID)) return false;
                if (body.Inventory != null && CarriedAmmo(body.Inventory, gun) > 0) return false;
                string caliber = Caliber(weapon);
                if (string.IsNullOrEmpty(caliber)) return true;
                int[] any = AmmoPool(caliber, 1, AnyAmmoMaxQuality);
                for (int i = 0; i < any.Length; i++)
                    if (IsValidBullet(gun, any[i])) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 这个槽位上若是枪：弹匣空着就装满，背包里同款弹药补到 <see cref="SkyIslandEnemyArmoryRules.ReserveRounds"/> 发。
        /// 弹种优先沿用弹匣里已装的、其次背包里已有的同口径弹药（官方 AddBullet 给的），都没有才按本档品质另挑。
        /// 只加不减，可失败：任一步失败保留现状并记一次警告。返回补的弹种（不是枪、或没补上时为 0），给 <see cref="SkyIslandAmmoKeeper"/> 续弹用。
        /// </summary>
        private static int EnsureAmmo(Item body, string slotKey, SkyIslandWeaponBand band, System.Random random, string fromKey, string label)
        {
            try
            {
                Slot slot = SkyIslandBossForge.FindSlot(body, slotKey);
                Item weapon = slot != null ? slot.Content : null;
                ItemSetting_Gun gun = weapon != null ? weapon.GetComponent<ItemSetting_Gun>() : null;
                if (gun == null || body.Inventory == null) return 0;
                int ammoTypeId = 0;
                Item loaded = gun.GetCurrentLoadedBullet();
                if (loaded != null && IsValidBullet(gun, loaded.TypeID)) ammoTypeId = loaded.TypeID;
                if (ammoTypeId <= 0) ammoTypeId = CarriedAmmo(body.Inventory, gun);
                if (ammoTypeId <= 0) ammoTypeId = PickAmmo(weapon, gun, band.AmmoMin, random);
                if (ammoTypeId <= 0)
                {
                    WarnOnce("noammo|" + weapon.TypeID, "[SkyIslandArmory] 这把枪找不到可用弹药：" + label + " " + slotKey + " " + weapon.TypeID
                        + " caliber=" + Caliber(weapon));
                    return 0;
                }
                if (loaded == null) FillMagazine(weapon, gun, ammoTypeId, fromKey);
                TopUpReserve(body, gun, ammoTypeId, fromKey);
                return ammoTypeId;
            }
            catch (Exception e)
            {
                WarnOnce("ammo|" + label + "|" + slotKey + "|" + e.Message, "[SkyIslandArmory] 补弹失败：" + label + " " + slotKey + " " + e.Message);
                return 0;
            }
        }

        /// <summary>背包里这一种备弹补到 <see cref="SkyIslandEnemyArmoryRules.ReserveRounds"/> 发（只加不减）。</summary>
        internal static void TopUpReserve(Item body, ItemSetting_Gun gun, int ammoTypeId, string fromKey)
        {
            if (body == null || body.Inventory == null || gun == null || ammoTypeId <= 0) return;
            int need = SkyIslandEnemyArmoryRules.ReserveRounds(gun.Capacity) - CountInInventory(body.Inventory, ammoTypeId);
            if (need > 0) StoreReserve(body, ammoTypeId, need, fromKey);
        }

        /// <summary>背包里这一种备弹还有多少发。</summary>
        internal static int ReserveOf(Item body, int ammoTypeId)
        {
            return body == null || body.Inventory == null ? 0 : CountInInventory(body.Inventory, ammoTypeId);
        }

        /// <summary>背包里已有的、这把枪能装的弹药（官方 AddBullet 给的那一堆）；没有返回 0。</summary>
        private static int CarriedAmmo(Inventory inventory, ItemSetting_Gun gun)
        {
            foreach (Item item in inventory)
                if (item != null && item.GetBool("IsBullet", false) && gun.IsValidBullet(item)) return item.TypeID;
            return 0;
        }

        private static int CountInInventory(Inventory inventory, int typeId)
        {
            int count = 0;
            foreach (Item item in inventory)
                if (item != null && item.TypeID == typeId) count += item.Stackable ? item.StackCount : 1;
            return count;
        }

        /// <summary>选弹药、装满弹匣；返回选中的弹药 TypeID，这把枪没有可用弹药时返回 0。</summary>
        private static int LoadMagazine(Item weapon, ItemSetting_Gun gun, SkyIslandWeaponBand band, System.Random random, string fromKey)
        {
            int ammoTypeId = PickAmmo(weapon, gun, band.AmmoMin, random);
            if (ammoTypeId <= 0) return 0;
            FillMagazine(weapon, gun, ammoTypeId, fromKey);
            return ammoTypeId;
        }

        /// <summary>按指定弹种把弹匣装满（容器或容量还没就绪时什么都不做，由官方换弹从背包装填）。</summary>
        private static void FillMagazine(Item weapon, ItemSetting_Gun gun, int ammoTypeId, string fromKey)
        {
            gun.SetTargetBulletType(ammoTypeId);
            // 弹匣容器或容量在实例上晚一步才就绪：只放背包备弹，由官方换弹装填（口径同 ModeHLoadoutKitApplicator）。
            if (weapon.Inventory == null || gun.Capacity <= 0) return;
            int remaining = gun.Capacity;
            while (remaining > 0)
            {
                Item ammo = ItemAssetsCollection.InstantiateSync(ammoTypeId);
                if (ammo == null) break;
                int stack = Math.Min(remaining, Math.Max(1, ammo.MaxStackCount));
                ammo.StackCount = stack;
                if (!string.IsNullOrEmpty(fromKey)) ammo.FromInfoKey = fromKey;
                if (!gun.IsValidBullet(ammo) || !weapon.Inventory.AddItem(ammo)) { ammo.DestroyTree(); break; }
                remaining -= stack;
            }
            if (GunBulletCountCacheField != null) GunBulletCountCacheField.SetValue(gun, -1);
        }

        private static void StoreReserve(Item body, int ammoTypeId, int count, string fromKey)
        {
            Inventory inventory = body.Inventory;
            if (inventory == null) return;
            while (count > 0)
            {
                Item ammo = ItemAssetsCollection.InstantiateSync(ammoTypeId);
                if (ammo == null) return;
                int stack = Math.Min(count, Math.Max(1, ammo.MaxStackCount));
                ammo.StackCount = stack;
                if (!string.IsNullOrEmpty(fromKey)) ammo.FromInfoKey = fromKey;
                // 官方 preset 把背包设成 15 格，随机战利品可能已经塞满：按需要扩一格。
                int free = inventory.GetFirstEmptyPosition(0);
                if (free < 0 || free >= inventory.Capacity)
                    inventory.SetCapacity(Math.Max(inventory.Capacity, inventory.Content.Count) + 1);
                if (!inventory.AddItem(ammo)) { ammo.DestroyTree(); return; }
                count -= stack;
            }
        }

        private static int PickAmmo(Item weapon, ItemSetting_Gun gun, int ammoMin, System.Random random)
        {
            string caliber = Caliber(weapon);
            if (string.IsNullOrEmpty(caliber)) return 0;
            List<int> valid = new List<int>();
            int[] preferred = AmmoPool(caliber, ammoMin, MaxQuality);
            for (int i = 0; i < preferred.Length; i++)
                if (IsValidBullet(gun, preferred[i])) valid.Add(preferred[i]);
            if (valid.Count > 0) return valid[random.Next(valid.Count)];
            // 这个口径没有够格的弹药：退回这把枪能用的最高品质（不设上限，见类注释）。
            int best = 0, bestQuality = -1;
            int[] any = AmmoPool(caliber, 1, AnyAmmoMaxQuality);
            for (int i = 0; i < any.Length; i++)
            {
                Item prefab = ItemAssetsCollection.GetPrefab(any[i]);
                if (prefab == null || prefab.Quality <= bestQuality || !IsValidBullet(gun, any[i])) continue;
                best = any[i];
                bestQuality = prefab.Quality;
            }
            return best;
        }

        private static bool IsValidBullet(ItemSetting_Gun gun, int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                return prefab != null && prefab.TypeID == typeId && gun.IsValidBullet(prefab);
            }
            catch (Exception) { return false; }
        }

        /// <summary>某槽位、某品质带的候选武器（排序后的 TypeID）。</summary>
        internal static int[] Pool(string slotKey, int minQuality, int maxQuality)
        {
            if (minQuality > maxQuality) return Empty;
            string key = slotKey + "|" + minQuality + "|" + maxQuality;
            int[] cached;
            if (weaponPools.TryGetValue(key, out cached)) return cached;
            List<int> result = new List<int>();
            bool complete = false;
            try
            {
                Slot template = TemplateSlot(slotKey);
                GameplayDataSettings.TagsData tags = GameplayDataSettings.Tags;
                if (template != null && template.requireTags != null && template.requireTags.Count > 0 && tags != null && tags.AllTags != null)
                {
                    List<Tag> exclude = LootExcludeTagPolicy.BuildExcludeTags(tags, true, true);
                    if (template.excludeTags != null) exclude.AddRange(template.excludeTags);
                    ItemFilter filter = default(ItemFilter);
                    filter.requireTags = template.requireTags.ToArray();
                    filter.excludeTags = exclude.ToArray();
                    filter.minQuality = minQuality;
                    filter.maxQuality = maxQuality;
                    int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                    if (ids != null)
                    {
                        bool gunSlot = slotKey == PrimarySlot;
                        for (int i = 0; i < ids.Length; i++)
                            if (Eligible(ids[i], template, gunSlot, minQuality, maxQuality)) result.Add(ids[i]);
                        complete = true;
                    }
                }
                result.Sort();
            }
            catch (Exception e)
            {
                complete = false;
                Debug.LogWarning("[SkyIslandArmory] 武器池查询失败 " + key + "：" + e.Message);
            }
            cached = result.ToArray();
            if (complete)
            {
                weaponPools[key] = cached;
                Debug.Log("[SkyIslandArmory] POOL " + key + " size=" + cached.Length);
            }
            return cached;
        }

        private static bool Eligible(int id, Slot template, bool gunSlot, int minQuality, int maxQuality)
        {
            if (id <= 0 || SkyIslandEnemyArmoryRules.IsOwnModItem(id) || LootBlacklistRegistry.Contains(id)) return false;
            Item prefab = ItemAssetsCollection.GetPrefab(id);
            if (prefab == null || prefab.TypeID != id || prefab.name == "FallbackItem" || prefab.Sticky) return false;
            if (prefab.Quality < minQuality || prefab.Quality > maxQuality) return false;
            if (!SkyIslandLootTables.AllowedInPool(prefab.Value)) return false;
            if (Mathf.RoundToInt(prefab.GetStatValue(ControlMindTypeHash)) != 0) return false;
            if (!template.CanPlug(prefab)) return false;
            ItemSetting_Gun gun = prefab.GetComponent<ItemSetting_Gun>();
            if (!gunSlot) return gun == null;
            if (gun == null || prefab.Inventory == null || gun.Capacity <= 0) return false;
            string caliber = Caliber(prefab);
            return !string.IsNullOrEmpty(caliber) && AmmoPool(caliber, 1, AnyAmmoMaxQuality).Length > 0;
        }

        /// <summary>某口径、某品质带的弹药（官方与其它 Mod，排序后的 TypeID）。</summary>
        private static int[] AmmoPool(string caliber, int minQuality, int maxQuality)
        {
            string key = caliber + "|" + minQuality + "|" + maxQuality;
            int[] cached;
            if (ammoPools.TryGetValue(key, out cached)) return cached;
            List<int> result = new List<int>();
            bool complete = false;
            try
            {
                GameplayDataSettings.TagsData tags = GameplayDataSettings.Tags;
                if (tags != null && tags.Bullet != null)
                {
                    ItemFilter filter = default(ItemFilter);
                    filter.requireTags = new[] { tags.Bullet };
                    filter.minQuality = minQuality;
                    filter.maxQuality = maxQuality;
                    filter.caliber = caliber;
                    int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                    if (ids != null)
                    {
                        for (int i = 0; i < ids.Length; i++)
                        {
                            if (ids[i] <= 0 || SkyIslandEnemyArmoryRules.IsOwnModItem(ids[i]) || LootBlacklistRegistry.Contains(ids[i])) continue;
                            Item prefab = ItemAssetsCollection.GetPrefab(ids[i]);
                            if (prefab != null && prefab.TypeID == ids[i] && prefab.name != "FallbackItem") result.Add(ids[i]);
                        }
                        complete = true;
                    }
                }
                result.Sort();
            }
            catch (Exception e)
            {
                complete = false;
                Debug.LogWarning("[SkyIslandArmory] 弹药池查询失败 " + key + "：" + e.Message);
            }
            cached = result.ToArray();
            if (complete) ammoPools[key] = cached;
            return cached;
        }

        private static Slot TemplateSlot(string slotKey)
        {
            Item template = ItemAssetsCollection.GetPrefab(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            return SkyIslandBossForge.FindSlot(template, slotKey);
        }

        private static string Caliber(Item item)
        {
            try { return item != null && item.Constants != null ? item.Constants.GetString("Caliber", null) : null; }
            catch (Exception) { return null; }
        }

        private static string Describe(Item body, string slotKey)
        {
            Slot slot = SkyIslandBossForge.FindSlot(body, slotKey);
            Item item = slot != null ? slot.Content : null;
            return item == null ? "none" : item.TypeID + "(q" + item.Quality + ")";
        }

        private static void WarnOnce(string key, string message)
        {
            if (warned.Add(key)) Debug.LogWarning(message);
        }

        /// <summary>
        /// 进岛读条时建好全部档次会用到的武器池（每个池子占一帧）。由 <see cref="SkyIslandLootPools.Prewarm"/> 在物资池之后透传，
        /// 同一进程第二趟起全是缓存命中。
        /// </summary>
        internal static IEnumerator Prewarm()
        {
            SkyIslandEnemyTier[] tiers =
            {
                SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite, SkyIslandEnemyTier.Champion,
                SkyIslandEnemyTier.Storm, SkyIslandEnemyTier.Chief, SkyIslandEnemyTier.Lord
            };
            string[] slots = { PrimarySlot, MeleeSlot };
            for (int i = 0; i < tiers.Length; i++)
            {
                SkyIslandWeaponBand band = SkyIslandEnemyArmoryRules.For(tiers[i]);
                for (int s = 0; s < slots.Length; s++)
                {
                    string key = slots[s] + "|" + band.Min + "|" + band.Max;
                    if (weaponPools.ContainsKey(key)) continue;
                    if (Pool(slots[s], band.Min, band.Max).Length == 0) Pool(slots[s], band.Floor, band.Max);
                    yield return null;
                }
                // 选弹药时用到的「够格弹药」池：本档枪池里出现的每个口径一次查询，一个档次占一帧。
                HashSet<string> calibers = new HashSet<string>(StringComparer.Ordinal);
                CollectCalibers(Pool(PrimarySlot, band.Min, band.Max), calibers);
                CollectCalibers(Pool(PrimarySlot, band.Floor, band.Max), calibers);
                foreach (string caliber in calibers) AmmoPool(caliber, band.AmmoMin, MaxQuality);
                yield return null;
            }
        }

        private static void CollectCalibers(int[] pool, HashSet<string> into)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                string caliber = Caliber(ItemAssetsCollection.GetPrefab(pool[i]));
                if (!string.IsNullOrEmpty(caliber)) into.Add(caliber);
            }
        }

        internal static void ResetStaticCaches()
        {
            weaponPools.Clear();
            ammoPools.Clear();
            warned.Clear();
        }

        internal static Slot WeaponSlot(Item body, bool primary)
        {
            return SkyIslandBossForge.FindSlot(body, primary ? PrimarySlot : SecondarySlot);
        }
    }

    /// <summary>
    /// 岛上敌人的续弹（见 <see cref="SkyIslandEnemyArmory"/> 类注释）：配装时记下主 / 副武器各是哪一把、用哪种弹，
    /// 每 <see cref="Interval"/> 秒看一眼背包，同款备弹不足一匣就补回规定发数。槽里不再是配装时那把枪就不管那一槽。
    /// 用 InvokeRepeating 定时，不进每帧路径（SkyIslandEnemyArmoryGuard）；只挂在本 Mod 刷出的敌人身上、随角色销毁；
    /// 倒下后取消定时，不往尸体箱里续。
    /// </summary>
    internal sealed class SkyIslandAmmoKeeper : MonoBehaviour
    {
        internal const float Interval = 3f;

        private CharacterMainControl owner;
        private Item primaryWeapon, secondaryWeapon;
        private int primaryAmmo, secondaryAmmo;
        private string fromKey;

        internal void Bind(CharacterMainControl character, int primary, int secondary, string from)
        {
            owner = character;
            Item body = character != null ? character.CharacterItem : null;
            primaryWeapon = primary > 0 ? Weapon(body, true) : null;
            secondaryWeapon = secondary > 0 ? Weapon(body, false) : null;
            primaryAmmo = primary;
            secondaryAmmo = secondary;
            fromKey = from;
            InvokeRepeating(nameof(Check), Interval, Interval);
        }

        private void Check()
        {
            try
            {
                if (owner == null || owner.Health == null || owner.Health.IsDead) { CancelInvoke(); return; }
                Item body = owner.CharacterItem;
                if (body == null) return;
                Refill(body, true, primaryWeapon, primaryAmmo);
                Refill(body, false, secondaryWeapon, secondaryAmmo);
            }
            catch (Exception) { CancelInvoke(); }
        }

        private void Refill(Item body, bool primary, Item expected, int ammoTypeId)
        {
            if (expected == null || ammoTypeId <= 0) return;
            Item weapon = Weapon(body, primary);
            if (weapon != expected) return;
            ItemSetting_Gun gun = weapon.GetComponent<ItemSetting_Gun>();
            if (gun == null || SkyIslandEnemyArmory.ReserveOf(body, ammoTypeId) >= Math.Max(1, gun.Capacity)) return;
            SkyIslandEnemyArmory.TopUpReserve(body, gun, ammoTypeId, fromKey);
        }

        private static Item Weapon(Item body, bool primary)
        {
            Slot slot = body != null ? SkyIslandEnemyArmory.WeaponSlot(body, primary) : null;
            return slot != null ? slot.Content : null;
        }

        private void OnDestroy()
        {
            CancelInvoke();
            owner = null;
            primaryWeapon = null;
            secondaryWeapon = null;
        }
    }
}
