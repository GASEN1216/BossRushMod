// ============================================================================
// PhantomWitchScytheWeaponConfig.cs - 幽灵女巫大镰装备工厂配置器
// ============================================================================
// 模块说明：
//   在 ItemFactory 加载镰刀 Prefab（TypeID 500044）后，自动完成：
//   - ItemAgent_MeleeWeapon 组件（手持插槽、动画类型、SlashFx/HitFx 回退）
//   - ItemSetting_MeleeWeapon 组件（Ghost 元素 + 诅咒 Buff 20% 触发）
//   - 全部 11 个近战 Stats（介于断界戟与霜之哀伤之间）
//   - 物品标签（Weapon / MeleeWeapon / DontDropOnDeadInSlot / Special / PhantomWitch）
//   - 本地化多键注入（Item_500044 / phantom_witch_scythe 等）
//   - 运动模糊禁用、Shader / Layer 修复、Item 自身渲染器禁用
//
//   与 FenHuangHalberdWeaponConfig / FrostmourneWeaponConfig 保持一致的配置范式。
//   Unity Prefab 只需挂 Item 组件 (TypeID=500044) + 3D 模型即可，其余由本类运行时补齐。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫大镰装备工厂配置器
    /// </summary>
    public static class PhantomWitchScytheWeaponConfig
    {
        // ========== 基础名匹配 ==========
        private const string SCYTHE_BASE_NAME = "PhantomScythe";
        private static readonly Vector3 DefaultSlashFxScale = new Vector3(1.8f, 1.8f, 1.8f);
        private static GameObject cachedFallbackSlashFx;
        private static GameObject cachedFallbackHitFx;
        private static PhantomWitchScytheConfig cachedConfig;

        // ========== 近战 Stats 数值（介于断界戟 55 / 霜之哀伤 38.5 之间）==========
        // AttackSpeed 在游戏内解释为"每秒攻击次数"，攻击间隔 cd = 1 / AttackSpeed。
        // 1.75 对应 ~0.57s/次，介于断界戟 (2.2, 0.45s) 与霜之哀伤 (1.54, 0.65s) 之间，符合设计定位。
        // 之前"A一下要等好久"的根因是 StatCollection._cachedStatsDictionary 缓存失效（见 InvalidateStatsDictionary），
        // 不是数值问题 —— 缓存没刷新时 GetStatValue 返回 0，Mathf.Max(0.1, 0) 让 cd 直接变成 10s。
        private static readonly Dictionary<string, float> WeaponStats = new Dictionary<string, float>
        {
            { "Damage", 42f },
            { "MoveSpeedMultiplier", 1.06f },
            { "BlockBullet", 0.4f },
            { "CritRate", 0.08f },
            { "CritDamageFactor", 1.45f },
            { "ArmorPiercing", 4.5f },
            { "AttackSpeed", 1.75f },
            { "AttackRange", PhantomWitchScytheConfig.BaseAttackRange },
            { "DealDamageTime", 0.1f },
            { "StaminaCost", 8f },
            { "BleedChance", 0.1f }
        };

        private static readonly HashSet<string> DisplayStats = new HashSet<string>
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
        /// 单参入口（由 CustomItemRuntimeStateHelper / ItemFactory 配置器调用）
        /// </summary>
        public static bool TryConfigure(Item item)
        {
            if (item == null || item.TypeID != PhantomWitchScytheIds.WeaponTypeId)
            {
                return false;
            }

            return TryConfigureInternal(item);
        }

        /// <summary>
        /// 双参入口（由 EquipmentFactory 的基础名分派调用）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName))
            {
                return false;
            }

            if (!baseName.Equals(SCYTHE_BASE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryConfigureInternal(item);
        }

        private static bool TryConfigureInternal(Item item)
        {
            try
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 开始配置幽灵女巫大镰...");

                // 1. 获取已加载的 3D 模型 Agent（如 AssetBundle 已到位）
                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(PhantomWitchScytheIds.ModelBaseName, out modelAgent);

                // 2. 创建 StatCollection 并添加近战 Stats
                ConfigureStats(item);

                // 3. 添加 ItemAgent_MeleeWeapon 组件，并为模型 Agent 补齐配置
                ConfigureMeleeAgent(item, modelAgent);

                // 4. 添加 ItemSetting_MeleeWeapon 组件（Ghost 元素 + 诅咒 Buff）
                ConfigureMeleeSetting(item);

                // 5. 追加物品标签（含 PhantomWitch）
                ConfigureTags(item);

                // 5.5 添加宝石槽位（2 个 Gem 槽）
                EquipmentHelper.ConfigureGemSlots(item, 2);

                // 6. 绑定已加载的模型到物品 prefab（如有）
                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(
                        item,
                        PhantomWitchScytheIds.ModelBaseName,
                        SCYTHE_BASE_NAME);
                }

                // 7. 注入本地化（多键）
                InjectLocalization(item);
                SyncItemValueFromRawValue(item);

                // 8. 修复运动模糊（武器在挥动时不产生模糊拖影）
                DisableMotionBlur(item.gameObject);

                if (modelAgent != null)
                {
                    DisableMotionBlur(modelAgent.gameObject);
                    FixModelGraphics(modelAgent.gameObject);
                }

                // 9. 禁用 Item prefab 自身的渲染器（避免玩家身上多一个模型）
                if (modelAgent != null)
                {
                    DisableItemRenderers(item.gameObject);
                }

                ModBehaviour.DevLog("[PhantomWitchScythe] 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 配置失败: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }

        private static void SyncItemValueFromRawValue(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                int rawValue = item.GetTotalRawValue();
                if (rawValue > 0 && item.Value < rawValue)
                {
                    item.Value = rawValue;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 同步物品价值失败: " + e.Message);
            }
        }

        // ========== Stats 配置 ==========

        private static void ConfigureStats(Item item)
        {
            StatCollection stats = item.Stats;
            if (stats == null)
            {
                item.CreateStatsComponent();
                stats = item.Stats;
            }

            if (stats == null)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] [WARNING] 无法获取或创建 StatCollection");
                return;
            }

            foreach (KeyValuePair<string, float> kvp in WeaponStats)
            {
                bool shouldDisplay = DisplayStats.Contains(kvp.Key);
                Stat existingStat = stats.GetStat(kvp.Key);
                if (existingStat != null)
                {
                    existingStat.BaseValue = kvp.Value;
                }
                else
                {
                    stats.Add(new Stat(kvp.Key, kvp.Value, shouldDisplay));
                }
            }

            // 关键修复：StatCollection 的内部字典在第一次 GetStat 后缓存，
            // 后续 Add 的 Stat 不会进入字典，导致战斗时 GetStatValue(hash) 查不到新 Stat、
            // 直接返回 0（伤害/攻速/范围全部失效）。手动失效字典，强制下次访问重建。
            InvalidateStatsDictionary(stats);

            ModBehaviour.DevLog("[PhantomWitchScythe] 已添加 11 个近战 Stats");
        }

        private static void InvalidateStatsDictionary(StatCollection stats)
        {
            if (stats == null)
            {
                return;
            }

            try
            {
                FieldInfo dictField = typeof(StatCollection).GetField(
                    "_cachedStatsDictionary",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (dictField != null)
                {
                    dictField.SetValue(stats, null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] [WARNING] 重置 Stat 字典缓存失败: " + e.Message);
            }
        }

        // ========== 组件配置 ==========

        private static void ConfigureMeleeAgent(Item item, ItemAgent modelAgent)
        {
            ItemAgent_MeleeWeapon meleeAgent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (meleeAgent == null)
            {
                meleeAgent = item.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
            }

            meleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;
            meleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;

            // 设置音效键
            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField(
                    "soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null)
                {
                    soundKeyField.SetValue(meleeAgent, "Default");
                }
            }
            catch
            {
            }

            // 设置斩击特效延迟
            try
            {
                FieldInfo slashDelayField = typeof(ItemAgent_MeleeWeapon).GetField(
                    "slashFxDelayTime",
                    BindingFlags.Public | BindingFlags.Instance);
                if (slashDelayField != null)
                {
                    slashDelayField.SetValue(meleeAgent, 0.06f);
                }
            }
            catch
            {
            }

            EnsureMeleeAttackFx(meleeAgent);

            // 确保 socketsList 已初始化
            try
            {
                FieldInfo socketsField = typeof(DuckovItemAgent).GetField(
                    "socketsList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketsField != null)
                {
                    object socketsList = socketsField.GetValue(meleeAgent);
                    if (socketsList == null)
                    {
                        socketsField.SetValue(meleeAgent, new List<Transform>());
                    }
                }
            }
            catch
            {
            }

            // 为 3D 模型 Agent 也注入相同的 Socket 设置
            if (modelAgent != null)
            {
                ItemAgent_MeleeWeapon modelMeleeAgent = modelAgent.gameObject.GetComponent<ItemAgent_MeleeWeapon>();
                if (modelMeleeAgent == null)
                {
                    modelMeleeAgent = modelAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                }
                modelMeleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;
                modelMeleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;
                EnsureMeleeAttackFx(modelMeleeAgent);
            }

            ModBehaviour.DevLog("[PhantomWitchScythe] 已配置 ItemAgent_MeleeWeapon (socket=normalHandheld, anim=meleeWeapon)");
        }

        internal static void EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)
        {
            if (meleeAgent == null)
            {
                return;
            }

            if (meleeAgent.slashFx == null)
            {
                meleeAgent.slashFx = GetFallbackSlashFx();
            }

            if (meleeAgent.hitFx == null)
            {
                meleeAgent.hitFx = GetFallbackHitFx();
            }

            if (meleeAgent.slashFx != null && meleeAgent.slashFx.transform != null)
            {
                Vector3 scale = meleeAgent.slashFx.transform.localScale;
                if (scale.sqrMagnitude <= 0.0001f)
                {
                    meleeAgent.slashFx.transform.localScale = DefaultSlashFxScale;
                }
            }
        }

        private static GameObject GetFallbackSlashFx()
        {
            if (cachedFallbackSlashFx == null)
            {
                cachedFallbackSlashFx = FindFallbackMeleeFx(true);
            }

            return cachedFallbackSlashFx;
        }

        private static GameObject GetFallbackHitFx()
        {
            if (cachedFallbackHitFx == null)
            {
                cachedFallbackHitFx = FindFallbackMeleeFx(false);
            }

            return cachedFallbackHitFx;
        }

        private static GameObject FindFallbackMeleeFx(bool slashFx)
        {
            try
            {
                ItemAgent_MeleeWeapon[] meleeAgents = Resources.FindObjectsOfTypeAll<ItemAgent_MeleeWeapon>();
                for (int i = 0; i < meleeAgents.Length; i++)
                {
                    ItemAgent_MeleeWeapon candidate = meleeAgents[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    Item candidateItem = candidate.Item;
                    if (candidateItem != null && candidateItem.TypeID == PhantomWitchScytheIds.WeaponTypeId)
                    {
                        continue;
                    }

                    GameObject fx = slashFx ? candidate.slashFx : candidate.hitFx;
                    if (fx == null)
                    {
                        continue;
                    }

                    if (cachedFallbackSlashFx == null && candidate.slashFx != null)
                    {
                        cachedFallbackSlashFx = candidate.slashFx;
                    }

                    if (cachedFallbackHitFx == null && candidate.hitFx != null)
                    {
                        cachedFallbackHitFx = candidate.hitFx;
                    }

                    return fx;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 搜索近战特效引用失败: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 配置 Ghost 元素 + 诅咒 Buff（50% 触发）
        /// </summary>
        private static void ConfigureMeleeSetting(Item item)
        {
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (meleeSetting == null)
            {
                meleeSetting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            }

            meleeSetting.element = ElementTypes.ghost;
            meleeSetting.dealExplosionDamage = false;
            meleeSetting.buff = PhantomWitchAssetManager.GetCurseBuff();
            meleeSetting.buffChance = meleeSetting.buff != null ? 0.5f : 0f;

            ModBehaviour.DevLog("[PhantomWitchScythe] 已配置 ItemSetting_MeleeWeapon (element=Ghost, curse="
                + (meleeSetting.buff != null ? "50%" : "null") + ")");
        }

        // ========== 标签配置 ==========

        private static void ConfigureTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "MeleeWeapon");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
            EquipmentHelper.AddTagToItem(item, "PhantomWitch");

            try
            {
                item.SetBool("IsMeleeWeapon", true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] SetBool IsMeleeWeapon 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[PhantomWitchScythe] 已添加标签: Weapon/MeleeWeapon/DontDropOnDeadInSlot/Special/PhantomWitch");
        }

        // ========== 本地化 ==========

        private static void InjectLocalization(Item item)
        {
            try
            {
                if (cachedConfig == null)
                {
                    cachedConfig = new PhantomWitchScytheConfig();
                }

                string displayName = L10n.T(cachedConfig.DisplayNameCN, cachedConfig.DisplayNameEN);
                string description = L10n.T(cachedConfig.DescriptionCN, cachedConfig.DescriptionEN);

                // 多键注入，确保游戏各处都能正确显示
                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                LocalizationHelper.InjectLocalization("phantom_witch_scythe", displayName);
                LocalizationHelper.InjectLocalization("phantom_witch_scythe_Desc", description);
                LocalizationHelper.InjectLocalization(PhantomWitchConfig.ScytheNameCN, displayName);
                LocalizationHelper.InjectLocalization(PhantomWitchConfig.ScytheNameEN, displayName);
                // Unity Prefab 的 displayName 字段直接是 "PhantomScythe_Melee_Item"，
                // 游戏 UI（物品名/描述/死亡日志）会用它当 key 去查本地化。
                // 不注入就会在界面上显示为 *PhantomScythe_Melee_Item* 和 *PhantomScythe_Melee_Item_Desc*。
                LocalizationHelper.InjectLocalization(PhantomWitchScytheIds.WeaponPrefabName, displayName);
                LocalizationHelper.InjectLocalization(PhantomWitchScytheIds.WeaponPrefabName + "_Desc", description);

                ModBehaviour.DevLog("[PhantomWitchScythe] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 本地化注入失败: " + e.Message);
            }
        }

        // ========== 渲染修复 ==========

        /// <summary>
        /// 禁用 Item prefab 自身的所有渲染器，防止玩家身上多渲染一个模型
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
                ModBehaviour.DevLog("[PhantomWitchScythe] 已禁用 Item prefab 上的 " + renderers.Length + " 个渲染器");
            }
            catch
            {
            }
        }

        /// <summary>
        /// 禁用武器模型的运动模糊
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
            catch
            {
            }
        }

        /// <summary>
        /// 强制设置近战武器模型的 Layer 和 Shader
        /// </summary>
        private static void FixModelGraphics(GameObject go)
        {
            try
            {
                // 设置 Layer 为 0 (Default)
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
                ModBehaviour.DevLog("[PhantomWitchScythe] FixModelGraphics 失败: " + e.Message);
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null)
            {
                return;
            }

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
