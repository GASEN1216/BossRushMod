// ============================================================================
// DragonDescendantBoss.cs - 龙裔遗族Boss主控制器
// ============================================================================
// 模块说明：
//   管理龙裔遗族Boss的生成、装备和生命周期
//   作为ModBehaviour的partial class实现
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;

namespace BossRush
{
    /// <summary>
    /// 龙裔遗族Boss主控制器（partial class）
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== Boss实例引用 ==========
        
        /// <summary>
        /// 龙裔遗族Boss实例
        /// </summary>
        private CharacterMainControl dragonDescendantInstance;
        
        /// <summary>
        /// 龙裔遗族能力控制器
        /// </summary>
        private DragonDescendantAbilityController dragonDescendantAbilities;
        
        /// <summary>
        /// 龙裔遗族是否已注册到预设列表 - 用于防止重复注册
        /// </summary>
        #pragma warning disable CS0414
        private static bool dragonDescendantRegistered = false;
        #pragma warning restore CS0414
        
        /// <summary>
        /// Boss龙套装效果是否已注册
        /// </summary>
        private bool dragonDescendantSetBonusRegistered = false;
        
        /// <summary>
        /// 缓存的Boss Health引用（用于快速身份验证）
        /// </summary>
        private Health cachedBossHealth;
        
        // ========== 生成方法 ==========
        
        /// <summary>
        /// 生成龙裔遗族Boss
        /// </summary>
        public async UniTask<CharacterMainControl> SpawnDragonDescendant(Vector3 position)
        {
            try
            {
                DevLog("[DragonDescendant] 开始生成龙裔遗族Boss at " + position);
                
                // 查找基础敌人预设
                CharacterRandomPreset basePreset = FindQuestionMarkPreset();
                
                if (basePreset == null)
                {
                    DevLog("[DragonDescendant] [ERROR] 未找到???敌人预设，使用后备方案");
                    basePreset = FindFallbackPreset();
                }
                
                if (basePreset == null)
                {
                    DevLog("[DragonDescendant] [ERROR] 无法找到任何可用预设");
                    // 通知BossRush系统生成失败，以便推进波次
                    try
                    {
                        // 查找龙裔遗族的预设信息
                        EnemyPresetInfo dragonPreset = null;
                        if (enemyPresets != null)
                        {
                            foreach (var p in enemyPresets)
                            {
                                if (p != null && p.name == DragonDescendantConfig.BOSS_NAME_KEY)
                                {
                                    dragonPreset = p;
                                    break;
                                }
                            }
                        }
                        OnBossSpawnFailed(dragonPreset);
                    }
                    catch {}
                    return null;
                }
                
                DevLog("[DragonDescendant] 使用预设: " + basePreset.name + " (nameKey=" + basePreset.nameKey + ")");
                
                // 生成角色（不修改原版预设）
                Vector3 dir = Vector3.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                var character = await basePreset.CreateCharacterAsync(position, dir, relatedScene, null, false);
                
                if (character == null)
                {
                    DevLog("[DragonDescendant] [ERROR] 生成角色失败");
                    // 通知BossRush系统生成失败，以便推进波次
                    try
                    {
                        EnemyPresetInfo dragonPreset = null;
                        if (enemyPresets != null)
                        {
                            foreach (var p in enemyPresets)
                            {
                                if (p != null && p.name == DragonDescendantConfig.BOSS_NAME_KEY)
                                {
                                    dragonPreset = p;
                                    break;
                                }
                            }
                        }
                        OnBossSpawnFailed(dragonPreset);
                    }
                    catch {}
                    return null;
                }
                
                dragonDescendantInstance = character;
                character.gameObject.name = "BossRush_DragonDescendant";
                
                // 关键：将龙裔遗族设置为当前Boss，以便BossRush系统能够追踪死亡事件
                currentBoss = character;
                
                // 多Boss模式下，将龙裔遗族加入当前波列表
                if (bossesPerWave > 1 && currentWaveBosses != null && !currentWaveBosses.Contains(character))
                {
                    currentWaveBosses.Add(character);
                }
                
                // 为该角色创建独立的预设副本，避免修改原版预设
                if (character.characterPreset != null)
                {
                    // 创建预设副本
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    customPreset.name = "DragonDescendant_Preset";
                    customPreset.showName = true;
                    customPreset.showHealthBar = true;
                    customPreset.nameKey = DragonDescendantConfig.BOSS_NAME_KEY;
                    
                    // 将副本赋值给角色
                    character.characterPreset = customPreset;
                    
                    DevLog("[DragonDescendant] 已创建独立预设副本, nameKey=" + customPreset.nameKey + 
                        ", showName=" + customPreset.showName + ", showHealthBar=" + customPreset.showHealthBar +
                        ", DisplayName=" + customPreset.DisplayName);
                }
                
                // 设置Health组件的血条显示属性
                if (character.Health != null)
                {
                    character.Health.showHealthBar = true;
                    DevLog("[DragonDescendant] 已设置 Health.showHealthBar = true");
                }
                
                // 设置Boss属性
                SetupBossAttributes(character);
                
                // 关键：在装备龙息武器之前，从角色已装备的原始武器获取完整属性（二阶段使用）
                OriginalWeaponData originalWeaponData = GetWeaponDataFromEquippedWeapon(character);
                
                // 装备武器和护甲
                await EquipDragonDescendant(character);
                
                // 添加能力控制器（传入原始武器完整属性）
                dragonDescendantAbilities = character.gameObject.AddComponent<DragonDescendantAbilityController>();
                dragonDescendantAbilities.Initialize(character, originalWeaponData);
                
                // 激活角色
                character.gameObject.SetActive(true);
                
                // 请求显示血条（必须在角色激活后调用）
                if (character.Health != null)
                {
                    character.Health.RequestHealthBar();
                    DevLog("[DragonDescendant] 已调用 RequestHealthBar()");
                }
                
                // 设置AI仇恨
                SetupAIAggro(character);
                
                // 订阅死亡事件（使用实例事件）
                if (character.Health != null)
                {
                    character.Health.OnDeadEvent.AddListener(OnDragonDescendantDeath);
                }
                
                // 注册龙套装效果（火焰伤害免疫）
                RegisterDragonDescendantSetBonus();
                
                // 记录 Boss 生成时间和原始掉落数量（用于掉落随机化）
                try
                {
                    bossSpawnTimes[character] = Time.time + 1f;
                    
                    // 记录原始掉落物品数量
                    int originalLootCount = 3; // 默认基础掉落数量
                    bossOriginalLootCounts[character] = originalLootCount;
                    
                    // 关键：订阅 Boss 的掉落事件（使用lambda捕获Boss引用）
                    character.BeforeCharacterSpawnLootOnDead += (dmgInfo) => OnBossBeforeSpawnLoot(character, dmgInfo);
                    
                    DevLog("[DragonDescendant] 记录 Boss 生成信息并订阅掉落事件 - 时间: " + Time.time + ", 原始掉落数量: " + originalLootCount);
                }
                catch (Exception recordEx)
                {
                    DevLog("[DragonDescendant] [WARNING] 记录 Boss 生成信息失败: " + recordEx.Message);
                }
                
                DevLog("[DragonDescendant] 龙裔遗族Boss生成完成");
                ShowMessage(L10n.T("龙裔遗族 出现了！", "Dragon Descendant has appeared!"));
                
                // 返回生成的角色引用，供BossRush系统验证
                return character;
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 生成Boss失败: " + e.Message + "\n" + e.StackTrace);
                return null;
            }
        }
        
