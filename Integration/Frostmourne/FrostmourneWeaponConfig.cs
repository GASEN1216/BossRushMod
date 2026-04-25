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
using HarmonyLib;
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
        private const string IceAuraRootName = "Frostmourne_IceAura";
        private const string IceMistName = "Frostmourne_IceMist";
        private const string IceSparkName = "Frostmourne_IceSpark";
        private static GameObject cachedFallbackSlashFx;
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

                // 5.5 添加宝石槽位（2 个 Gem 槽）
                EquipmentHelper.ConfigureGemSlots(item, 2);

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
                    if (candidate == null) continue;

                    Item candidateItem = candidate.Item;
                    if (candidateItem != null && candidateItem.TypeID == FrostmourneIds.WeaponTypeId)
                    {
                        continue;
                    }

                    GameObject fx = slashFx ? candidate.slashFx : candidate.hitFx;
                    if (fx == null) continue;

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
                ModBehaviour.DevLog("[Frostmourne] 搜索近战特效引用失败: " + e.Message);
            }

            return null;
        }

        internal static void TryAddIceEffectsToGraphic(GameObject targetVisual)
        {
            if (targetVisual == null)
            {
                return;
            }

            FrostmourneRuntimeMaterialTracker materialTracker =
                FrostmourneRuntimeMaterialTracker.GetOrAdd(targetVisual);

            Transform existingRoot = FindChildRecursive(targetVisual.transform, IceAuraRootName);
            if (existingRoot != null)
            {
                EnsureIceEffectsPlaying(targetVisual);
                return;
            }

            try
            {
                RemoveOriginalPurpleEffects(targetVisual);
                CreateIceAuraEffects(targetVisual, materialTracker);
                ModBehaviour.DevLog("[Frostmourne] 已为目标添加冰焰环绕: " + targetVisual.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 添加冰焰环绕失败: " + e.Message);
            }
        }

        internal static void PrepareRuntimeHoldAgentVisual(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            DisableMotionBlur(go);
            FixModelGraphics(go);
            EnableRuntimeRenderers(go);
            TryAddIceEffectsToGraphic(go);
        }

        private static void RemoveOriginalPurpleEffects(GameObject go)
        {
            if (go == null) return;
            try
            {
                ParticleSystem[] particles = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem ps = particles[i];
                    if (ps == null || ps.gameObject == null) continue;
                    string name = ps.gameObject.name;
                    if (name.Contains("Ice") || name.Contains("Frostmourne"))
                    {
                        continue;
                    }
                    UnityEngine.Object.Destroy(ps.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] RemoveOriginalPurpleEffects 失败: " + e.Message);
            }
        }

        private static void CreateIceAuraEffects(
            GameObject targetVisual,
            FrostmourneRuntimeMaterialTracker materialTracker)
        {
            if (targetVisual == null)
            {
                return;
            }

            Transform existingRoot = FindChildRecursive(targetVisual.transform, IceAuraRootName);
            if (existingRoot != null)
            {
                EnsureIceEffectsPlaying(existingRoot.gameObject);
                return;
            }

            Vector3 localCenter;
            Vector3 localExtents;
            GetVisualBounds(targetVisual, out localCenter, out localExtents);

            GameObject auraRoot = new GameObject(IceAuraRootName);
            auraRoot.transform.SetParent(targetVisual.transform, false);
            auraRoot.transform.localPosition = localCenter;
            auraRoot.transform.localRotation = Quaternion.identity;
            auraRoot.transform.localScale = Vector3.one;

            GameObject sourceModel = GetFireAK47Model();
            if (sourceModel != null)
            {
                TryCopyAndTintAura(
                    sourceModel,
                    "Smoke",
                    auraRoot.transform,
                    localExtents,
                    false,
                    materialTracker);
            }
            EnsureIceEffectsPlaying(auraRoot);
        }

        private static GameObject GetFireAK47Model()
        {
            try
            {
                var prefab = ItemAssetsCollection.GetPrefab(862);  // FIRE_AK47_TYPE_ID
                if (prefab != null && prefab.ItemGraphic != null)
                {
                    return prefab.ItemGraphic.gameObject;
                }
            }
            catch { }
            return null;
        }

        private static void TryCopyAndTintAura(
            GameObject sourceModel,
            string name,
            Transform parentTarget,
            Vector3 localExtents,
            bool sparkMode,
            FrostmourneRuntimeMaterialTracker materialTracker)
        {
            Transform sourcePS = FindChildRecursive(sourceModel.transform, name);
            if (sourcePS == null) return;

            GameObject copy = UnityEngine.Object.Instantiate(sourcePS.gameObject, parentTarget);
            copy.name = name;
            copy.transform.localPosition = Vector3.zero;  // Centered on the Frostmourne bounds
            copy.transform.localRotation = sourcePS.localRotation;
            copy.transform.localScale = sourcePS.localScale;

            Color IceCoreColor = new Color(0.46f, 0.86f, 1f, 0.88f);
            Color IceFadeColor = new Color(0.88f, 0.97f, 1f, 0.55f);

            ParticleSystem[] psList = copy.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < psList.Length; i++)
            {
                ParticleSystem ps = psList[i];
                if (ps == null) continue;

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = new ParticleSystem.MinMaxGradient(IceCoreColor, IceFadeColor);

                var emission = ps.emission;
                emission.rateOverTime = sparkMode ? 5f : 8f; // lowered

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(
                    Mathf.Max(0.06f, localExtents.x * 1.1f),
                    Mathf.Max(0.18f, localExtents.y * 1.0f),
                    Mathf.Max(0.06f, localExtents.z * 1.0f));

                var velocityOverLifetime = ps.velocityOverLifetime;
                velocityOverLifetime.enabled = true;
                velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
                velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(sparkMode ? 0.08f : 0.04f);
                velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(sparkMode ? 0.02f : 0.01f);
                velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(sparkMode ? 0.02f : 0.01f);

                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(IceCoreColor, 0f),
                        new GradientColorKey(IceFadeColor, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(sparkMode ? 0.95f : 0.65f, 0f),
                        new GradientAlphaKey(sparkMode ? 0.45f : 0.28f, 0.6f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );

                var colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                }
            }

            Renderer[] renderers = copy.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterials == null) continue;

                Material[] materials = renderer.sharedMaterials;
                Material[] tintedMaterials = new Material[materials.Length];

                for (int j = 0; j < materials.Length; j++)
                {
                    Material source = materials[j];
                    if (source == null) continue;

                    Material tinted = new Material(source);
                    if (tinted.HasProperty("_Color")) tinted.color = IceCoreColor;
                    if (tinted.HasProperty("_TintColor")) tinted.SetColor("_TintColor", IceCoreColor);
                    if (tinted.HasProperty("_EmissionColor")) tinted.SetColor("_EmissionColor", IceCoreColor * 0.45f);

                    tintedMaterials[j] = tinted;
                    if (materialTracker != null)
                    {
                        materialTracker.Track(tinted);
                    }
                }

                renderer.sharedMaterials = tintedMaterials;
            }

            Light[] lights = copy.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                light.color = IceCoreColor;
                light.range = 1f;  // lowered for weapon aura
                light.intensity = 1f;
            }

            copy.SetActive(true);
        }

        private static void GetVisualBounds(GameObject targetVisual, out Vector3 localCenter, out Vector3 localExtents)
        {
            localCenter = Vector3.zero;
            localExtents = new Vector3(0.08f, 0.45f, 0.08f);

            if (targetVisual == null)
            {
                return;
            }

            Renderer[] renderers = targetVisual.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combinedBounds = new Bounds(targetVisual.transform.position, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return;
            }

            localCenter = targetVisual.transform.InverseTransformPoint(combinedBounds.center);
            localExtents = combinedBounds.extents;
        }

        private static void EnsureIceEffectsPlaying(GameObject targetVisual)
        {
            if (targetVisual == null)
            {
                return;
            }

            Transform auraRoot = FindChildRecursive(targetVisual.transform, IceAuraRootName);
            if (auraRoot == null)
            {
                return;
            }

            ParticleSystem[] particles = auraRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if (ps != null && !ps.isPlaying)
                {
                    ps.Play(true);
                }
            }
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

        private static void EnableRuntimeRenderers(GameObject go)
        {
            try
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    r.enabled = true;
                    r.forceRenderingOff = false;
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

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (string.Equals(root.name, targetName, StringComparison.Ordinal))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    internal sealed class FrostmourneRuntimeMaterialTracker : MonoBehaviour
    {
        private readonly List<Material> trackedMaterials = new List<Material>();

        internal static FrostmourneRuntimeMaterialTracker GetOrAdd(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            FrostmourneRuntimeMaterialTracker tracker =
                target.GetComponent<FrostmourneRuntimeMaterialTracker>();
            if (tracker == null)
            {
                tracker = target.AddComponent<FrostmourneRuntimeMaterialTracker>();
            }

            return tracker;
        }

        internal void Track(Material material)
        {
            if (material == null || trackedMaterials.Contains(material))
            {
                return;
            }

            trackedMaterials.Add(material);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < trackedMaterials.Count; i++)
            {
                Material material = trackedMaterials[i];
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            trackedMaterials.Clear();
        }
    }

    public static class FrostmourneHoldItemPatch
    {
    }
}
