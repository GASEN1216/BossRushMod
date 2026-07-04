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
        /// <param name="position">生成位置</param>
        /// <param name="isChildProtectionSummon">是否为孩儿护我阶段召唤（true时不加入波次追踪系统）</param>
        public async UniTask<CharacterMainControl> SpawnDragonDescendant(
            Vector3 position,
            bool isChildProtectionSummon = false,
            bool notifyBossRushOnFailure = true,
            bool deferActivationUntilNextFrame = false)
        {
            try
            {
                DevLog("[DragonDescendant] 开始生成龙裔遗族Boss at " + position + (isChildProtectionSummon ? " (孩儿护我召唤)" : ""));

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
                    if (!isChildProtectionSummon && notifyBossRushOnFailure)
                    {
                        NotifyDragonDescendantSpawnFailed();
                    }
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
                    if (!isChildProtectionSummon && notifyBossRushOnFailure)
                    {
                        NotifyDragonDescendantSpawnFailed();
                    }
                    return null;
                }

                dragonDescendantInstance = character;
                character.gameObject.name = "BossRush_DragonDescendant";

                // 孩儿护我召唤的龙裔不加入波次追踪系统，避免死亡时误触发下一波
                if (!isChildProtectionSummon)
                {
                    // 关键：将龙裔遗族设置为当前Boss，以便BossRush系统能够追踪死亡事件
                    currentBoss = character;

                    // 多Boss模式下，将龙裔遗族加入当前波列表
                    if (bossesPerWave > 1 && currentWaveBosses != null && !currentWaveBosses.Contains(character))
                    {
                        currentWaveBosses.Add(character);
                    }
                }
                else
                {
                    DevLog("[DragonDescendant] 孩儿护我召唤：跳过波次追踪系统注册");
                }

                // 为该角色创建独立的预设副本，避免修改原版预设
                if (character.characterPreset != null)
                {
                    // 创建预设副本
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    customPreset.name = DragonDescendantConfig.BOSS_NAME_KEY;
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

                // 应用全局 Boss 数值倍率（所有模式生效）
                ApplyBossStatMultiplier(character);

                // 关键：在装备龙息武器之前，从角色已装备的原始武器获取完整属性（二阶段使用）
                OriginalWeaponData originalWeaponData = GetWeaponDataFromEquippedWeapon(character);

                // 装备武器和护甲
                await EquipDragonDescendant(character);

                // 添加能力控制器（传入原始武器完整属性）
                dragonDescendantAbilities = character.gameObject.AddComponent<DragonDescendantAbilityController>();
                dragonDescendantAbilities.Initialize(character, originalWeaponData);

                if (deferActivationUntilNextFrame)
                {
                    await UniTask.Yield();
                }

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

                // 自定义龙 Boss 也补上和普通 Boss 一样的位置兜底，避免被位移后卡进地下或掉出有效区域。
                StartCoroutine(DelayedBossPositionValidation(character, 0.5f));
                RegisterEnemyRecoveryAnchor(character, position);

                // 孩儿护我召唤的龙裔：跳过死亡事件订阅和波次追踪，但保留掉落系统
                // 这些龙裔的死亡由龙王的OnDescendantDeath处理，不走BossRush标准流程
                if (!isChildProtectionSummon)
                {
                    // 订阅死亡事件（使用实例事件）- 仅普通龙裔Boss
                    if (character.Health != null)
                    {
                        character.Health.OnDeadEvent.AddListener(OnDragonDescendantDeath);
                    }

                    // 注册龙套装效果（火焰伤害免疫）- 仅普通龙裔Boss
                    RegisterDragonDescendantSetBonus();

                    DevLog("[DragonDescendant] 龙裔遗族Boss生成完成");
                    ShowMessage(L10n.T("龙裔遗族 出现了！", "Dragon Descendant has appeared!"));
                }
                else
                {
                    DevLog("[DragonDescendant] 孩儿护我召唤的龙裔生成完成（跳过波次追踪，保留掉落）");
                }

                // 记录 Boss 生成时间和原始掉落数量（用于掉落随机化）- 所有龙裔都有掉落
                try
                {
                    int originalLootCount = 3; // 默认基础掉落数量
                    RegisterBossRandomLootTracking(character, originalLootCount);

                    DevLog("[DragonDescendant] 记录 Boss 生成信息并订阅掉落事件 - 时间: " + Time.time + ", 原始掉落数量: " + originalLootCount + (isChildProtectionSummon ? " (孩儿护我召唤)" : ""));
                }
                catch (Exception recordEx)
                {
                    DevLog("[DragonDescendant] [WARNING] 记录 Boss 生成信息失败: " + recordEx.Message);
                }

                // 返回生成的角色引用，供BossRush系统验证
                return character;
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 生成Boss失败: " + e.Message + "\n" + e.StackTrace);
                if (!isChildProtectionSummon && notifyBossRushOnFailure)
                {
                    NotifyDragonDescendantSpawnFailed();
                }
                return null;
            }
        }

        private void NotifyDragonDescendantSpawnFailed()
        {
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
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] NotifyDragonDescendantSpawnFailed异常: " + e.Message);
            }
        }

        // ========== 性能优化：预设缓存 ==========

        // 避免每次生成Boss时都调用Resources.FindObjectsOfTypeAll

        /// <summary>
        /// 缓存的Boss_Red敌人预设（龙裔/龙皇基础预制体）
        /// </summary>
        private static CharacterRandomPreset cachedQuestionMarkPreset = null;

        /// <summary>
        /// 是否已搜索过Boss_Red预设
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
                var presets = ObjectCache.GetCharacterPresets();

                // 优先查找 Cname_Boss_Red 预设（龙裔/龙皇使用的Boss预制体）
                foreach (var preset in presets)
                {
                    if (preset == null) continue;

                    // 通过nameKey精确匹配 Cname_Boss_Red
                    if (preset.nameKey == DragonDescendantConfig.BasePresetNameKey ||
                        preset.nameKey == "Cname_Boss_Red")
                    {
                        cachedQuestionMarkPreset = preset;
                        DevLog("[DragonDescendant] 找到 Cname_Boss_Red 预设: " + preset.name + " (nameKey=" + preset.nameKey + ")");
                        return preset;
                    }
                }

                // 后备：通过预设名称模糊匹配 Boss_Red
                foreach (var preset in presets)
                {
                    if (preset == null) continue;

                    if (preset.name.Contains("Boss_Red") ||
                        preset.name.Contains("BossRed"))
                    {
                        cachedQuestionMarkPreset = preset;
                        DevLog("[DragonDescendant] 通过名称匹配找到 Boss_Red 预设: " + preset.name);
                        return preset;
                    }
                }

                // 最终后备：查找???预设（兼容旧版本）
                foreach (var preset in presets)
                {
                    if (preset == null) continue;

                    if (preset.nameKey == "???" ||
                        preset.Name == "???" ||
                        preset.DisplayName == "???")
                    {
                        cachedQuestionMarkPreset = preset;
                        DevLog("[DragonDescendant] [WARNING] 未找到 Cname_Boss_Red，回退到???预设: " + preset.name);
                        return preset;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 查找基础预设失败: " + e.Message);
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
            cachedDragonDescendantOriginalWeaponData = null;

            // 清理物品缓存
            cachedItemsByName.Clear();
            cachedBulletsByCaliber.Clear();

            // 清理武器配置缓存
            DragonBreathWeaponConfig.ClearStaticCache();

            // 清理Buff处理器缓存
            DragonBreathBuffHandler.ClearStaticCache();

            // 清理能力控制器缓存（燃烧弹、子弹预制体）
            DragonDescendantAbilityController.ClearStaticCache();
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
                var presets = ObjectCache.GetCharacterPresets();

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

        private static OriginalWeaponData cachedDragonDescendantOriginalWeaponData;

        internal static Projectile GetCachedDragonDescendantPhase2BulletPrefab()
        {
            return cachedDragonDescendantOriginalWeaponData != null ? cachedDragonDescendantOriginalWeaponData.bulletPrefab : null;
        }

        private static void CacheDragonDescendantOriginalWeaponData(OriginalWeaponData data)
        {
            if (data == null || data.bulletPrefab == null)
            {
                return;
            }

            cachedDragonDescendantOriginalWeaponData = data;
            DevLog("[DragonDescendant] 已缓存二阶段原始弹幕基底: " + data.bulletPrefab.name);
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
                            CacheDragonDescendantOriginalWeaponData(data);
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
                            CacheDragonDescendantOriginalWeaponData(data);
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
                        CacheDragonDescendantOriginalWeaponData(data);
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

        // ========== 性能优化：运行时查找缓存 ==========

        /// <summary>
        /// 缓存的子弹（按口径）
        /// </summary>
        private static Dictionary<string, Item> cachedBulletsByCaliber = new Dictionary<string, Item>();

        /// <summary>
        /// 缓存的物品（按名称）
        /// </summary>
        private static Dictionary<string, Item> cachedItemsByName = new Dictionary<string, Item>();

    }
}
