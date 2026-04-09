// ============================================================================
// FrostmourneWeaponConfig.cs - 霜之哀伤装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为霜之哀伤 Prefab 配置：
//   - ItemAgent_MeleeWeapon 组件（手持插槽、动画类型）
//   - ItemSetting_MeleeWeapon 组件（冰属性）
//   - 全部 11 个近战 Stats（断界戟的 70%）
//   - ColdProtection +2 modifier
//   - 物品标签
//   - 本地化注入
//   - 冰色挥砍特效
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤装备工厂配置器
    /// </summary>
    public static class FrostmourneWeaponConfig
    {
        // ========== 基础名匹配 ==========
        private const string FROSTMOURNE_BASE_NAME = "Frostmourne";
        private static readonly Vector3 DefaultSlashFxScale = new Vector3(1.34f, 1.34f, 0.96f); // 比断界戟更薄
        private static GameObject cachedIceSlashFx;
        private static GameObject cachedFallbackHitFx;

        // ========== 近战 Stats 数值（断界戟的 70%）==========
        private const float STAT_DAMAGE = 38.5f;
        private const float STAT_BLOCK_BULLET = 0.7f;
        private const float STAT_CRIT_RATE = 0.07f;
        private const float STAT_CRIT_DAMAGE_FACTOR = 1.4f;
        private const float STAT_ARMOR_PIERCING = 4.2f;
        private const float STAT_ATTACK_SPEED = 1.54f;
        private const float STAT_ATTACK_RANGE = FrostmourneConfig.BaseAttackRange;
        private const float STAT_DEAL_DAMAGE_TIME = 0.084f;
        private const float STAT_STAMINA_COST = 7f;
        private const float STAT_BLEED_CHANCE = 0.105f;
        private const float STAT_MOVE_SPEED_MULTIPLIER = 1.07f;

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
        /// 尝试配置霜之哀伤（由 EquipmentFactory 在加载 AssetBundle 后调用）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            if (!baseName.Equals(FROSTMOURNE_BASE_NAME, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                ModBehaviour.DevLog("[Frostmourne] 开始配置霜之哀伤...");

                // 获取实际的 3D 模型 Agent 并修复它的渲染属性
                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(FrostmourneIds.ModelBaseName, out modelAgent);

                // 1. 创建 StatCollection 并添加近战 Stats
                ConfigureStats(item);

                // 2. 添加 ItemAgent_MeleeWeapon 组件
                ConfigureMeleeAgent(item, modelAgent);

                // 3. 添加 ItemSetting_MeleeWeapon 组件（冰属性）
                ConfigureMeleeSetting(item);

                // 4. 添加标签
                ConfigureTags(item);

                // 5. 添加 ColdProtection +2 modifier
                ConfigureModifiers(item);

                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(item, FrostmourneIds.ModelBaseName, FROSTMOURNE_BASE_NAME);
                }

                // 6. 注入本地化
                InjectLocalization(item);
                SyncItemValueFromRawValue(item);

                // 7. 修复运动模糊
                DisableMotionBlur(item.gameObject);

                if (modelAgent != null)
                {
                    DisableMotionBlur(modelAgent.gameObject);
                    FixModelGraphics(modelAgent.gameObject);
                }

                // 8. 禁用 Item prefab 自身的渲染器
                if (modelAgent != null)
                {
                    DisableItemRenderers(item.gameObject);
                }

                ModBehaviour.DevLog("[Frostmourne] 霜之哀伤配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 配置失败: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }

        private static void SyncItemValueFromRawValue(Item item)
        {
            if (item == null) return;

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
                ModBehaviour.DevLog("[Frostmourne] 同步物品价值失败: " + e.Message);
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
                ModBehaviour.DevLog("[Frostmourne] [WARNING] 无法获取或创建 StatCollection");
                return;
            }

            foreach (KeyValuePair<string, float> kvp in WEAPON_STATS)
            {
                bool shouldDisplay = DISPLAY_STATS.Contains(kvp.Key);
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

            ModBehaviour.DevLog("[Frostmourne] 已添加 11 个近战 Stats");
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

            EnsureMeleeAttackFx(meleeAgent);

            // 确保 socketsList 被初始化
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

            ModBehaviour.DevLog("[Frostmourne] 已配置 ItemAgent_MeleeWeapon");
        }

        internal static void EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)
        {
            if (meleeAgent == null) return;

            if (meleeAgent.slashFx == null)
            {
                meleeAgent.slashFx = GetIceSlashFx();
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

        /// <summary>
        /// 获取冰色挥砍特效（复用原版特效但调整颜色和缩放）
        /// </summary>
        private static GameObject GetIceSlashFx()
        {
            if (cachedIceSlashFx == null)
            {
                // 先找到原版的 slashFx
                GameObject originalSlashFx = FindFallbackMeleeFx(true);
                if (originalSlashFx != null)
                {
                    // 克隆并修改为冰色
                    cachedIceSlashFx = UnityEngine.Object.Instantiate(originalSlashFx);
                    cachedIceSlashFx.name = "Frostmourne_IceSlashFx";
                    cachedIceSlashFx.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(cachedIceSlashFx);

                    // 调整缩放（更薄）
                    cachedIceSlashFx.transform.localScale = DefaultSlashFxScale;

                    // 修改粒子系统颜色为冰蓝色
                    TintParticleSystemsIce(cachedIceSlashFx);
                }
                else
                {
                    // 没有原版特效可用，创建简单的模板
                    cachedIceSlashFx = FrostmourneSlashFxCompat.CreateTemplate(DefaultSlashFxScale);
                }
            }

            return cachedIceSlashFx;
        }

        /// <summary>
        /// 将 GameObject 下所有粒子系统的颜色调整为冰蓝色
        /// </summary>
        private static void TintParticleSystemsIce(GameObject go)
        {
            if (go == null) return;

            Color iceColor = new Color(0.31f, 0.76f, 0.97f, 1f);       // #4FC3F7
            Color iceColorFade = new Color(0.51f, 0.83f, 0.98f, 0.6f); // #81D4FA

            ParticleSystem[] particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in particleSystems)
            {
                if (ps == null) continue;

                try
                {
                    var main = ps.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(iceColor, iceColorFade);
                }
                catch { }

                try
                {
                    var colorOverLifetime = ps.colorOverLifetime;
                    if (colorOverLifetime.enabled)
                    {
                        Gradient gradient = new Gradient();
                        gradient.SetKeys(
                            new GradientColorKey[]
                            {
                                new GradientColorKey(iceColor, 0f),
                                new GradientColorKey(iceColorFade, 1f)
                            },
                            new GradientAlphaKey[]
                            {
                                new GradientAlphaKey(1f, 0f),
                                new GradientAlphaKey(0f, 1f)
                            }
                        );
                        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                    }
                }
                catch { }
            }

            // 修改 Renderer 材质颜色
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null || r.sharedMaterial == null) continue;

                try
                {
                    // 克隆材质避免影响原版
                    Material mat = new Material(r.sharedMaterial);
                    if (mat.HasProperty("_Color"))
                    {
                        mat.color = iceColor;
                    }
                    if (mat.HasProperty("_TintColor"))
                    {
                        mat.SetColor("_TintColor", iceColor);
                    }
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.SetColor("_EmissionColor", iceColor * 0.5f);
                    }
                    r.material = mat;
                }
                catch { }
            }
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
                    if (candidate == null) continue;

                    Item candidateItem = candidate.Item;
                    // 跳过自己和断界戟
                    if (candidateItem != null &&
                        (candidateItem.TypeID == FrostmourneIds.WeaponTypeId ||
                         candidateItem.TypeID == FenHuangHalberdIds.WeaponTypeId))
                    {
                        continue;
                    }

                    GameObject fx = slashFx ? candidate.slashFx : candidate.hitFx;
                    if (fx == null) continue;

                    if (cachedFallbackHitFx == null && candidate.hitFx != null)
                    {
                        cachedFallbackHitFx = candidate.hitFx;
                    }

                    return fx;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 搜索近战特效引用失败: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 配置冰属性
        /// </summary>
        private static void ConfigureMeleeSetting(Item item)
        {
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (meleeSetting == null)
            {
                meleeSetting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            }

            meleeSetting.element = ElementTypes.ice;
            meleeSetting.dealExplosionDamage = false;

            // 设置冻伤 buff（原版 Cold buff，100% 触发）
            try
            {
                Duckov.Buffs.Buff coldBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Cold;
                if (coldBuff != null)
                {
                    meleeSetting.buff = coldBuff;
                    meleeSetting.buffChance = 1f;
                    ModBehaviour.DevLog("[Frostmourne] 已设置冻伤 buff (Cold, 100%)");
                }
                else
                {
                    meleeSetting.buffChance = 0f;
                    ModBehaviour.DevLog("[Frostmourne] [WARNING] 未找到原版 Cold buff");
                }
            }
            catch (Exception e)
            {
                meleeSetting.buffChance = 0f;
                ModBehaviour.DevLog("[Frostmourne] 设置冻伤 buff 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[Frostmourne] 已配置 ItemSetting_MeleeWeapon (element=Ice)");
        }

        // ========== 标签配置 ==========

        private static void ConfigureTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "MeleeWeapon");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
            EquipmentHelper.AddTagToItem(item, "DragonKing");

            try
            {
                item.SetBool("IsMeleeWeapon", true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] SetBool IsMeleeWeapon 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[Frostmourne] 已添加标签");
        }

        // ========== Modifier 配置 ==========

        /// <summary>
        /// 添加 ColdProtection +2 modifier
        /// </summary>
        private static void ConfigureModifiers(Item item)
        {
            try
            {
                if (HasColdProtectionModifier(item))
                {
                    ModBehaviour.DevLog("[Frostmourne] 已存在 ColdProtection modifier，跳过重复添加");
                    return;
                }

                EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, 2f, true);
                ModBehaviour.DevLog("[Frostmourne] 已添加 ColdProtection +2 modifier");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 添加 modifier 失败: " + e.Message);
            }
        }

        private static bool HasColdProtectionModifier(Item item)
        {
            if (item == null || item.Modifiers == null)
            {
                return false;
            }

            try
            {
                foreach (ModifierDescription mod in item.Modifiers)
                {
                    if (mod == null || !mod.Display)
                    {
                        continue;
                    }

                    if (mod.Key == "ColdProtection")
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 检查 ColdProtection modifier 失败: " + e.Message);
            }

            return false;
        }

        // ========== 本地化 ==========

        private static FrostmourneConfig _cachedConfig;

        private static void InjectLocalization(Item item)
        {
            try
            {
                if (_cachedConfig == null) _cachedConfig = new FrostmourneConfig();
                string displayName = L10n.T(_cachedConfig.DisplayNameCN, _cachedConfig.DisplayNameEN);
                string description = L10n.T(_cachedConfig.DescriptionCN, _cachedConfig.DescriptionEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                LocalizationHelper.InjectLocalization("frostmourne", displayName);
                LocalizationHelper.InjectLocalization("frostmourne_Desc", description);

                ModBehaviour.DevLog("[Frostmourne] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 本地化注入失败: " + e.Message);
            }
        }

        // ========== 渲染修复 ==========

        private static void DisableItemRenderers(GameObject go)
        {
            try
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r != null) r.enabled = false;
                }
            }
            catch { }
        }

        private static void DisableMotionBlur(GameObject go)
        {
            try
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r != null) r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }
            }
            catch { }
        }

        private static void FixModelGraphics(GameObject go)
        {
            try
            {
                SetLayerRecursively(go, 0);

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
                ModBehaviour.DevLog("[Frostmourne] FixModelGraphics 失败: " + e.Message);
            }
        }

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

    /// <summary>
    /// 冰色挥砍特效兼容组件（当找不到原版特效时使用）
    /// </summary>
    internal sealed class FrostmourneSlashFxCompat : MonoBehaviour
    {
        private static readonly Vector3 HiddenPosition = new Vector3(0f, -9999f, 0f);
        private const float Lifetime = 0.3f;

        internal static GameObject CreateTemplate(Vector3 scale)
        {
            GameObject template = new GameObject("Frostmourne_SlashFxTemplate");
            template.transform.position = HiddenPosition;
            template.transform.localScale = scale;
            template.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(template);
            template.AddComponent<FrostmourneSlashFxCompat>();
            return template;
        }

        private void Awake()
        {
            if (gameObject.name.IndexOf("(Clone)", StringComparison.Ordinal) >= 0)
            {
                UnityEngine.Object.Destroy(gameObject, Lifetime);
                return;
            }

            transform.position = HiddenPosition;
        }
    }
}
