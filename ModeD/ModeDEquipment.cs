// ============================================================================
// ModeDEquipment.cs - Mode D 装备系统
// ============================================================================
// 模块说明：
//   管理 Mode D 模式下的装备发放逻辑，包括：
//   - 玩家开局装备发放（武器、护甲、头盔、弹药、医疗品等）
//   - 敌人配装（替换默认装备，确保有合理掉落）
//   - 物品池管理（配件池、各类装备池）
//   
// 开局装备规则：
//   - 武器：必给，优先低品质（1-3级），配件随机 30% 概率
//   - 弹药：必给，根据武器类型匹配
//   - 护甲/头盔：各 50% 概率
//   - 近战武器：40% 概率
//   - 医疗品：必给 3 格
//   - 图腾/面具：各 30% 概率
//   - 背包：40% 概率
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// Mode D 装备发放和敌人配装模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode D 装备系统字段
        
        /// <summary>保存发放的武器引用，用于后续给弹药</summary>
        private Item lastGivenWeapon = null;

        /// <summary>Mode D 全局物品池（静态缓存）</summary>
        private static List<int> modeDGlobalItemPool = null;
        
        /// <summary>Mode D 全局物品池是否已初始化</summary>
        private static bool modeDGlobalItemPoolInitialized = false;
        
        #endregion

        #region 物品分类枚举
        
        /// <summary>
        /// 物品大类枚举，用于分类发放和掉落
        /// </summary>
        private enum ItemCategory
        {
            Weapon,      // 武器
            Accessory,   // 配件
            Helmet,      // 头盔
            Armor,       // 护甲
            Ammo,        // 弹药
            Medical,     // 医疗品
            Totem,       // 图腾
            Mask,        // 面具
            Backpack,    // 背包
            MeleeWeapon  // 近战武器
        }
        
        #endregion

        #region 玩家开局装备发放
        
        /// <summary>
        /// 给玩家发放开局装备
        /// </summary>
        /// <remarks>
        /// 发放顺序：武器 → 弹药 → 护甲 → 头盔 → 近战 → 医疗品 → 图腾 → 面具 → 背包
        /// </remarks>
        private void GivePlayerStarterKit()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    DevLog("[ModeD] [ERROR] GivePlayerStarterKit: 未找到玩家");
                    return;
                }

                DevLog("[ModeD] 给玩家发放开局装备...");

                // 1. 发放随机武器（必给，配件随机 0-100%）
                lastGivenWeapon = null;
                GiveRandomWeapon(main);

                // 填满弹夹
                if (lastGivenWeapon != null)
                {
                    EnsureStarterGunHasBulletType(lastGivenWeapon);
                    FillGunMagazine(lastGivenWeapon);
                }

                // 2. 发放弹药（必给，根据武器）
                GiveStarterAmmo(main);

                // 3. 发放护甲（随机给，50%概率）
                if (UnityEngine.Random.value > 0.5f)
                {
                    GiveRandomArmor(main);
                }

                // 4. 发放头盔（随机给，50%概率）
                if (UnityEngine.Random.value > 0.5f)
                {
                    GiveRandomHelmet(main);
                }

                // 5. 发放近战武器（随机给，40%概率）
                if (UnityEngine.Random.value > 0.6f)
                {
                    GiveRandomMeleeWeapon(main);
                }

                // 6. 发放医疗品（必给，3格）
                GiveStarterMedical(main, 3);

                // 7. 发放图腾（随机给，30%概率）
                if (UnityEngine.Random.value > 0.7f)
                {
                    GiveRandomTotem(main);
                }

                // 8. 发放面具（随机给，30%概率）
                if (UnityEngine.Random.value > 0.7f)
                {
                    GiveRandomMask(main);
                }

                // 9. 发放背包（随机给，40%概率）
                if (UnityEngine.Random.value > 0.6f)
                {
                    GiveRandomBackpack(main);
                }

                DevLog("[ModeD] 开局装备发放完成");
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GivePlayerStarterKit 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 发放随机武器
        /// </summary>
        private void GiveRandomWeapon(CharacterMainControl character)
        {
            try
            {
                if (modeDWeaponPool.Count == 0)
                {
                    DevLog("[ModeD] 武器池为空，跳过武器发放");
                    return;
                }

                // 优先选择低品质（1-3级）武器
                int weaponId = GetRandomItemByQuality(modeDWeaponPool, 1, 3);

                Item weapon = ItemAssetsCollection.InstantiateSync(weaponId);
                if (weapon == null)
                {
                    DevLog("[ModeD] 无法创建武器 ID=" + weaponId);
                    return;
                }

                // 保存武器引用，用于后续给弹药
                lastGivenWeapon = weapon;

                // 配件完全随机：每个槽位独立判断是否安装（50%概率）
                TryAddRandomAttachmentsFullRandom(weapon);

                // 尝试装备到主武器槽
                bool equipped = character.CharacterItem.TryPlug(weapon, true, null, 0);
                if (!equipped)
                {
                    // 放入背包
                    ItemUtilities.SendToPlayerCharacterInventory(weapon, false);
                }

                DevLog("[ModeD] 发放武器: " + weapon.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomWeapon 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 完全随机添加配件（每个槽位独立50%概率）
        /// </summary>
        private void TryAddRandomAttachmentsFullRandom(Item weapon)
        {
            try
            {
                if (weapon == null || weapon.Slots == null) return;

                // 确保配件池已初始化
                if (modeDAccessoryPool.Count == 0)
                {
                    InitializeAccessoryPool();
                }

                if (modeDAccessoryPool.Count == 0)
                {
                    DevLog("[ModeD] 配件池为空，跳过配件添加");
                    return;
                }

                // 遍历武器的配件槽，每个槽位独立50%概率
                foreach (Slot slot in weapon.Slots)
                {
                    if (slot == null || slot.Content != null) continue;

                    // 调低为约 30% 概率填充此槽
                    if (UnityEngine.Random.value > 0.7f)
                    {
                        TryFillSlotWithRandomAccessory(weapon, slot);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] TryAddRandomAttachmentsFullRandom 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放随机近战武器（从预建池中随机）
        /// </summary>
        private void GiveRandomMeleeWeapon(CharacterMainControl character)
        {
            try
            {
                if (modeDMeleePool.Count == 0)
                {
                    DevLog("[ModeD] 近战武器池为空，跳过近战武器发放");
                    return;
                }

                int meleeId = modeDMeleePool[UnityEngine.Random.Range(0, modeDMeleePool.Count)];
                Item melee = ItemAssetsCollection.InstantiateSync(meleeId);
                if (melee == null)
                {
                    DevLog("[ModeD] 无法创建近战武器 ID=" + meleeId);
                    return;
                }

                // 尝试装备到近战槽，如果失败放入背包
                bool equipped = character.CharacterItem.TryPlug(melee, true, null, 0);
                if (!equipped)
                {
                    ItemUtilities.SendToPlayerCharacterInventory(melee, false);
                }

                DevLog("[ModeD] 发放近战武器: " + melee.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomMeleeWeapon 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 给敌人发放随机近战武器（从预建池中随机）
        /// </summary>
        /// <param name="enemy">敌人角色</param>
        private void GiveRandomMeleeWeaponToEnemy(CharacterMainControl enemy)
        {
            try
            {
                if (modeDMeleePool.Count == 0)
                {
                    DevLog("[ModeD] 敌人近战武器池为空，跳过");
                    return;
                }

                int meleeId = modeDMeleePool[UnityEngine.Random.Range(0, modeDMeleePool.Count)];
                Item melee = ItemAssetsCollection.InstantiateSync(meleeId);
                if (melee == null)
                {
                    DevLog("[ModeD] 无法创建敌人近战武器 ID=" + meleeId);
                    return;
                }

                bool equipped = enemy.CharacterItem.TryPlug(melee, true, null, 0);
                if (!equipped)
                {
                    // P1-4 修复：装备失败时尝试放入背包，否则销毁防止泄漏
                    Inventory inventory = enemy.CharacterItem.Inventory;
                    if (inventory != null)
                    {
                        inventory.AddAndMerge(melee, 0);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(melee.gameObject);
                        DevLog("[ModeD] [WARNING] 敌人近战武器装备失败且无背包，已销毁");
                    }
                }

                DevLog("[ModeD] 敌人发放近战武器: " + melee.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomMeleeWeaponToEnemy 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 发放随机护甲
        /// </summary>
        private void GiveRandomArmor(CharacterMainControl character)
        {
            try
            {
                if (modeDArmortPool.Count == 0)
                {
                    DevLog("[ModeD] 护甲池为空，跳过护甲发放");
                    return;
                }

                int armorId = GetRandomItemByQuality(modeDArmortPool, 1, 3);
                Item armor = ItemAssetsCollection.InstantiateSync(armorId);
                if (armor != null)
                {
                    bool equipped = character.CharacterItem.TryPlug(armor, true, null, 0);
                    if (!equipped)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(armor, false);
                    }
                    DevLog("[ModeD] 发放护甲: " + armor.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomArmor 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放随机头盔
        /// </summary>
        private void GiveRandomHelmet(CharacterMainControl character)
        {
            try
            {
                if (modeDHelmetPool.Count == 0)
                {
                    DevLog("[ModeD] 头盔池为空，跳过头盔发放");
                    return;
                }

                int helmetId = GetRandomItemByQuality(modeDHelmetPool, 1, 3);
                Item helmet = ItemAssetsCollection.InstantiateSync(helmetId);
                if (helmet != null)
                {
                    bool equipped = character.CharacterItem.TryPlug(helmet, true, null, 0);
                    if (!equipped)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(helmet, false);
                    }
                    DevLog("[ModeD] 发放头盔: " + helmet.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomHelmet 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放随机图腾（从预建池中随机）
        /// </summary>
        private void GiveRandomTotem(CharacterMainControl character)
        {
            try
            {
                if (modeDTotemPool.Count == 0)
                {
                    DevLog("[ModeD] 图腾池为空，跳过图腾发放");
                    return;
                }

                int totemId = modeDTotemPool[UnityEngine.Random.Range(0, modeDTotemPool.Count)];
                Item totem = ItemAssetsCollection.InstantiateSync(totemId);
                if (totem != null)
                {
                    bool equipped = character.CharacterItem.TryPlug(totem, true, null, 0);
                    if (!equipped)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(totem, false);
                    }
                    DevLog("[ModeD] 发放图腾: " + totem.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomTotem 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放随机面具（从预建池中随机）
        /// </summary>
        private void GiveRandomMask(CharacterMainControl character)
        {
            try
            {
                if (modeDMaskPool.Count == 0)
                {
                    DevLog("[ModeD] 面具池为空，跳过面具发放");
                    return;
                }

                int maskId = modeDMaskPool[UnityEngine.Random.Range(0, modeDMaskPool.Count)];
                Item mask = ItemAssetsCollection.InstantiateSync(maskId);
                if (mask != null)
                {
                    bool equipped = character.CharacterItem.TryPlug(mask, true, null, 0);
                    if (!equipped)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(mask, false);
                    }
                    DevLog("[ModeD] 发放面具: " + mask.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomMask 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放随机背包（从预建池中随机）
        /// </summary>
        private void GiveRandomBackpack(CharacterMainControl character)
        {
            try
            {
                if (modeDBackpackPool.Count == 0)
                {
                    DevLog("[ModeD] 背包池为空，跳过背包发放");
                    return;
                }

                int backpackId = modeDBackpackPool[UnityEngine.Random.Range(0, modeDBackpackPool.Count)];
                Item backpack = ItemAssetsCollection.InstantiateSync(backpackId);
                if (backpack != null)
                {
                    bool equipped = character.CharacterItem.TryPlug(backpack, true, null, 0);
                    if (!equipped)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(backpack, false);
                    }
                    DevLog("[ModeD] 发放背包: " + backpack.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveRandomBackpack 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 根据名称查找 Tag
        /// </summary>
        private Duckov.Utilities.Tag FindTagByName(string tagName)
        {
            try
            {
                foreach (var tag in GameplayDataSettings.Tags.AllTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        return tag;
                    }
                }
            }
            catch {}
            return null;
        }
        /// <summary>
        /// 发放开局弹药（根据武器的 TargetBulletID）
        /// </summary>
        private void GiveStarterAmmo(CharacterMainControl character)
        {
            try
            {
                if (lastGivenWeapon == null)
                {
                    DevLog("[ModeD] 没有武器，跳过弹药发放");
                    return;
                }

                // 获取武器的 ItemSetting_Gun 组件
                ItemSetting_Gun gunSetting = lastGivenWeapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null)
                {
                    DevLog("[ModeD] 武器没有 ItemSetting_Gun 组件，跳过弹药发放");
                    return;
                }

                int targetBulletId = gunSetting.TargetBulletID;
                if (targetBulletId < 0)
                {
                    DevLog("[ModeD] 武器没有指定弹药类型，跳过弹药发放");
                    return;
                }

                DevLog("[ModeD] 武器目标弹药ID: " + targetBulletId);

                // 直接创建对应的弹药
                Item ammo = ItemAssetsCollection.InstantiateSync(targetBulletId);
                if (ammo != null)
                {
                    ammo.StackCount = UnityEngine.Random.Range(60, 121);
                    ItemUtilities.SendToPlayerCharacterInventory(ammo, false);
                    DevLog("[ModeD] 发放弹药: " + ammo.DisplayName + " x" + ammo.StackCount);
                }
                else
                {
                    DevLog("[ModeD] 无法创建弹药 ID=" + targetBulletId);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveStarterAmmo 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 发放开局医疗品
        /// </summary>
        /// <param name="character">角色</param>
        /// <param name="count">发放数量（默认1）</param>
        private void GiveStarterMedical(CharacterMainControl character, int count = 1)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (modeDMedicalPool.Count == 0)
                    {
                        // 使用硬编码的常见医疗品
                        int[] commonMedIds = new int[] { 401, 402, 403 };
                        int medId = commonMedIds[UnityEngine.Random.Range(0, commonMedIds.Length)];

                        Item med = ItemAssetsCollection.InstantiateSync(medId);
                        if (med != null)
                        {
                            ItemUtilities.SendToPlayerCharacterInventory(med, false);
                            DevLog("[ModeD] 发放医疗品(硬编码): " + med.DisplayName);
                        }
                    }
                    else
                    {
                        int medicalId = modeDMedicalPool[UnityEngine.Random.Range(0, modeDMedicalPool.Count)];
                        Item medical = ItemAssetsCollection.InstantiateSync(medicalId);
                        if (medical != null)
                        {
                            ItemUtilities.SendToPlayerCharacterInventory(medical, false);
                            DevLog("[ModeD] 发放医疗品: " + medical.DisplayName);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveStarterMedical 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 确保开局武器有弹药类型
        /// <para>通过武器口径查找匹配的弹药</para>
        /// </summary>
        /// <param name="weapon">需要设置弹药类型的武器</param>
        private void EnsureStarterGunHasBulletType(Item weapon)
        {
            try
            {
                if (weapon == null) return;

                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                // 已有弹药类型，无需设置
                if (gunSetting.TargetBulletID >= 0) return;

                // 获取武器的口径
                string weaponCaliber = weapon.Constants.GetString("Caliber".GetHashCode(), null);
                if (string.IsNullOrEmpty(weaponCaliber))
                {
                    DevLog("[ModeD] 武器没有口径信息，无法匹配弹药");
                    return;
                }

                DevLog("[ModeD] 武器口径: " + weaponCaliber);

                if (modeDAmmoPool.Count == 0)
                {
                    DevLog("[ModeD] 弹药池为空，无法为武器设置弹药类型");
                    return;
                }

                // 遍历弹药池，找到口径匹配的弹药
                List<int> matchingAmmo = new List<int>();
                for (int i = 0; i < modeDAmmoPool.Count; i++)
                {
                    int ammoId = modeDAmmoPool[i];
                    try
                    {
                        var meta = ItemAssetsCollection.GetMetaData(ammoId);
                        if (meta.caliber == weaponCaliber)
                        {
                            matchingAmmo.Add(ammoId);
                        }
                    }
                    catch {}
                }

                if (matchingAmmo.Count == 0)
                {
                    DevLog("[ModeD] 未找到口径 " + weaponCaliber + " 的弹药");
                    return;
                }

                // 随机选择一个匹配的弹药
                int selectedAmmoId = matchingAmmo[UnityEngine.Random.Range(0, matchingAmmo.Count)];
                gunSetting.SetTargetBulletType(selectedAmmoId);
                DevLog("[ModeD] 为武器设置弹药类型: ID=" + selectedAmmoId + " (口径=" + weaponCaliber + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] EnsureStarterGunHasBulletType 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 填满枪支弹夹
        /// </summary>
        private void FillGunMagazine(Item weapon)
        {
            try
            {
                if (weapon == null) return;

                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                int targetBulletId = gunSetting.TargetBulletID;
                if (targetBulletId < 0) return;

                int capacity = gunSetting.Capacity;
                int currentBullets = gunSetting.BulletCount;
                int bulletsNeeded = capacity - currentBullets;

                if (bulletsNeeded <= 0) return;

                // P2-7 修复：检查武器是否有 Inventory
                Inventory weaponInventory = weapon.Inventory;
                if (weaponInventory == null) return;

                // 创建弹药并放入枪的弹夹内
                Item ammo = ItemAssetsCollection.InstantiateSync(targetBulletId);
                if (ammo != null)
                {
                    ammo.StackCount = bulletsNeeded;
                    // 直接放入枪的库存（弹夹）
                    weaponInventory.AddAndMerge(ammo, 0);
                    DevLog("[ModeD] 填满弹夹: " + weapon.DisplayName + " +" + bulletsNeeded + " 发");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] FillGunMagazine 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 填充枪支内部弹药（用于敌人配装）
        /// </summary>
        /// <param name="weapon">武器</param>
        /// <param name="stacks">弹药堆叠数</param>
        /// <param name="minPerStack">每堆最小数量</param>
        /// <param name="maxPerStack">每堆最大数量</param>
        private void FillGunInternalAmmo(Item weapon, int stacks, int minPerStack, int maxPerStack)
        {
            try
            {
                if (weapon == null) return;

                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                int targetBulletId = gunSetting.TargetBulletID;
                if (targetBulletId < 0) return;

                Inventory gunInventory = weapon.Inventory;
                if (gunInventory == null) return;

                for (int i = 0; i < stacks; i++)
                {
                    Item ammo = ItemAssetsCollection.InstantiateSync(targetBulletId);
                    if (ammo == null) continue;

                    ammo.StackCount = UnityEngine.Random.Range(minPerStack, maxPerStack + 1);
                    gunInventory.AddAndMerge(ammo, 0);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] FillGunInternalAmmo 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 按品质范围随机选择物品（优化版：有限随机抽样，无列表分配）
        /// </summary>
        private int GetRandomItemByQuality(List<int> pool, int minQuality, int maxQuality)
        {
            // P1-11: 先检查空池
            if (pool == null || pool.Count == 0)
            {
                return 0;
            }

            try
            {
                // P1-11 优化：使用有限随机抽样代替分配 filtered List
                // 最多尝试 30 次，如果都找不到符合品质的，就直接返回随机的一个
                const int MAX_QUALITY_ATTEMPTS = 30;

                for (int attempt = 0; attempt < MAX_QUALITY_ATTEMPTS; attempt++)
                {
                    int id = pool[UnityEngine.Random.Range(0, pool.Count)];
                    try
                    {
                        var meta = ItemAssetsCollection.GetMetaData(id);
                        if (meta.quality >= minQuality && meta.quality <= maxQuality)
                        {
                            return id;
                        }
                    }
                    catch {}
                }

                // 没有找到符合品质要求的，随机返回一个
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            catch
            {
                return pool.Count > 0 ? pool[0] : 0;
            }
        }

        /// <summary>
        /// Mode D 配件池（Accessory Tag）
        /// </summary>
        private readonly List<int> modeDAccessoryPool = new List<int>();

        /// <summary>
        /// 初始化配件池（包含游戏所有配件）
        /// </summary>
        private void InitializeAccessoryPool()
        {
            try
            {
                modeDAccessoryPool.Clear();

                // 通过名字查找配件 Tag
                Duckov.Utilities.Tag accessoryTag = FindTagByName("Accessory");
                if (accessoryTag == null)
                {
                    DevLog("[ModeD] 未找到 Accessory Tag，跳过配件池初始化");
                    return;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { accessoryTag };
                filter.minQuality = 1;
                filter.maxQuality = 8; // 包含所有品质
                int[] accessoryIds = ItemAssetsCollection.Search(filter);

                if (accessoryIds != null)
                {
                    modeDAccessoryPool.AddRange(accessoryIds);
                }

                DevLog("[ModeD] 配件池初始化完成，数量: " + modeDAccessoryPool.Count);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] InitializeAccessoryPool 失败: " + e.Message);
            }
        }

        /// <summary>
        /// P1-7 优化：尝试用随机配件填充槽位（有限随机抽样，而非全池洗牌）
        /// </summary>
        private void TryFillSlotWithRandomAccessory(Item weapon, Slot slot)
        {
            try
            {
                if (modeDAccessoryPool.Count == 0) return;

                // P1-7 优化：改为有限次数随机抽样，而不是复制+洗牌整个池
                // 每个槽最多尝试 8 次，避免大量 Instantiate/Destroy 和 GC
                const int MAX_ACCESSORY_ATTEMPTS = 8;

                for (int attempt = 0; attempt < MAX_ACCESSORY_ATTEMPTS; attempt++)
                {
                    int accessoryId = modeDAccessoryPool[UnityEngine.Random.Range(0, modeDAccessoryPool.Count)];
                    try
                    {
                        Item accessory = ItemAssetsCollection.InstantiateSync(accessoryId);
                        if (accessory == null) continue;

                        // 使用 Slot.CanPlug 检查是否可以安装
                        if (slot.CanPlug(accessory))
                        {
                            Item replaced;
                            if (slot.Plug(accessory, out replaced))
                            {
                                DevLog("[ModeD] 安装配件: " + accessory.DisplayName + " 到 " + slot.DisplayName);
                                
                                // 销毁被替换的配件（如果有）
                                if (replaced != null)
                                {
                                    UnityEngine.Object.Destroy(replaced.gameObject);
                                }
                                return; // 成功安装，退出
                            }
                        }

                        // 无法安装，销毁临时配件
                        UnityEngine.Object.Destroy(accessory.gameObject);
                    }
                    catch {}
                }

                // 尝试 8 次后仍未找到合适的配件，放弃此槽
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] TryFillSlotWithRandomAccessory 失败: " + e.Message);
            }
        }

        // 所有可掉落的物品大类
        private readonly ItemCategory[] allDropCategories = new ItemCategory[]
        {
            ItemCategory.Weapon,
            ItemCategory.Accessory,
            ItemCategory.Helmet,
            ItemCategory.Armor,
            ItemCategory.Ammo,
            ItemCategory.Medical,
            ItemCategory.Totem,
            ItemCategory.Mask
        };

        /// <summary>
        /// 为敌人配装（替换默认装备）
        /// 掉落物包含一把枪和匹配的子弹，其他格子随机填充
        /// </summary>
        /// <param name="enemy">敌人角色</param>
        /// <param name="waveIndex">当前波次</param>
        /// <param name="enemyHealth">敌人血量（用于决定装备品质）</param>
        /// <param name="isBoss">是否为Boss（Boss保留原有头盔和护甲）</param>
        public void EquipEnemyForModeD(CharacterMainControl enemy, int waveIndex, float enemyHealth, bool isBoss = false)
        {
            try
            {
                if (enemy == null) return;

                DevLog("[ModeD] 为敌人配装: wave=" + waveIndex + ", health=" + enemyHealth);

                // 根据血量和波次计算品质等级（1-6）
                int qualityLevel = CalculateQualityLevel(waveIndex, enemyHealth);

                bool hasPrimaryOrSecondaryWeapon = false;
                bool hasMeleeWeapon = false;

                try
                {
                    Slot prim = enemy.PrimWeaponSlot();
                    if (prim != null && prim.Content != null)
                    {
                        hasPrimaryOrSecondaryWeapon = true;
                    }
                }
                catch {}

                try
                {
                    Slot sec = enemy.SecWeaponSlot();
                    if (sec != null && sec.Content != null)
                    {
                        hasPrimaryOrSecondaryWeapon = true;
                    }
                }
                catch {}

                try
                {
                    Slot meleeSlot = enemy.MeleeWeaponSlot();
                    if (meleeSlot != null && meleeSlot.Content != null)
                    {
                        hasMeleeWeapon = true;
                    }
                }
                catch {}

                bool hasPetAI = false;
                try
                {
                    hasPetAI = enemy.GetComponentInChildren<PetAI>() != null;
                }
                catch {}

                bool keepOriginalMeleeSetup = (!hasPrimaryOrSecondaryWeapon && hasMeleeWeapon) || hasPetAI;

                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                // P1-4 修复：解耦 Inventory 检查，允许无背包敌人也能配装
                // 即使 inventory 为 null，仍然可以给敌人装备武器（TryPlug），只是不能往背包塞东西
                Inventory inventory = characterItem.Inventory;
                bool hasInventory = (inventory != null);

                if (keepOriginalMeleeSetup)
                {
                    DevLog("[ModeD] 检测到近战/宠物型敌人，保留原始武器配置，仅追加掉落");

                    // 只有有背包的敌人才追加掉落
                    if (hasInventory)
                    {
                        int meleeTargetItemCount = 5 + Mathf.FloorToInt(enemyHealth / 100f);
                        if (meleeTargetItemCount < 5)
                        {
                            meleeTargetItemCount = 5;
                        }

                        int meleeCurrentCount = 0;
                        try
                        {
                            if (inventory.Content != null)
                            {
                                meleeCurrentCount = inventory.Content.Count;
                            }
                        }
                        catch {}

                        int meleeRemainingToFill = Mathf.Max(0, meleeTargetItemCount - meleeCurrentCount);
                        if (meleeRemainingToFill > 0)
                        {
                            FillEnemyInventoryForModeD(enemy, qualityLevel, meleeRemainingToFill);
                        }
                    }
                    else
                    {
                        DevLog("[ModeD] 敌人无背包，跳过追加掉落");
                    }

                    return;
                }

                // 清空敌人现有装备和背包（ClearEnemyInventory 内部已处理 inventory == null）
                // Boss保留原有头盔和护甲
                ClearEnemyInventory(enemy, isBoss);

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                // 1. 随机填充少量额外掉落物（不包含武器和弹药）- 需要背包
                if (hasInventory)
                {
                    int extraItems = 3;

                    ItemCategory[] nonWeaponCategories = new ItemCategory[]
                    {
                        ItemCategory.Accessory,
                        ItemCategory.Helmet,
                        ItemCategory.Armor,
                        ItemCategory.Medical,
                        ItemCategory.Totem,
                        ItemCategory.Mask
                    };

                    for (int i = 0; i < extraItems; i++)
                    {
                        ItemCategory randomCategory = nonWeaponCategories[UnityEngine.Random.Range(0, nonWeaponCategories.Length)];
                        GiveEnemyItemByCategoryNoWeapon(enemy, randomCategory, qualityLevel);
                    }
                }

                // 2. 确保敌人有武器可用（装备到手上，不需要背包）
                GiveEnemyEquippedWeapon(enemy, qualityLevel);
                GiveRandomMeleeWeaponToEnemy(enemy);

                // 3. 尝试将敌人背包进一步填满（仅限 Mode D，需要背包）
                if (hasInventory)
                {
                    int targetItemCount = 5 + Mathf.FloorToInt(enemyHealth / 100f);
                    if (targetItemCount < 5)
                    {
                        targetItemCount = 5;
                    }

                    int currentCount = 0;
                    try
                    {
                        if (inventory.Content != null)
                        {
                            currentCount = inventory.Content.Count;
                        }
                    }
                    catch {}

                    int remainingToFill = Mathf.Max(0, targetItemCount - currentCount);
                    if (remainingToFill > 0)
                    {
                        FillEnemyInventoryForModeD(enemy, qualityLevel, remainingToFill);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] EquipEnemyForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 随机选择指定数量的物品大类
        /// </summary>
        private List<ItemCategory> SelectRandomCategories(int count)
        {
            List<ItemCategory> available = new List<ItemCategory>(allDropCategories);
            List<ItemCategory> selected = new List<ItemCategory>();

            for (int i = 0; i < count && available.Count > 0; i++)
            {
                int index = UnityEngine.Random.Range(0, available.Count);
                selected.Add(available[index]);
                available.RemoveAt(index);
            }

            return selected;
        }

        /// <summary>
        /// 根据类别给敌人发放物品到背包
        /// </summary>
        private void GiveEnemyItemByCategory(CharacterMainControl enemy, ItemCategory category, int qualityLevel)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                Inventory inventory = characterItem.Inventory;
                if (inventory == null) return;

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                Item item = null;

                switch (category)
                {
                    case ItemCategory.Weapon:
                        if (modeDWeaponPool.Count > 0)
                        {
                            int id = GetRandomItemByQuality(modeDWeaponPool, minQ, maxQ);
                            item = ItemAssetsCollection.InstantiateSync(id);
                            // 武器也随机加配件
                            if (item != null) TryAddRandomAttachmentsFullRandom(item);
                        }
                        break;

                    case ItemCategory.Accessory:
                        if (modeDAccessoryPool.Count == 0) InitializeAccessoryPool();
                        if (modeDAccessoryPool.Count > 0)
                        {
                            int id = modeDAccessoryPool[UnityEngine.Random.Range(0, modeDAccessoryPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Helmet:
                        if (modeDHelmetPool.Count > 0)
                        {
                            int id = GetRandomItemByQuality(modeDHelmetPool, minQ, maxQ);
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Armor:
                        if (modeDArmortPool.Count > 0)
                        {
                            int id = GetRandomItemByQuality(modeDArmortPool, minQ, maxQ);
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Ammo:
                        item = CreateRandomAmmo(minQ, maxQ);
                        if (item != null) item.StackCount = UnityEngine.Random.Range(30, 90);
                        break;

                    case ItemCategory.Medical:
                        if (modeDMedicalPool.Count > 0)
                        {
                            int id = modeDMedicalPool[UnityEngine.Random.Range(0, modeDMedicalPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Totem:
                        if (modeDTotemPool.Count > 0)
                        {
                            int id = modeDTotemPool[UnityEngine.Random.Range(0, modeDTotemPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Mask:
                        if (modeDMaskPool.Count > 0)
                        {
                            int id = modeDMaskPool[UnityEngine.Random.Range(0, modeDMaskPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;
                }

                if (item != null)
                {
                    inventory.AddAndMerge(item, 0);
                    DevLog("[ModeD] 敌人掉落物: " + category + " - " + item.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveEnemyItemByCategory 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 根据类别给敌人发放物品到背包（不包含武器和弹药）
        /// </summary>
        private void GiveEnemyItemByCategoryNoWeapon(CharacterMainControl enemy, ItemCategory category, int qualityLevel)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                Inventory inventory = characterItem.Inventory;
                if (inventory == null) return;

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                Item item = null;

                switch (category)
                {
                    case ItemCategory.Accessory:
                        if (modeDAccessoryPool.Count == 0) InitializeAccessoryPool();
                        if (modeDAccessoryPool.Count > 0)
                        {
                            int id = modeDAccessoryPool[UnityEngine.Random.Range(0, modeDAccessoryPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Helmet:
                        if (modeDHelmetPool.Count > 0)
                        {
                            int id = GetRandomItemByQuality(modeDHelmetPool, minQ, maxQ);
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Armor:
                        if (modeDArmortPool.Count > 0)
                        {
                            int id = GetRandomItemByQuality(modeDArmortPool, minQ, maxQ);
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Medical:
                        if (modeDMedicalPool.Count > 0)
                        {
                            int id = modeDMedicalPool[UnityEngine.Random.Range(0, modeDMedicalPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Totem:
                        if (modeDTotemPool.Count > 0)
                        {
                            int id = modeDTotemPool[UnityEngine.Random.Range(0, modeDTotemPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    case ItemCategory.Mask:
                        if (modeDMaskPool.Count > 0)
                        {
                            int id = modeDMaskPool[UnityEngine.Random.Range(0, modeDMaskPool.Count)];
                            item = ItemAssetsCollection.InstantiateSync(id);
                        }
                        break;

                    // 不处理武器和弹药类型
                    case ItemCategory.Weapon:
                    case ItemCategory.Ammo:
                    default:
                        break;
                }

                if (item != null)
                {
                    inventory.AddAndMerge(item, 0);
                    DevLog("[ModeD] 敌人掉落物: " + category + " - " + item.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveEnemyItemByCategoryNoWeapon 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 尝试将敌人背包进一步填满一些（仅 Mode D 使用）
        /// </summary>
        private void FillEnemyInventoryForModeD(CharacterMainControl enemy, int qualityLevel, int maxItemsToAdd)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                Inventory inventory = characterItem.Inventory;
                if (inventory == null) return;

                if (maxItemsToAdd <= 0)
                {
                    return;
                }

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                // 依据 Inventory.GetFirstEmptyPosition(0) 一直塞，直到没有空位或达到安全上限
                int safety = 60;
                int loops = 0;

                while (loops < safety && loops < maxItemsToAdd)
                {
                    int firstEmpty = -1;
                    try
                    {
                        firstEmpty = inventory.GetFirstEmptyPosition(0);
                    }
                    catch {}

                    if (firstEmpty < 0)
                    {
                        break; // 背包已满
                    }

                    Item randomItem = CreateRandomGlobalItemForModeD(minQ, maxQ);
                    if (randomItem != null)
                    {
                        inventory.AddAndMerge(randomItem, 0);
                    }

                    loops++;
                }

                int finalCount = 0;
                try
                {
                    if (inventory.Content != null)
                    {
                        finalCount = inventory.Content.Count;
                    }
                }
                catch {}

                DevLog("[ModeD] 背包填充完成，物品数量: " + finalCount);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] FillEnemyInventoryForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 创建随机弹药（品质 1-5 和 6-8 之间约 70% / 30% 概率分布）
        /// </summary>
        private Item CreateRandomAmmo(int minQ, int maxQ)
        {
            try
            {
                Duckov.Utilities.Tag bulletTag = GameplayDataSettings.Tags.Bullet;
                if (bulletTag == null) return null;

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { bulletTag };

                int[] lowIds = null;
                int[] highIds = null;

                int lowMin = minQ;
                int lowMax = Mathf.Min(5, maxQ);
                if (lowMin <= lowMax)
                {
                    filter.minQuality = lowMin;
                    filter.maxQuality = lowMax;
                    lowIds = ItemAssetsCollection.Search(filter);
                }

                int highMin = Mathf.Max(6, minQ);
                int highMax = Mathf.Min(8, maxQ);
                if (highMin <= highMax)
                {
                    filter.minQuality = highMin;
                    filter.maxQuality = highMax;
                    highIds = ItemAssetsCollection.Search(filter);
                }

                if ((lowIds == null || lowIds.Length == 0) && (highIds == null || highIds.Length == 0))
                {
                    filter.minQuality = minQ;
                    filter.maxQuality = maxQ;
                    int[] ids = ItemAssetsCollection.Search(filter);
                    if (ids != null && ids.Length > 0)
                    {
                        int id = ids[UnityEngine.Random.Range(0, ids.Length)];
                        return ItemAssetsCollection.InstantiateSync(id);
                    }
                    return null;
                }

                float lowWeight = (lowIds != null && lowIds.Length > 0) ? 0.85f : 0f;
                float highWeight = (highIds != null && highIds.Length > 0) ? 0.15f : 0f;

                float totalWeight = lowWeight + highWeight;
                if (totalWeight <= 0f)
                {
                    return null;
                }

                float roll = UnityEngine.Random.value * totalWeight;
                int[] chosen = null;

                if (roll < lowWeight && lowIds != null && lowIds.Length > 0)
                {
                    chosen = lowIds;
                }
                else if (highIds != null && highIds.Length > 0)
                {
                    chosen = highIds;
                }
                else if (lowIds != null && lowIds.Length > 0)
                {
                    chosen = lowIds;
                }

                if (chosen != null && chosen.Length > 0)
                {
                    int id = chosen[UnityEngine.Random.Range(0, chosen.Length)];
                    return ItemAssetsCollection.InstantiateSync(id);
                }
            }
            catch {}
            return null;
        }

        /// <summary>
        /// P1-10 优化：给敌人装备武器（装在手上用于战斗），检查返回值防止泄漏
        /// </summary>
        private void GiveEnemyEquippedWeapon(CharacterMainControl enemy, int qualityLevel)
        {
            try
            {
                if (modeDWeaponPool.Count == 0) return;

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                int weaponId = GetRandomItemByQuality(modeDWeaponPool, minQ, maxQ);
                Item weapon = ItemAssetsCollection.InstantiateSync(weaponId);
                if (weapon == null) return;

                // 随机加配件
                TryAddRandomAttachmentsFullRandom(weapon);

                // P1-10 修复：检查 TryPlug 返回值，失败时处理武器防止泄漏
                bool equipped = enemy.CharacterItem.TryPlug(weapon, true, null, 0);
                if (!equipped)
                {
                    // 装备失败，尝试放入背包
                    Inventory inventory = enemy.CharacterItem.Inventory;
                    if (inventory != null)
                    {
                        inventory.AddAndMerge(weapon, 0);
                    }
                    else
                    {
                        // 背包也不存在，销毁武器防止泄漏
                        UnityEngine.Object.Destroy(weapon.gameObject);
                        DevLog("[ModeD] [WARNING] 敌人武器装备失败且无背包，已销毁武器");
                        return;
                    }
                }

                // 给弹药和填满弹夹
                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting != null)
                {
                    if (gunSetting.TargetBulletID < 0)
                    {
                        // 为武器自动选择一个可用弹药类型
                        EnsureStarterGunHasBulletType(weapon);
                    }

                    if (gunSetting.TargetBulletID >= 0)
                    {
                        // 填满弹夹
                        FillGunMagazine(weapon);

                        // 注意：不再调用 FillGunInternalAmmo，避免敌人掉落的武器内有大量子弹
                        // 原代码：FillGunInternalAmmo(weapon, 2, 30, 60);
                        // 这会导致玩家捡到的枪里有60-120发额外子弹（如狙击枪80发）

                        // P1-4 修复：给背包弹药（供敌人战斗使用，会随尸体掉落），检查 Inventory 是否为 null
                        Inventory enemyInventory = enemy.CharacterItem.Inventory;
                        if (enemyInventory != null)
                        {
                            Item ammo = ItemAssetsCollection.InstantiateSync(gunSetting.TargetBulletID);
                            if (ammo != null)
                            {
                                ammo.StackCount = UnityEngine.Random.Range(30, 60);
                                enemyInventory.AddAndMerge(ammo, 0);
                            }
                        }
                    }
                }

                DevLog("[ModeD] 敌人装备武器: " + weapon.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveEnemyEquippedWeapon 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 计算装备品质等级
        /// </summary>
        private int CalculateQualityLevel(int waveIndex, float enemyHealth)
        {
            // 基础品质：波次越高越好
            int baseQuality = 1 + (waveIndex / 5); // 每5波提升1级

            // 血量加成：血量越高越好
            int healthBonus = (int)(enemyHealth / 500f); // 每500血+1级

            int quality = baseQuality + healthBonus;

            // 限制在1-6范围
            return Mathf.Clamp(quality, 1, 6);
        }

        /// <summary>
        /// P1-9 优化：清空敌人背包和装备（移除 ToArray() 避免 GC）
        /// </summary>
        /// <param name="enemy">敌人角色</param>
        /// <param name="preserveHelmetAndArmor">是否保留头盔和护甲（Boss专用）</param>
        private void ClearEnemyInventory(CharacterMainControl enemy, bool preserveHelmetAndArmor = false)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                // 清空背包 - P1-9 修复：使用倒序 for 循环代替 ToArray()
                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    var content = inventory.Content;
                    // 倒序遍历，避免在移除元素时出现问题
                    for (int i = content.Count - 1; i >= 0; --i)
                    {
                        var item = content[i];
                        if (item != null)
                        {
                            item.Detach();
                            UnityEngine.Object.Destroy(item.gameObject);
                        }
                    }
                }

                // 清空装备槽（如果 preserveHelmetAndArmor 为 true，则跳过头盔和护甲槽）
                foreach (Slot slot in characterItem.Slots)
                {
                    if (slot != null && slot.Content != null)
                    {
                        // 如果需要保留头盔和护甲，检查槽位类型
                        if (preserveHelmetAndArmor)
                        {
                            // 通过槽位 Key 判断是否为头盔或护甲槽
                            // 游戏中头盔槽 Key 为 "Helmat"，护甲槽 Key 为 "Armor"
                            string slotKey = slot.Key ?? "";
                            bool isHelmetOrArmorSlot = slotKey == "Helmat" || slotKey == "Armor";
                            if (isHelmetOrArmorSlot)
                            {
                                DevLog("[ModeD] 保留Boss头盔/护甲槽: " + slotKey);
                                continue; // 跳过此槽位
                            }
                        }

                        Item content = slot.Content;
                        slot.Unplug();
                        UnityEngine.Object.Destroy(content.gameObject);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] ClearEnemyInventory 失败: " + e.Message);
            }
        }
        
        #endregion
    }
}
