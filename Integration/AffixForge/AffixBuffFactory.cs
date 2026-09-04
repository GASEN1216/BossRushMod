// ============================================================================
// AffixBuffFactory.cs - 词缀用运行时 Buff 预制体工厂（磐石 / 迅手 / 狂潮）
// ============================================================================
// 模块职责：
//   运行时（不依赖任何 AssetBundle）构造三种 Buff 预制体，形态逐字照
//   PhantomWitchAssetManager.GetCurseBuff：根 GO 先 SetActive(false) → 挂 Buff →
//   反射写私有字段 → 建 Effect + TriggerOnSetItem + ModifierAction 子 GO →
//   把 trigger/action 塞进 Effect 的列表、把 Effect 塞进 Buff.effects →
//   最后 SetActive(true) 触发 Awake 完成组装。
//
// 硬约束：
//   1. 每个强度档一个独立预制体（3 种 × 3 档 = 9 个 GameObject）。
//      ModifierAction.Awake 已经把 modifier 用当时的 modifierValue 构造好了，
//      运行时再改 modifierValue 字段【不会】生效，所以不能复用一个预制体改数值。
//   2. Buff.ID 相同的 Buff 再次 AddBuff 会走官方 NotifyIncomingBuffWithSameID：
//      刷新 timeWhenStarted 且在未达 maxLayers 时叠层。这正是"击杀叠层 + 刷新时长"
//      的现成语义，不需要我们自己写叠层逻辑。
//   3. 数值单点在 AffixDefinitions，本文件只查表，不硬编码幅度。
//   4. 全部 GO 走 DontDestroyOnLoad，ResetStaticCaches 必须显式 Destroy，
//      否则切场景不回收。
//   5. 零新增 Harmony patch；反射只用于官方私有字段（与现役 PhantomWitch 同一套）。
// ============================================================================