        // ========== 性能优化：预设缓存 ==========
        // 避免每次生成Boss时都调用Resources.FindObjectsOfTypeAll
        
        /// <summary>
        /// 缓存的???敌人预设
        /// </summary>
        private static CharacterRandomPreset cachedQuestionMarkPreset = null;
        
        /// <summary>
        /// 是否已搜索过???预设
        /// </summary>
        private static bool questionMarkPresetSearched = false;
        
        /// <summary>
        /// 查找"???"敌人预设（带缓存）
        /// [性能优化] 使用EnemySpawner.AllPresets替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private CharacterRandomPreset FindQuestionMarkPreset()
        {
            // 使用缓存
            if (cachedQuestionMarkPreset != null) return cachedQuestionMarkPreset;
            if (questionMarkPresetSearched) return null;
            
            questionMarkPresetSearched = true;
            
            try
            {
                // 直接使用Resources.FindObjectsOfTypeAll获取预设列表
                // 此方法仅在Boss生成时调用一次，不影响战斗性能
                var presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                
                // 遍历查找???预设
                foreach (var preset in presets)
                {
                    if (preset == null) continue;
                    
                    // 通过nameKey匹配
                    if (preset.nameKey == DragonDescendantConfig.BasePresetNameKey ||
                        preset.nameKey == "???" ||
                        preset.Name == "???" ||
                        preset.DisplayName == "???")
                    {
                        cachedQuestionMarkPreset = preset;
                        return preset;
                    }
                }
                
                // 尝试通过名称模糊匹配
                foreach (var preset in presets)
                {
                    if (preset == null) continue;
                    
                    if (preset.name.Contains("???") || 
                        preset.name.Contains("Question") ||
                        preset.name.Contains("Unknown"))
                    {
                        cachedQuestionMarkPreset = preset;
                        return preset;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 查找???预设失败: " + e.Message);
            }
            
            return null;
        }
        
        /// <summary>
        /// 缓存的后备预设
        /// </summary>
        private static CharacterRandomPreset cachedFallbackPreset = null;
        
        /// <summary>
        /// 是否已搜索过后备预设
        /// </summary>
        private static bool fallbackPresetSearched = false;
        
        /// <summary>
        /// 清理龙裔遗族相关的所有静态缓存（场景切换时调用，防止持有已销毁对象引用）
        /// </summary>
        public static void ClearDragonDescendantStaticCache()
        {
            // 清理预设缓存
            cachedQuestionMarkPreset = null;
            questionMarkPresetSearched = false;
            cachedFallbackPreset = null;
            fallbackPresetSearched = false;
            
            // 清理物品缓存
            cachedItemsByName.Clear();
            cachedBulletsByCaliber.Clear();
            
            // 清理武器配置缓存
            DragonBreathWeaponConfig.ClearStaticCache();
            
            // 清理Buff处理器缓存
            DragonBreathBuffHandler.ClearStaticCache();
        }
        
        /// <summary>
        /// 查找后备预设（任意showName=true的敌人）（带缓存）
        /// [性能优化] 复用FindQuestionMarkPreset的预设列表获取逻辑
        /// </summary>
        private CharacterRandomPreset FindFallbackPreset()
        {
            // 使用缓存
            if (cachedFallbackPreset != null) return cachedFallbackPreset;
            if (fallbackPresetSearched) return null;
            
            fallbackPresetSearched = true;
            
            try
            {
                // 直接使用Resources.FindObjectsOfTypeAll获取预设列表
                // 此方法仅在Boss生成时调用一次，不影响战斗性能
                var presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                
                foreach (var preset in presets)
                {
                    if (preset == null) continue;
                    
                    // 查找显示名字的敌人（通常是精英/Boss）
                    if (preset.showName && preset.team != Teams.player)
                    {
                        cachedFallbackPreset = preset;
                        return preset;
                    }
                }
                
                // 如果没有showName的，返回任意非玩家预设
                foreach (var preset in presets)
                {
                    if (preset == null) continue;
                    if (preset.team != Teams.player)
                    {
                        cachedFallbackPreset = preset;
                        return preset;
                    }
                }
            }
            catch { }
            
            return null;
        }
        
        /// <summary>
        /// 原始武器属性数据（用于二阶段射击）
        /// </summary>
        public class OriginalWeaponData
        {
            public Projectile bulletPrefab;      // 子弹预制体
            public GameObject muzzleFxPrefab;    // 枪口特效预制体
            public string shootKey;              // 开枪音效键
            public float bulletSpeed;            // 子弹速度
            public float shootSpeed;             // 射速（每秒发射数）
            public float damage;                 // 伤害
            public float bulletDistance;         // 子弹射程
        }
        
        /// <summary>
        /// 从角色已装备的武器获取完整属性（在替换为龙息武器之前调用）
        /// 用于二阶段发射原始武器的子弹
        /// </summary>
        private OriginalWeaponData GetWeaponDataFromEquippedWeapon(CharacterMainControl character)
        {
            try
            {
                if (character == null) return null;
                
                OriginalWeaponData data = new OriginalWeaponData();
                
                // 从角色当前手持的枪获取属性
                var gun = character.GetGun();
                if (gun != null)
                {
                    // 获取射速、子弹速度、伤害等属性
                    data.bulletSpeed = gun.BulletSpeed;
                    data.shootSpeed = gun.ShootSpeed;
                    data.damage = gun.Damage;
                    data.bulletDistance = gun.BulletDistance;
                    
                    // 通过反射获取GunItemSetting
                    var gunSettingProp = gun.GetType().GetProperty("GunItemSetting");
                    if (gunSettingProp != null)
                    {
                        var gunSetting = gunSettingProp.GetValue(gun) as ItemSetting_Gun;
                        if (gunSetting != null)
                        {
                            data.bulletPrefab = gunSetting.bulletPfb;
                            data.muzzleFxPrefab = gunSetting.muzzleFxPfb;
                            data.shootKey = gunSetting.shootKey;
                            
                            DevLog("[DragonDescendant] 从角色手持武器获取完整属性: " +
                                "子弹=" + (data.bulletPrefab != null ? data.bulletPrefab.name : "null") +
                                ", 射速=" + data.shootSpeed +
                                ", 子弹速度=" + data.bulletSpeed +
                                ", 伤害=" + data.damage +
                                ", 音效=" + data.shootKey);
                            return data;
                        }
                    }
                    
                    // 尝试直接从gun.Item获取
                    if (gun.Item != null)
                    {
                        var itemGunSetting = gun.Item.GetComponent<ItemSetting_Gun>();
                        if (itemGunSetting != null)
                        {
                            data.bulletPrefab = itemGunSetting.bulletPfb;
                            data.muzzleFxPrefab = itemGunSetting.muzzleFxPfb;
                            data.shootKey = itemGunSetting.shootKey;
                            
                            DevLog("[DragonDescendant] 从角色武器Item获取完整属性: " +
                                "子弹=" + (data.bulletPrefab != null ? data.bulletPrefab.name : "null") +
                                ", 射速=" + data.shootSpeed +
                                ", 音效=" + data.shootKey);
                            return data;
                        }
                    }
                }
                
                // 方法2：从主武器槽位获取
                var primSlot = character.PrimWeaponSlot();
                if (primSlot != null && primSlot.Content != null)
                {
                    var weaponItem = primSlot.Content;
                    var itemGunSetting = weaponItem.GetComponent<ItemSetting_Gun>();
                    if (itemGunSetting != null)
                    {
                        data.bulletPrefab = itemGunSetting.bulletPfb;
                        data.muzzleFxPrefab = itemGunSetting.muzzleFxPfb;
                        data.shootKey = itemGunSetting.shootKey;
                        
                        // 从Item的Stats获取数值属性
                        data.bulletSpeed = weaponItem.GetStatValue("BulletSpeed".GetHashCode());
                        data.shootSpeed = weaponItem.GetStatValue("ShootSpeed".GetHashCode());
                        data.damage = weaponItem.GetStatValue("Damage".GetHashCode());
                        data.bulletDistance = weaponItem.GetStatValue("BulletDistance".GetHashCode());
                        
                        // 设置默认值
                        if (data.bulletSpeed <= 0) data.bulletSpeed = 30f;
                        if (data.shootSpeed <= 0) data.shootSpeed = 5f;
                        if (data.damage <= 0) data.damage = 15f;
                        if (data.bulletDistance <= 0) data.bulletDistance = 50f;
                        
                        DevLog("[DragonDescendant] 从主武器槽位获取完整属性: " +
                            "子弹=" + (data.bulletPrefab != null ? data.bulletPrefab.name : "null") +
                            ", 武器=" + weaponItem.name +
                            ", 射速=" + data.shootSpeed +
                            ", 音效=" + data.shootKey);
                        return data;
                    }
                }
                
                DevLog("[DragonDescendant] [WARNING] 未能从角色已装备武器获取属性");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 从角色武器获取属性失败: " + e.Message);
            }
            
            return null;
        }

        
        /// <summary>
        /// 设置Boss属性
        /// </summary>
        private void SetupBossAttributes(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null) return;
                
                var item = character.CharacterItem;
                
                // 设置血量
                var healthStat = item.GetStat("MaxHealth");
                if (healthStat != null)
                {
                    healthStat.BaseValue = DragonDescendantConfig.BaseHealth;
                }
                
                // 恢复满血
                if (character.Health != null)
                {
                    character.Health.SetHealth(DragonDescendantConfig.BaseHealth);
                }
                
                // 设置伤害倍率
                var gunDmgStat = item.GetStat("GunDamageMultiplier");
                if (gunDmgStat != null)
                {
                    gunDmgStat.BaseValue = DragonDescendantConfig.DamageMultiplier;
                }
                
                var meleeDmgStat = item.GetStat("MeleeDamageMultiplier");
                if (meleeDmgStat != null)
                {
                    meleeDmgStat.BaseValue = DragonDescendantConfig.DamageMultiplier;
                }
                
                DevLog("[DragonDescendant] Boss属性设置完成: HP=" + DragonDescendantConfig.BaseHealth + 
                    ", DmgMult=" + DragonDescendantConfig.DamageMultiplier);
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 设置Boss属性失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 装备龙裔遗族
        /// </summary>
        private UniTask EquipDragonDescendant(CharacterMainControl character)
        {
            try
            {
                if (character == null) return UniTask.CompletedTask;
                
                var characterItem = character.CharacterItem;
                if (characterItem == null)
                {
                    DevLog("[DragonDescendant] [WARNING] CharacterItem is null");
                    return UniTask.CompletedTask;
                }
                
                // 装备龙头（优先使用TypeID，其次使用名称）
                Item helmItem = FindItemByTypeId(DragonDescendantConfig.DRAGON_HELM_TYPE_ID);
                if (helmItem == null)
                {
                    helmItem = FindItemByName(DragonDescendantConfig.DRAGON_HELM_NAME);
                }
                if (helmItem != null)
                {
                    EquipArmorItem(character, helmItem, "Helmat".GetHashCode());
                }
                else
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到龙头装备");
                }
                
                // 装备龙甲（优先使用TypeID，其次使用名称）
                Item armorItem = FindItemByTypeId(DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID);
                if (armorItem == null)
                {
                    armorItem = FindItemByName(DragonDescendantConfig.DRAGON_ARMOR_NAME);
                }
                if (armorItem != null)
                {
                    EquipArmorItem(character, armorItem, "Armor".GetHashCode());
                }
                else
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到龙甲装备");
                }
                
                // 装备龙息武器（替换原有武器）
                EquipDragonBreathWeapon(character);
                
                // 刷新装备模型显示
                RefreshEquipmentModels(character);
                
                // 加载最高级子弹（BR口径）
                LoadHighestTierAmmo(character);
                
                DevLog("[DragonDescendant] 装备完成");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 装备失败: " + e.Message);
            }
            return UniTask.CompletedTask;
        }
        
