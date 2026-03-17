// ============================================================================
// FenHuangHalberdWeaponConfig.cs - 焚皇断界戟装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为焚皇断界戟 Prefab 配置：
//   - ItemAgent_MeleeWeapon 组件（手持插槽、动画类型）
//   - ItemSetting_MeleeWeapon 组件（火属性）
//   - 全部 11 个近战 Stats
//   - 物品标签（Weapon/MeleeWeapon/DontDropOnDeadInSlot/Special）
//   - 本地化注入
//
//   Unity Prefab 只需配置 Item 组件（TypeID=500034）+ 3D 模型即可
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟装备工厂配置器
    /// </summary>
    public static class FenHuangHalberdWeaponConfig
    {
        // ========== 基础名匹配 ==========
        private const string HALBERD_BASE_NAME = "FenHuangHalberd";

        // ========== 近战 Stats 数值 ==========
        private const float STAT_DAMAGE = 55f;
        private const float STAT_BLOCK_BULLET = 1f;
        private const float STAT_CRIT_RATE = 0.10f;
        private const float STAT_CRIT_DAMAGE_FACTOR = 2.0f;
        private const float STAT_ARMOR_PIERCING = 6f;
        private const float STAT_ATTACK_SPEED = 2.2f;
        private const float STAT_ATTACK_RANGE = FenHuangHalberdConfig.BaseAttackRange;
        private const float STAT_DEAL_DAMAGE_TIME = 0.12f;
        private const float STAT_STAMINA_COST = 10f;
        private const float STAT_BLEED_CHANCE = 0.15f;
        private const float STAT_MOVE_SPEED_MULTIPLIER = 1.1f;

        private static readonly Dictionary<string, float> WEAPON_STATS = new Dictionary<string, float>
        {
            { "Damage", STAT_DAMAGE },
            { "MoveSpeedMultiplier", STAT_MOVE_SPEED_MULTIPLIER },
            { "BlockBullet", STAT_BLOCK_BULLET },
            { "CritRate", STAT_CRIT_RATE },
            { "CritDamageFactor", STAT_CRIT_DAMAGE_FACTOR },
            { "ArmorPiercing", STAT_ARMOR_PIERCING },
            { "AttackSpeed", STAT_ATTACK_SPEED },
            { "AttackRange", STAT_ATTACK_RANGE },
            { "DealDamageTime", STAT_DEAL_DAMAGE_TIME },
            { "StaminaCost", STAT_STAMINA_COST },
            { "BleedChance", STAT_BLEED_CHANCE }
        };

        private static readonly HashSet<string> DISPLAY_STATS = new HashSet<string>
        {
            "Damage",
            "MoveSpeedMultiplier",
            "CritRate",
            "CritDamageFactor",
            "ArmorPiercing",
            "AttackSpeed",
            "AttackRange",
            "StaminaCost",
            "BleedChance"
        };

        /// <summary>
        /// 尝试配置焚皇断界戟（由 EquipmentFactory 在加载 AssetBundle 后调用）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            if (!baseName.Equals(HALBERD_BASE_NAME, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 开始配置焚皇断界戟...");

                // 获取实际的 3D 模型 Agent 并修复它的渲染属性
                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(FenHuangHalberdIds.ModelBaseName, out modelAgent);

                // 1. 创建 StatCollection 并添加近战 Stats
                ConfigureStats(item);

                // 2. 添加 ItemAgent_MeleeWeapon 组件，并注入到模型 Agent
                ConfigureMeleeAgent(item, modelAgent);

                // 3. 添加 ItemSetting_MeleeWeapon 组件
                ConfigureMeleeSetting(item);

                // 4. 添加标签
                ConfigureTags(item);

                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(item, FenHuangHalberdIds.ModelBaseName, HALBERD_BASE_NAME);
                }

                // 5. 注入本地化
                InjectLocalization(item);

                // 6. 修复运动模糊（禁用 MotionVector 使武器不会在移动时模糊）
                DisableMotionBlur(item.gameObject);

                if (modelAgent != null)
                {
                    DisableMotionBlur(modelAgent.gameObject);
                    FixModelGraphics(modelAgent.gameObject);
                }

                // 7. 禁用 Item prefab 自身的渲染器
                // 焚皇断界戟的 prefab 将 3D 模型（Mesh）直接放在 Item 的子节点上，
                // 没有单独的 _Model prefab，也没有 ItemGraphic 来管理显示/隐藏。
                // 当游戏实例化物品放入装备槽时，这些渲染器会一直可见，
                // 导致玩家身上多出一个戟的模型，且换武器后仍然残留。
                // 实际的手持显示由 Handheld Agent 系统负责，这里只需禁用 Item 上的渲染器。
                if (modelAgent != null)
                {
                    DisableItemRenderers(item.gameObject);
                }

                ModBehaviour.DevLog("[FenHuangHalberd] 焚皇断界戟配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 配置失败: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }

        // ========== Stats 配置 ==========

        /// <summary>
        /// 创建并配置近战武器所需的全部 Stats
        /// </summary>
        private static void ConfigureStats(Item item)
        {
            // 获取或创建 StatCollection
            StatCollection stats = item.Stats;
            if (stats == null)
            {
                // 使用 Item.CreateStatsComponent() 创建
                item.CreateStatsComponent();
                stats = item.Stats;
            }

            if (stats == null)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] [WARNING] 无法获取或创建 StatCollection");
                return;
            }

            // 添加全部 11 个近战 Stats
            // Stat 构造函数: Stat(string key, float value, bool display = false)
            int addedCount = 0;
            int updatedCount = 0;

            foreach (KeyValuePair<string, float> kvp in WEAPON_STATS)
            {
                bool shouldDisplay = DISPLAY_STATS.Contains(kvp.Key);
                Stat existingStat = stats.GetStat(kvp.Key);
                if (existingStat != null)
                {
                    existingStat.BaseValue = kvp.Value;
                    updatedCount++;
                }
                else
                {
                    stats.Add(new Stat(kvp.Key, kvp.Value, shouldDisplay));
                    addedCount++;
                }
            }

            ModBehaviour.DevLog("[FenHuangHalberd] 已添加 11 个近战 Stats");
        }

        // ========== 组件配置 ==========

        /// <summary>
        /// 添加并配置 ItemAgent_MeleeWeapon 组件
        /// </summary>
        private static void ConfigureMeleeAgent(Item item, ItemAgent modelAgent)
        {
            ItemAgent_MeleeWeapon meleeAgent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (meleeAgent == null)
            {
                meleeAgent = item.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
            }

            // 原版近战武器用 normalHandheld 挂在 RightHandSocket（受攻击动画骨骼驱动）
            meleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;

            // 设置动画类型 = meleeWeapon（枚举值 3，播放近战挥砍动画）
            meleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;

            // 设置音效键
            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null)
                {
                    soundKeyField.SetValue(meleeAgent, "Default");
                }
            }
            catch { }

            // 设置斩击特效延迟
            try
            {
                FieldInfo slashDelayField = typeof(ItemAgent_MeleeWeapon).GetField("slashFxDelayTime",
                    BindingFlags.Public | BindingFlags.Instance);
                if (slashDelayField != null)
                {
                    slashDelayField.SetValue(meleeAgent, 0.06f);
                }
            }
            catch { }

            // hitFx 和 slashFx 留空，使用原版默认

            // 确保 socketsList 被初始化（DuckovItemAgent 基类需要）
            try
            {
                FieldInfo socketsField = typeof(DuckovItemAgent).GetField("socketsList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketsField != null)
                {
                    var socketsList = socketsField.GetValue(meleeAgent);
                    if (socketsList == null)
                    {
                        socketsField.SetValue(meleeAgent, new System.Collections.Generic.List<Transform>());
                    }
                }
            }
            catch { }

            // 为 3D 模型 Agent 也注入相同的 Socket 设置，确保真正生成的 Handheld Agent 绑定到正确的插槽
            if (modelAgent != null)
            {
                ItemAgent_MeleeWeapon modelMeleeAgent = modelAgent.gameObject.GetComponent<ItemAgent_MeleeWeapon>();
                if (modelMeleeAgent == null)
                {
                    modelMeleeAgent = modelAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                }
                modelMeleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;
                modelMeleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;
            }

            ModBehaviour.DevLog("[FenHuangHalberd] 已配置 ItemAgent_MeleeWeapon (socket=normalHandheld, anim=meleeWeapon)");
        }

        /// <summary>
        /// 添加并配置 ItemSetting_MeleeWeapon 组件
        /// </summary>
        private static void ConfigureMeleeSetting(Item item)
        {
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (meleeSetting == null)
            {
                meleeSetting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            }

            // 设置火属性
            meleeSetting.element = ElementTypes.fire;

            // 不使用爆炸伤害
            meleeSetting.dealExplosionDamage = false;

            // 不通过 ItemSetting 触发 Buff（连招代码单独处理）
            meleeSetting.buffChance = 0f;

            ModBehaviour.DevLog("[FenHuangHalberd] 已配置 ItemSetting_MeleeWeapon (element=Fire)");
        }

        // ========== 标签配置 ==========

        /// <summary>
        /// 添加物品标签
        /// </summary>
        private static void ConfigureTags(Item item)
        {
            // 近战武器标签
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "MeleeWeapon");

            // 绑定装备（死亡不掉落）
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");

            // 特殊物品
            EquipmentHelper.AddTagToItem(item, "Special");
            EquipmentHelper.AddTagToItem(item, "DragonKing");

            // 设置 IsMeleeWeapon 布尔标记（ItemSetting_MeleeWeapon.SetMarkerParam 的逻辑）
            try
            {
                item.SetBool("IsMeleeWeapon", true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] SetBool IsMeleeWeapon 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[FenHuangHalberd] 已添加标签: Weapon/MeleeWeapon/DontDropOnDeadInSlot/Special/DragonKing");
        }

        // ========== 本地化 ==========

        /// <summary>
        /// 注入焚皇断界戟本地化文本
        /// </summary>
        // 缓存配置实例，避免每次本地化注入都创建新对象
        private static FenHuangHalberdConfig _cachedConfig;

        private static void InjectLocalization(Item item)
        {
            try
            {
                if (_cachedConfig == null) _cachedConfig = new FenHuangHalberdConfig();
                string displayName = L10n.T(_cachedConfig.DisplayNameCN, _cachedConfig.DisplayNameEN);
                string description = L10n.T(_cachedConfig.DescriptionCN, _cachedConfig.DescriptionEN);

                // 使用多种键注入，确保游戏各处都能正确显示
                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                LocalizationHelper.InjectLocalization("fenhuang_halberd", displayName);
                LocalizationHelper.InjectLocalization("fenhuang_halberd_Desc", description);

                ModBehaviour.DevLog("[FenHuangHalberd] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 本地化注入失败: " + e.Message);
            }
        }
        /// <summary>
        /// 禁用 Item prefab 自身的所有渲染器，防止物品实例在玩家身上多渲染一个模型
        /// </summary>
        private static void DisableItemRenderers(GameObject go)
        {
            try
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r != null)
                    {
                        r.enabled = false;
                    }
                }
                ModBehaviour.DevLog("[FenHuangHalberd] 已禁用 Item prefab 上的 " + renderers.Length + " 个渲染器");
            }
            catch { }
        }

        /// <summary>
        /// 禁用武器模型的运动模糊，使其与原版武器表现一致
        /// </summary>
        private static void DisableMotionBlur(GameObject go)
        {
            try
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r != null)
                    {
                        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 强制设置近战武器模型的 Layer 和 Shader，使其与原版武器表现一致
        /// </summary>
        private static void FixModelGraphics(GameObject go)
        {
            try
            {
                // 设置 Layer 为 0 (Default)，因为它是跟随身体的近战武器，使用人物主摄像机渲染，而不是第一人称 FPS 摄像机(Layer 9)
                SetLayerRecursively(go, 0);
                
                // 强制使用游戏原版 Shader
                Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                if (gameShader != null)
                {
                    Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        if (r.sharedMaterials != null)
                        {
                            foreach (Material mat in r.sharedMaterials)
                            {
                                if (mat != null && mat.shader != null)
                                {
                                    string oldShaderName = mat.shader.name;
                                    // 替换常见的标准或者 lit shader
                                    if (oldShaderName.Contains("Standard") || 
                                        oldShaderName.Contains("Lit") ||
                                        oldShaderName.Contains("Universal"))
                                    {
                                        mat.shader = gameShader;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] FixModelGraphics 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 递归设置 GameObject 及其子物体的 Layer
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