using System;
using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 词缀 Buff 工厂。三种 Buff 各 3 档，懒建 + 缓存。
    /// </summary>
    public static class AffixBuffFactory
    {
        // ---- Buff.ID 基址。实际 ID = 基址 + tier(1..3)，三段互不重叠 ----
        // Buff.ID 与物品 TypeID 不同域，取 5006xx 段只为便于排查。
        /// <summary>磐石 Buff ID 基址，实际占用 500602 / 500603 / 500604。</summary>
        public const int BuffId_Bulwark = 500601;
        /// <summary>迅手 Buff ID 基址，实际占用 500605 / 500606 / 500607。</summary>
        public const int BuffId_SwiftHand = 500604;
        /// <summary>狂潮 Buff ID 基址，实际占用 500608 / 500609 / 500610。</summary>
        public const int BuffId_Frenzy = 500607;

        // ---- 本地化键 ----
        public const string LOC_BULWARK_NAME = "Buff_BossRush_Affix_Bulwark_Name";
        public const string LOC_BULWARK_DESC = "Buff_BossRush_Affix_Bulwark_Desc";
        public const string LOC_SWIFTHAND_NAME = "Buff_BossRush_Affix_SwiftHand_Name";
        public const string LOC_SWIFTHAND_DESC = "Buff_BossRush_Affix_SwiftHand_Desc";
        public const string LOC_FRENZY_NAME = "Buff_BossRush_Affix_Frenzy_Name";
        public const string LOC_FRENZY_DESC = "Buff_BossRush_Affix_Frenzy_Desc";

        // ---- Buff 规格常量 ----
        private const float BULWARK_LIFETIME = 4f;
        private const int BULWARK_MAX_LAYERS = 3;
        private const float SWIFTHAND_LIFETIME = 5f;
        private const int SWIFTHAND_MAX_LAYERS = 2;
        private const float FRENZY_LIFETIME = 6f;
        private const int FRENZY_MAX_LAYERS = 5;

        private const int TIER_COUNT = 3;

        private static readonly string[] BulwarkStatKeys = { "BodyArmor", "HeadArmor" };
        private static readonly string[] SwiftHandStatKeys = { "ReloadSpeedGain" };
        private static readonly string[] FrenzyStatKeys = { "GunShootSpeedMultiplier", "Moveability" };

        // ---- 缓存：索引 = tier - 1 ----
        private static readonly Buff[] cachedBulwarkBuffs = new Buff[TIER_COUNT];
        private static readonly Buff[] cachedSwiftHandBuffs = new Buff[TIER_COUNT];
        private static readonly Buff[] cachedFrenzyBuffs = new Buff[TIER_COUNT];
        private static readonly GameObject[] cachedBulwarkGOs = new GameObject[TIER_COUNT];
        private static readonly GameObject[] cachedSwiftHandGOs = new GameObject[TIER_COUNT];
        private static readonly GameObject[] cachedFrenzyGOs = new GameObject[TIER_COUNT];

        private static bool localizationInjected;

        // ---- 反射字段（与 PhantomWitchAssetManager 同一套字段名，已逐个核实）----
        private static bool buffReflectionInitialized;
        private static FieldInfo buffIdField;
        private static FieldInfo buffLimitedLifeTimeField;
        private static FieldInfo buffTotalLifeTimeField;
        private static FieldInfo buffMaxLayersField;
        private static FieldInfo buffDisplayNameField;
        private static FieldInfo buffDescriptionField;
        private static FieldInfo buffIconField;
        private static FieldInfo buffExclusiveTagField;
        private static FieldInfo buffEffectsField;
        private static FieldInfo effectTriggersField;
        private static FieldInfo effectActionsField;
        private static FieldInfo modifierBuffField;

        // ====================================================================
        // 对外接口
        // ====================================================================

        /// <summary>磐石：受击短暂加身体/头部护甲（加点型，maxLayers 3，4 秒）。</summary>
        public static Buff GetBulwarkBuff(int tier)
        {
            return GetOrBuild(
                tier,
                cachedBulwarkBuffs,
                cachedBulwarkGOs,
                AffixDefinitions.Id_Bulwark,
                BuffId_Bulwark,
                LOC_BULWARK_NAME,
                LOC_BULWARK_DESC,
                BULWARK_LIFETIME,
                BULWARK_MAX_LAYERS,
                BulwarkStatKeys,
                ModifierType.Add,
                "BossRush_Affix_Bulwark");
        }

        /// <summary>迅手：击杀短暂加换弹速（加点型，maxLayers 2，5 秒）。</summary>
        public static Buff GetSwiftHandBuff(int tier)
        {
            return GetOrBuild(
                tier,
                cachedSwiftHandBuffs,
                cachedSwiftHandGOs,
                AffixDefinitions.Id_SwiftHand,
                BuffId_SwiftHand,
                LOC_SWIFTHAND_NAME,
                LOC_SWIFTHAND_DESC,
                SWIFTHAND_LIFETIME,
                SWIFTHAND_MAX_LAYERS,
                SwiftHandStatKeys,
                ModifierType.Add,
                "BossRush_Affix_SwiftHand");
        }

        /// <summary>狂潮：击杀叠攻速 / 移速（百分比型，maxLayers 5，6 秒）。</summary>
        public static Buff GetFrenzyBuff(int tier)
        {
            return GetOrBuild(
                tier,
                cachedFrenzyBuffs,
                cachedFrenzyGOs,
                AffixDefinitions.Id_Frenzy,
                BuffId_Frenzy,
                LOC_FRENZY_NAME,
                LOC_FRENZY_DESC,
                FRENZY_LIFETIME,
                FRENZY_MAX_LAYERS,
                FrenzyStatKeys,
                ModifierType.PercentageAdd,
                "BossRush_Affix_Frenzy");
        }

        /// <summary>
        /// 注入三个 Buff 的名称/描述本地化。幂等；
        /// AffixForgeLocalization 若也注同名键，后写覆盖前写，值相同无副作用。
        ///
        /// **不能用一次性闩挡住重入**：文案是注入那一刻用 `L10n.T(中,英)` 求值后
        /// 写进 SetOverrideText 的，玩家切语言时需要整体重注入一遍
        /// （ModBehaviour 订阅了 LocalizationManager.OnSetLanguage）。
        /// 闩住的话这三条会永远停在启动时的语言。注入本身是字典覆盖写，重跑无副作用。
        /// </summary>
        public static void InjectLocalization()
        {
            localizationInjected = true;

            try
            {
                LocalizationHelper.InjectLocalization(LOC_BULWARK_NAME, L10n.T("磐石", "Bulwark"));
                LocalizationHelper.InjectLocalization(LOC_BULWARK_DESC, L10n.T("受击后短暂提升护甲，可叠加。", "Briefly gains armor after being hit. Stacks."));
                LocalizationHelper.InjectLocalization(LOC_SWIFTHAND_NAME, L10n.T("迅手", "Swift Hand"));
                LocalizationHelper.InjectLocalization(LOC_SWIFTHAND_DESC, L10n.T("击杀后短暂提升换弹速度，可叠加。", "Briefly gains reload speed after a kill. Stacks."));
                LocalizationHelper.InjectLocalization(LOC_FRENZY_NAME, L10n.T("狂潮", "Frenzy"));
                LocalizationHelper.InjectLocalization(LOC_FRENZY_DESC, L10n.T("击杀后短暂提升射速与机动性，可叠加。", "Briefly gains fire rate and mobility after a kill. Stacks."));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] [WARNING] Buff 本地化注入失败: " + e.Message);
            }
        }

        /// <summary>销毁全部缓存 GO 并清引用。切场景不会自动回收 DontDestroyOnLoad 对象。</summary>
        public static void ResetStaticCaches()
        {
            DestroyCachedSet(cachedBulwarkBuffs, cachedBulwarkGOs);
            DestroyCachedSet(cachedSwiftHandBuffs, cachedSwiftHandGOs);
            DestroyCachedSet(cachedFrenzyBuffs, cachedFrenzyGOs);

            buffReflectionInitialized = false;
            buffIdField = null;
            buffLimitedLifeTimeField = null;
            buffTotalLifeTimeField = null;
            buffMaxLayersField = null;
            buffDisplayNameField = null;
            buffDescriptionField = null;
            buffIconField = null;
            buffExclusiveTagField = null;
            buffEffectsField = null;
            effectTriggersField = null;
            effectActionsField = null;
            modifierBuffField = null;

            localizationInjected = false;
        }

        // ====================================================================
        // 内部实现
        // ====================================================================

        private static void DestroyCachedSet(Buff[] buffs, GameObject[] gos)
        {
            for (int i = 0; i < TIER_COUNT; i++)
            {
                try
                {
                    if (gos[i] != null)
                    {
                        UnityEngine.Object.Destroy(gos[i]);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[AffixForge] [WARNING] 销毁 Buff 预制体失败: " + e.Message);
                }

                gos[i] = null;
                buffs[i] = null;
            }
        }

        private static Buff GetOrBuild(
            int tier,
            Buff[] cache,
            GameObject[] goCache,
            string affixId,
            int buffIdBase,
            string nameKey,
            string descKey,
            float lifeTime,
            int maxLayers,
            string[] statKeys,
            ModifierType modifierType,
            string goName)
        {
            if (tier < 1) tier = 1;
            if (tier > TIER_COUNT) tier = TIER_COUNT;
            int index = tier - 1;

            if (cache[index] != null)
            {
                return cache[index];
            }

            try
            {
                AffixDefinition def = AffixDefinitions.Find(affixId);
                if (def == null)
                {
                    ModBehaviour.DevLog("[AffixForge] [WARNING] 未找到词缀定义: " + affixId);
                    return null;
                }

                float perLayerValue = AffixDefinitions.GetTierValue(def, tier);

                GameObject root;
                Buff buff = BuildBuff(
                    buffIdBase + tier,
                    nameKey,
                    descKey,
                    lifeTime,
                    maxLayers,
                    statKeys,
                    modifierType,
                    perLayerValue,
                    goName + "_T" + tier,
                    affixId,
                    out root);

                if (buff == null)
                {
                    return null;
                }

                cache[index] = buff;
                goCache[index] = root;
                return buff;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] [ERROR] 构建 Buff 失败(" + affixId + " T" + tier + "): " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 真正的组装过程。逐字照 PhantomWitchAssetManager.GetCurseBuff 的顺序：
        /// 先 inactive → 写字段 → 建 Effect 链 → 最后 SetActive(true) 触发 Awake。
        /// </summary>
        private static Buff BuildBuff(
            int id,
            string nameKey,
            string descKey,
            float lifeTime,
            int maxLayers,
            string[] statKeys,
            ModifierType modifierType,
            float perLayerValue,
            string goName,
            string affixId,
            out GameObject root)
        {
            root = null;

            InitBuffReflection();
            InjectLocalization();

            GameObject buffRoot = new GameObject(goName);
            buffRoot.SetActive(false);
            // 只用 DontDestroyOnLoad，不设 HideFlags —— 与现役 PhantomWitchAssetManager
            // 完全同形态。HideAndDontSave 在这条组装链上没有被验证过，不引入变量。
            UnityEngine.Object.DontDestroyOnLoad(buffRoot);

            Buff buff = buffRoot.AddComponent<Buff>();

            SetFieldSafe(buff, buffIdField, id);
            SetFieldSafe(buff, buffLimitedLifeTimeField, true);
            SetFieldSafe(buff, buffTotalLifeTimeField, lifeTime);
            SetFieldSafe(buff, buffMaxLayersField, maxLayers);
            SetFieldSafe(buff, buffDisplayNameField, nameKey);
            SetFieldSafe(buff, buffDescriptionField, descKey);
            SetFieldSafe(buff, buffIconField, TryLoadAffixIcon(affixId));

            if (buffExclusiveTagField != null)
            {
                try
                {
                    object notExclusive = Enum.Parse(buffExclusiveTagField.FieldType, "NotExclusive");
                    SetFieldSafe(buff, buffExclusiveTagField, notExclusive);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[AffixForge] [WARNING] 设置 ExclusiveTag 失败: " + e.Message);
                }
            }

            for (int i = 0; i < statKeys.Length; i++)
            {
                GameObject effectChild = new GameObject("Effect_" + statKeys[i]);
                effectChild.transform.SetParent(buffRoot.transform, false);

                Effect effect = effectChild.AddComponent<Effect>();
                TriggerOnSetItem trigger = effectChild.AddComponent<TriggerOnSetItem>();
                ModifierAction modifier = effectChild.AddComponent<ModifierAction>();

                modifier.targetStatKey = statKeys[i];
                modifier.ModifierType = modifierType;
                modifier.modifierValue = perLayerValue;

                // ModifierAction.buff 是私有字段，必须反射写；
                // 写上之后 OnBuffLayerChanged 才会把 modifier.Value 乘上当前层数。
                SetFieldSafe(modifier, modifierBuffField, buff);

                if (effectTriggersField != null)
                {
                    System.Collections.IList triggers = effectTriggersField.GetValue(effect) as System.Collections.IList;
                    if (triggers != null)
                    {
                        triggers.Add(trigger);
                    }
                }

                if (effectActionsField != null)
                {
                    System.Collections.IList actions = effectActionsField.GetValue(effect) as System.Collections.IList;
                    if (actions != null)
                    {
                        actions.Add(modifier);
                    }
                }

                if (buffEffectsField != null)
                {
                    System.Collections.IList effects = buffEffectsField.GetValue(buff) as System.Collections.IList;
                    if (effects != null)
                    {
                        effects.Add(effect);
                    }
                }
            }

            // 组装完毕再激活，触发 Awake 让 EffectComponent 自己找 Master
            buffRoot.SetActive(true);

            root = buffRoot;
            return buff;
        }

        /// <summary>词缀图标缺文件时返回 null，Buff 照常工作（只是没图标）。</summary>
        private static Sprite TryLoadAffixIcon(string affixId)
        {
            try
            {
                string relativePath = AffixDefinitions.GetIconRelativePath(AffixDefinitions.Find(affixId));
                if (string.IsNullOrEmpty(relativePath))
                {
                    return null;
                }

                return ItemFactory.GetSpriteFromFile(relativePath);
            }
            catch
            {
                return null;
            }
        }

        private static void InitBuffReflection()
        {
            if (buffReflectionInitialized)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type buffType = typeof(Buff);
            buffIdField = buffType.GetField("id", flags);
            buffLimitedLifeTimeField = buffType.GetField("limitedLifeTime", flags);
            buffTotalLifeTimeField = buffType.GetField("totalLifeTime", flags);
            buffMaxLayersField = buffType.GetField("maxLayers", flags);
            buffDisplayNameField = buffType.GetField("displayName", flags);
            buffDescriptionField = buffType.GetField("description", flags);
            buffIconField = buffType.GetField("icon", flags);
            buffExclusiveTagField = buffType.GetField("exclusiveTag", flags);
            buffEffectsField = buffType.GetField("effects", flags);

            Type effectType = typeof(Effect);
            effectTriggersField = effectType.GetField("triggers", flags);
            effectActionsField = effectType.GetField("actions", flags);

            modifierBuffField = typeof(ModifierAction).GetField("buff", flags);

            buffReflectionInitialized = true;
        }

        private static void SetFieldSafe(object target, FieldInfo field, object value)
        {
            if (target == null || field == null)
            {
                return;
            }

            try
            {
                if (value == null)
                {
                    field.SetValue(target, null);
                    return;
                }

                Type fieldType = field.FieldType;
                Type valueType = value.GetType();

                if (fieldType.IsAssignableFrom(valueType))
                {
                    field.SetValue(target, value);
                    return;
                }

                if (fieldType.IsEnum && valueType == typeof(string))
                {
                    field.SetValue(target, Enum.Parse(fieldType, (string)value));
                    return;
                }

                field.SetValue(target, Convert.ChangeType(value, fieldType));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] [WARNING] SetFieldSafe 失败: " + field.Name + " - " + e.Message);
            }
        }
    }
}