        /// <summary>
        /// 装备龙息武器（替换Boss原有武器）
        /// </summary>
        private void EquipDragonBreathWeapon(CharacterMainControl character)
        {
            try
            {
                if (character == null) return;
                
                // 获取主武器槽位
                var primSlot = character.PrimWeaponSlot();
                if (primSlot == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到主武器槽位");
                    return;
                }
                
                // 移除原有武器
                Item oldWeapon = primSlot.Content;
                if (oldWeapon != null)
                {
                    DevLog("[DragonDescendant] 移除原有武器: " + oldWeapon.name + " (TypeID=" + oldWeapon.TypeID + ")");
                    // Unplug() 直接返回Item，不需要out参数
                    Item unpluggedItem = primSlot.Unplug();
                    // 销毁原有武器实例
                    if (unpluggedItem != null)
                    {
                        UnityEngine.Object.Destroy(unpluggedItem.gameObject);
                    }
                }
                
                // 使用 ItemAssetsCollection.InstantiateSync 创建龙息武器实例
                Item dragonBreathItem = ItemAssetsCollection.InstantiateSync(DragonDescendantConfig.DRAGON_BREATH_TYPE_ID);
                if (dragonBreathItem == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 创建龙息武器实例失败 (TypeID=" + DragonDescendantConfig.DRAGON_BREATH_TYPE_ID + ")");
                    return;
                }
                
                // 配置龙息武器属性
                DragonBreathWeaponConfig.ConfigureWeapon(dragonBreathItem);
                
                // 添加到库存
                var inventory = character.CharacterItem.Inventory;
                if (inventory != null)
                {
                    inventory.AddItem(dragonBreathItem);
                }
                
                // 装备到主武器槽
                Item pluggedOut;
                primSlot.Plug(dragonBreathItem, out pluggedOut);
                
                // 让Boss手持武器
                character.ChangeHoldItem(dragonBreathItem);
                
                // 为Boss的龙息武器添加火焰特效（从带火AK-47复制）
                TryAddFireEffectsToBossWeapon(character, dragonBreathItem);
                
                DevLog("[DragonDescendant] 已装备龙息武器 (TypeID=" + DragonDescendantConfig.DRAGON_BREATH_TYPE_ID + ")");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 装备龙息武器失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 为Boss的龙息武器添加火焰特效
        /// </summary>
        private void TryAddFireEffectsToBossWeapon(CharacterMainControl character, Item dragonBreathItem)
        {
            try
            {
                if (character == null || dragonBreathItem == null) return;
                
                // 获取Boss手持的武器Agent
                var gun = character.GetGun();
                if (gun != null && gun.Item == dragonBreathItem)
                {
                    // 调用DragonBreathWeaponConfig的火焰特效添加方法
                    DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gun);
                    DevLog("[DragonDescendant] 已为Boss龙息武器添加火焰特效");
                }
                else
                {
                    DevLog("[DragonDescendant] [WARNING] 无法获取Boss的龙息武器Agent，火焰特效添加失败");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 添加火焰特效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 通过TypeID查找物品预制体
        /// [性能优化] 使用ItemAssetsCollection替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private Item FindItemByTypeId(int typeId)
        {
            try
            {
                // [性能优化] 优先使用ItemAssetsCollection.GetPrefab获取预制体
                var prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    DevLog("[DragonDescendant] 通过TypeID找到物品: " + prefab.name + " (TypeID=" + typeId + ")");
                    return prefab;
                }
                
                // 后备方案：遍历ItemAssetsCollection.entries
                var itemAssets = ItemAssetsCollection.Instance;
                if (itemAssets != null && itemAssets.entries != null)
                {
                    foreach (var entry in itemAssets.entries)
                    {
                        if (entry.prefab != null && entry.prefab.TypeID == typeId)
                        {
                            DevLog("[DragonDescendant] 通过entries找到物品: " + entry.prefab.name + " (TypeID=" + typeId + ")");
                            return entry.prefab;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 通过TypeID查找物品失败: " + e.Message);
            }
            return null;
        }
        
        /// <summary>
        /// 装备护甲物品（直接传入Item）
        /// </summary>
        private void EquipArmorItem(CharacterMainControl character, Item armorItem, int slotHash)
        {
            try
            {
                if (character == null || armorItem == null) return;
                
                var inventory = character.CharacterItem.Inventory;
                if (inventory != null)
                {
                    var newItem = armorItem.CreateInstance();
                    if (newItem != null)
                    {
                        inventory.AddItem(newItem);
                        
                        // 使用hash值获取槽位并装备
                        var slot = character.CharacterItem.Slots.GetSlot(slotHash);
                        if (slot != null)
                        {
                            Item unpluggedItem;
                            slot.Plug(newItem, out unpluggedItem);
                            DevLog("[DragonDescendant] 装备护甲成功: " + armorItem.name + " -> slot hash " + slotHash);
                        }
                        else
                        {
                            DevLog("[DragonDescendant] [WARNING] 未找到槽位 hash: " + slotHash);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 装备护甲物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 刷新装备模型显示
        /// </summary>
        private void RefreshEquipmentModels(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null) return;
                
                // 获取CharacterEquipmentController并刷新
                var equipController = character.GetComponent<CharacterEquipmentController>();
                if (equipController != null)
                {
                    // 重新设置Item会触发所有装备槽位的模型更新
                    equipController.SetItem(character.CharacterItem);
                    DevLog("[DragonDescendant] 已刷新装备模型");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 刷新装备模型失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 装备武器
        /// </summary>
        private UniTask EquipWeapon(CharacterMainControl character, string weaponName)
        {
            try
            {
                // 查找武器物品
                Item weaponItem = FindItemByName(weaponName);
                if (weaponItem == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到武器: " + weaponName);
                    return UniTask.CompletedTask;
                }
                
                // 创建武器实例并添加到库存
                var inventory = character.CharacterItem.Inventory;
                if (inventory != null)
                {
                    // 使用Item.CreateInstance创建物品实例
                    var newItem = weaponItem.CreateInstance();
                    if (newItem != null)
                    {
                        inventory.AddItem(newItem);
                        
                        // 尝试装备到主武器槽
                        try
                        {
                            // 获取主武器槽并装备
                            var primarySlot = character.CharacterItem.Slots.GetSlot("Primary");
                            if (primarySlot != null)
                            {
                                Item unpluggedItem;
                                primarySlot.Plug(newItem, out unpluggedItem);
                            }
                        }
                        catch { }
                        
                        DevLog("[DragonDescendant] 装备武器: " + weaponName);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 装备武器失败: " + e.Message);
            }
            return UniTask.CompletedTask;
        }
        
        /// <summary>
        /// 装备护甲（使用字符串槽位名）
        /// </summary>
        private UniTask EquipArmor(CharacterMainControl character, string armorName, string slotTag)
        {
            return EquipArmorWithHash(character, armorName, slotTag.GetHashCode());
        }
        
        /// <summary>
        /// 装备护甲（使用hash值槽位）
        /// </summary>
        private UniTask EquipArmorWithHash(CharacterMainControl character, string armorName, int slotHash)
        {
            try
            {
                // 查找护甲物品
                Item armorItem = FindItemByName(armorName);
                if (armorItem == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到护甲: " + armorName);
                    return UniTask.CompletedTask;
                }
                
                // 创建护甲实例
                var inventory = character.CharacterItem.Inventory;
                if (inventory != null)
                {
                    var newItem = armorItem.CreateInstance();
                    if (newItem != null)
                    {
                        inventory.AddItem(newItem);
                        
                        // 使用hash值获取槽位并装备
                        try
                        {
                            var slot = character.CharacterItem.Slots.GetSlot(slotHash);
                            if (slot != null)
                            {
                                Item unpluggedItem;
                                slot.Plug(newItem, out unpluggedItem);
                                DevLog("[DragonDescendant] 装备护甲成功: " + armorName + " -> slot hash " + slotHash);
                            }
                            else
                            {
                            DevLog("[DragonDescendant] [WARNING] 未找到槽位 hash: " + slotHash);
                            }
                        }
                        catch (Exception slotEx)
                        {
                            DevLog("[DragonDescendant] [WARNING] 装备到槽位失败: " + slotEx.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 装备护甲失败: " + e.Message);
            }
            return UniTask.CompletedTask;
        }
        
        /// <summary>
        /// 加载最高级子弹并装填到武器
        /// </summary>
        private UniTask LoadHighestTierAmmo(CharacterMainControl character)
        {
            try
            {
                // 获取当前武器
                var gun = character.GetGun();
                if (gun == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 无法获取Boss武器，跳过弹药加载");
                    return UniTask.CompletedTask;
                }
                
                var gunSetting = gun.GunItemSetting;
                if (gunSetting == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 武器没有GunItemSetting，跳过弹药加载");
                    return UniTask.CompletedTask;
                }
                
                // 获取武器口径
                string weaponCaliber = gun.Item.Constants.GetString("Caliber".GetHashCode(), null);
                if (string.IsNullOrEmpty(weaponCaliber))
                {
                    weaponCaliber = "BR"; // 龙息武器默认BR口径
                }
                DevLog("[DragonDescendant] 武器口径: " + weaponCaliber);
                
                // 查找匹配口径的子弹
                Item bestBullet = FindBulletByCaliber(weaponCaliber);
                if (bestBullet == null)
                {
                    DevLog("[DragonDescendant] [WARNING] 未找到口径 " + weaponCaliber + " 的子弹");
                    return UniTask.CompletedTask;
                }
                
                DevLog("[DragonDescendant] 找到子弹: " + bestBullet.name + " (TypeID=" + bestBullet.TypeID + ")");
                
                // 获取库存
                var inventory = character.CharacterItem.Inventory;
                if (inventory == null)
                {
                    DevLog("[DragonDescendant] [WARNING] Boss没有库存");
                    return UniTask.CompletedTask;
                }
                
                // 添加大量子弹到库存（每组30发，添加10组 = 300发）
                int ammoPerStack = 30;
                int stackCount = 10;
                for (int i = 0; i < stackCount; i++)
                {
                    var bulletItem = bestBullet.CreateInstance();
                    if (bulletItem != null)
                    {
                        bulletItem.StackCount = ammoPerStack;
                        inventory.AddItem(bulletItem);
                    }
                }
                DevLog("[DragonDescendant] 已添加 " + (ammoPerStack * stackCount) + " 发子弹到库存");
                
                // 设置武器的目标弹药类型
                gunSetting.SetTargetBulletType(bestBullet.TypeID);
                DevLog("[DragonDescendant] 已设置目标弹药类型: " + bestBullet.TypeID);
                
                // 直接装填弹药到武器弹夹（使用反射设置bulletCount）
                int capacity = gunSetting.Capacity;
                if (capacity <= 0) capacity = 20; // 默认容量
                
                // 通过Variables设置BulletCount
                int bulletCountHash = "BulletCount".GetHashCode();
                gun.Item.Variables.SetInt(bulletCountHash, capacity);
                
                // 同时在武器库存中添加对应数量的子弹实例（模拟已装填状态）
                if (gun.Item.Inventory != null)
                {
                    var loadedBullet = bestBullet.CreateInstance();
                    if (loadedBullet != null)
                    {
                        loadedBullet.StackCount = capacity;
                        gun.Item.Inventory.AddItem(loadedBullet);
                    }
                }
                
                DevLog("[DragonDescendant] 已装填 " + capacity + " 发子弹到弹夹");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 加载子弹失败: " + e.Message + "\n" + e.StackTrace);
            }
            return UniTask.CompletedTask;
        }
        
        // ========== 性能优化：子弹缓存 ==========
        
        /// <summary>
        /// 缓存的子弹（按口径）
        /// </summary>
        private static Dictionary<string, Item> cachedBulletsByCaliber = new Dictionary<string, Item>();
        
        /// <summary>
        /// 根据口径查找子弹（带缓存）
        /// [性能优化] 使用ItemAssetsCollection替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private Item FindBulletByCaliber(string caliber)
        {
            // 检查缓存
            Item cachedBullet;
            if (cachedBulletsByCaliber.TryGetValue(caliber, out cachedBullet))
            {
                return cachedBullet;
            }
            
            try
            {
                Item bestBullet = null;
                int bestQuality = -1;
                int caliberHash = "Caliber".GetHashCode();
                
                // 获取Bullet Tag
                Duckov.Utilities.Tag bulletTag = null;
                try
                {
                    bulletTag = Duckov.Utilities.GameplayDataSettings.Tags.Bullet;
                }
                catch { }
                
                // [性能优化] 使用ItemAssetsCollection遍历，避免Resources.FindObjectsOfTypeAll
                var itemAssets = ItemAssetsCollection.Instance;
                if (itemAssets != null && itemAssets.entries != null)
                {
                    foreach (var entry in itemAssets.entries)
                    {
                        if (entry == null || entry.prefab == null) continue;
                        var item = entry.prefab;
                        
                        // 检查是否是子弹（通过Tag判断）
                        if (bulletTag != null && !item.Tags.Contains(bulletTag)) continue;
                        
                        // 检查口径是否匹配
                        string itemCaliber = item.Constants.GetString(caliberHash, null);
                        if (string.IsNullOrEmpty(itemCaliber) || itemCaliber != caliber) continue;
                        
                        // 获取品质
                        int quality = 0;
                        try
                        {
                            quality = (int)item.Quality;
                        }
                        catch { }
                        
                        // 选择最高品质的子弹
                        if (bestBullet == null || quality > bestQuality)
                        {
                            bestQuality = quality;
                            bestBullet = item;
                        }
                    }
                }
                
                // 缓存结果
                if (bestBullet != null)
                {
                    cachedBulletsByCaliber[caliber] = bestBullet;
                }
                
                return bestBullet;
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 查找子弹失败: " + e.Message);
            }
            
            return null;
        }
        
        // ========== 性能优化：物品名称缓存 ==========
        
        /// <summary>
        /// 缓存的物品（按名称）
        /// </summary>
        private static Dictionary<string, Item> cachedItemsByName = new Dictionary<string, Item>();
        
        /// <summary>
        /// 通过名称查找物品（带缓存）
        /// [性能优化] 使用ItemAssetsCollection替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private Item FindItemByName(string itemName)
        {
            // 检查缓存
            Item cachedItem;
            if (cachedItemsByName.TryGetValue(itemName, out cachedItem))
            {
                return cachedItem;
            }
            
            try
            {
                // [性能优化] 使用ItemAssetsCollection遍历
                var itemAssets = ItemAssetsCollection.Instance;
                if (itemAssets == null || itemAssets.entries == null) return null;
                
                // 精确匹配
                foreach (var entry in itemAssets.entries)
                {
                    if (entry == null || entry.prefab == null) continue;
                    var item = entry.prefab;
                    
                    if (item.name == itemName || item.DisplayName == itemName)
                    {
                        cachedItemsByName[itemName] = item;
                        return item;
                    }
                }
                
                // 模糊匹配
                foreach (var entry in itemAssets.entries)
                {
                    if (entry == null || entry.prefab == null) continue;
                    var item = entry.prefab;
                    
                    if (item.name.Contains(itemName) || 
                        (item.DisplayName != null && item.DisplayName.Contains(itemName)))
                    {
                        cachedItemsByName[itemName] = item;
                        return item;
                    }
                }
            }
            catch { }
            
            return null;
        }
        

        
        /// <summary>
        /// 设置AI仇恨
        /// </summary>
        private void SetupAIAggro(CharacterMainControl character)
        {
            try
            {
                var main = CharacterMainControl.Main;
                if (main == null) return;
                
                var ai = character.GetComponentInChildren<AICharacterController>();
                if (ai != null && main.mainDamageReceiver != null)
                {
                    ai.forceTracePlayerDistance = 500f;
                    ai.searchedEnemy = main.mainDamageReceiver;
                    ai.SetTarget(main.mainDamageReceiver.transform);
                    ai.SetNoticedToTarget(main.mainDamageReceiver);
                    ai.noticed = true;
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 龙裔遗族死亡回调
        /// </summary>
        private void OnDragonDescendantDeath(DamageInfo damageInfo)
        {
            try
            {
                DevLog("[DragonDescendant] 龙裔遗族被击败");
                
                // 取消注册龙套装效果
                UnregisterDragonDescendantSetBonus();
                
                // 注意：龙套装掉落逻辑已移至 TryAddDragonSetToLootBeforeSpawn
                // 在 OnBossBeforeSpawnLoot_LootAndRewards 中调用，确保在掉落箱生成之前添加
                
                // 清理引用
                if (dragonDescendantInstance != null && dragonDescendantInstance.Health != null)
                {
                    dragonDescendantInstance.Health.OnDeadEvent.RemoveListener(OnDragonDescendantDeath);
                }
                
                dragonDescendantInstance = null;
                dragonDescendantAbilities = null;
                
                // 触发BossRush标准死亡处理（如果在BossRush模式中）
                // 这会由BossRush系统的OnEnemyDiedWithDamageInfo自动处理
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 死亡处理失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 清理龙裔遗族Boss
        /// </summary>
        public void CleanupDragonDescendant()
        {
            try
            {
                // 取消注册龙套装效果
                UnregisterDragonDescendantSetBonus();
                
                if (dragonDescendantInstance != null)
                {
                    if (dragonDescendantInstance.Health != null)
                    {
                        dragonDescendantInstance.Health.OnDeadEvent.RemoveListener(OnDragonDescendantDeath);
                    }
                    
                    UnityEngine.Object.Destroy(dragonDescendantInstance.gameObject);
                    dragonDescendantInstance = null;
                }
                
                dragonDescendantAbilities = null;
            }
            catch { }
        }
        
        // ========== BossRush系统集成 ==========
        
        /// <summary>
        /// 注册龙裔遗族到BossRush敌人预设系统
        /// 注意：每次 enemyPresets 被清空后都需要重新注册，所以不能依赖 static 标记
        /// </summary>
        private void RegisterDragonDescendantPreset()
        {
            try
            {
                if (enemyPresets == null)
                {
                    DevLog("[DragonDescendant] [WARNING] enemyPresets 为空，无法注册");
                    return;
                }
                
                // 检查是否已存在（每次都检查，因为 enemyPresets 可能被清空重建）
                bool exists = false;
                foreach (var p in enemyPresets)
                {
                    if (p != null && p.name == DragonDescendantConfig.BOSS_NAME_KEY)
                    {
                        exists = true;
                        break;
                    }
                }
                
                if (exists)
                {
                    DevLog("[DragonDescendant] 已存在于预设列表中，跳过注册");
                    return;
                }
                
                // 创建EnemyPresetInfo
                var presetInfo = new EnemyPresetInfo
                {
                    name = DragonDescendantConfig.BOSS_NAME_KEY,
                    displayName = DragonDescendantConfig.BOSS_NAME_CN,
                    team = (int)Teams.scav,
                    baseHealth = DragonDescendantConfig.BaseHealth,
                    baseDamage = 50f,
                    healthMultiplier = 1f,
                    damageMultiplier = DragonDescendantConfig.DamageMultiplier,
                    expReward = 500
                };
                
                enemyPresets.Add(presetInfo);
                dragonDescendantRegistered = true;
                DevLog("[DragonDescendant] 已注册到BossRush敌人预设系统");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 注册预设失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 检查是否是龙裔遗族预设
        /// </summary>
        private bool IsDragonDescendantPreset(EnemyPresetInfo preset)
        {
            if (preset == null) return false;
            return preset.name == DragonDescendantConfig.BOSS_NAME_KEY ||
                   preset.displayName == DragonDescendantConfig.BOSS_NAME_CN ||
                   preset.displayName == DragonDescendantConfig.BOSS_NAME_EN;
        }
        
        // ========== Boss龙套装效果 ==========
        
        /// <summary>
        /// 注册Boss龙套装效果（火焰伤害免疫）
        /// </summary>
        private void RegisterDragonDescendantSetBonus()
        {
            if (dragonDescendantSetBonusRegistered) return;
            
            try
            {
                // 缓存Health引用用于快速身份验证
                if (dragonDescendantInstance != null && dragonDescendantInstance.Health != null)
                {
                    cachedBossHealth = dragonDescendantInstance.Health;
                    Health.OnHurt += OnDragonDescendantHurt;
                    dragonDescendantSetBonusRegistered = true;
                    DevLog("[DragonDescendant] 已注册龙套装效果（火焰免疫），Health引用已缓存");
                }
                else
                {
                    DevLog("[DragonDescendant] [WARNING] 注册龙套装效果失败：Boss实例或Health为空");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 注册龙套装效果失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消注册Boss龙套装效果
        /// </summary>
        private void UnregisterDragonDescendantSetBonus()
        {
            if (!dragonDescendantSetBonusRegistered) return;
            
            try
            {
                Health.OnHurt -= OnDragonDescendantHurt;
                cachedBossHealth = null; // 清理缓存
                dragonDescendantSetBonusRegistered = false;
                DevLog("[DragonDescendant] 已取消注册龙套装效果");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 取消注册龙套装效果失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// Boss伤害事件回调 - 火焰伤害免疫并转化为治疗
        /// [性能优化] 使用缓存的Health引用进行快速身份验证
        /// </summary>
        private void OnDragonDescendantHurt(Health health, DamageInfo damageInfo)
        {
            // [性能优化] 快速过滤：使用缓存引用直接比较
            if (cachedBossHealth == null || health != cachedBossHealth) return;
            
            try
            {
                // 检查是否有火焰伤害（快速路径）
                if (damageInfo.elementFactors == null || damageInfo.elementFactors.Count == 0) return;
                
                // 使用 finalDamage 计算火焰伤害占比
                float totalFinalDamage = damageInfo.finalDamage;
                if (totalFinalDamage <= 0f) return;
                
                // 计算火焰伤害占比
                float fireFactor = 0f;
                float totalFactor = 0f;
                var factors = damageInfo.elementFactors;
                int count = factors.Count;
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.factor > 0f)
                    {
                        totalFactor += ef.factor;
                        if (ef.elementType == ElementTypes.fire)
                        {
                            fireFactor += ef.factor;
                        }
                    }
                }
                
                // 没有火焰伤害则跳过
                if (fireFactor <= 0f || totalFactor <= 0f) return;
                
                // 计算火焰伤害在最终伤害中的占比，全部转化为治疗
                float fireRatio = fireFactor / totalFactor;
                float actualFireDamage = totalFinalDamage * fireRatio;
                float fireHealAmount = actualFireDamage; // 100% 转化为治疗
                
                // 将火焰伤害因子设为0（免疫火焰伤害）
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.elementType == ElementTypes.fire && ef.factor > 0f)
                    {
                        factors[i] = new ElementFactor(ElementTypes.fire, 0f);
                    }
                }
                
                DevLog("[DragonDescendant] Boss火焰伤害吸收: " + actualFireDamage.ToString("F1") + " -> 治疗: " + fireHealAmount.ToString("F1"));
                
                // 延迟治疗（在伤害计算完成后）
                if (fireHealAmount > 0f)
                {
                    StartCoroutine(DelayedBossHeal(health, fireHealAmount));
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] OnDragonDescendantHurt 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟治疗Boss（在伤害计算完成后）
        /// </summary>
        private System.Collections.IEnumerator DelayedBossHeal(Health health, float amount)
        {
            yield return null; // 等待一帧
            
            if (health != null && !health.IsDead)
            {
                health.AddHealth(amount);
                DevLog("[DragonDescendant] Boss火焰能量治疗: +" + amount.ToString("F1"));
                
                // 显示治疗数字
                try
                {
                    if (dragonDescendantInstance != null)
                    {
                        FX.PopText.Pop("+" + amount.ToString("F0"), 
                            dragonDescendantInstance.transform.position + Vector3.up * 2.5f, 
                            new Color(0.2f, 1f, 0.2f), 1.2f, null);
                    }
                }
                catch { }
            }
        }
    }
}
