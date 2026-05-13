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
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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
                    ammo.StackCount = UnityEngine.Random.Range(120, 181);
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
        /// 检查物品预制体是否拥有指定 Tag
        /// </summary>
        private bool ItemTypeHasTag(int typeId, Duckov.Utilities.Tag requiredTag)
        {
            try
            {
                if (requiredTag == null)
                {
                    return true;
                }

                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab == null || prefab.Tags == null || prefab.Tags.Count == 0)
                {
                    return false;
                }

                if (prefab.Tags.Contains(requiredTag))
                {
                    return true;
                }

                foreach (Duckov.Utilities.Tag tag in prefab.Tags)
                {
                    if (tag != null && string.Equals(tag.name, requiredTag.name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch {}

            return false;
        }

        /// <summary>
        /// 开局医疗品额外要求带有 Healing Tag，避免混入无法治疗的消耗品
        /// </summary>
        private List<int> GetStarterMedicalPool()
        {
            Duckov.Utilities.Tag healingTag = FindTagByName("Healing");
            if (healingTag == null || modeDMedicalPool.Count == 0)
            {
                return modeDMedicalPool;
            }

            List<int> filteredPool = new List<int>(modeDMedicalPool.Count);
            for (int i = 0; i < modeDMedicalPool.Count; i++)
            {
                int typeId = modeDMedicalPool[i];
                if (ItemTypeHasTag(typeId, healingTag))
                {
                    filteredPool.Add(typeId);
                }
            }

            if (filteredPool.Count != modeDMedicalPool.Count)
            {
                DevLog("[ModeD] 开局医疗品池附加 Healing 过滤: " + modeDMedicalPool.Count + " -> " + filteredPool.Count);
            }

            return filteredPool;
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
                List<int> starterMedicalPool = GetStarterMedicalPool();

                for (int i = 0; i < count; i++)
                {
                    if (starterMedicalPool.Count == 0)
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
                        int medicalId = starterMedicalPool[UnityEngine.Random.Range(0, starterMedicalPool.Count)];
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

        #endregion
    }
}
