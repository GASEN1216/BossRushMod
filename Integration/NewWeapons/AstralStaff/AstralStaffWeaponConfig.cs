// ============================================================================
// AstralStaffWeaponConfig.cs - 星阙物品注册与配置
// ============================================================================
// 没有任何 bundle：克隆 Mod 自己的通用物品（船票 → 冒险家日志 → 冷淬液）当底，
// 写入 TypeID / 近战 Stat / 近战代理 / 标签 / 品质价值耐久，再挂代码生成的图标。
// 手上拿的是官方空手持代理（Handheld 预制体缺失时的官方回退），
// RuntimeConfiguredMeleeHoldItemPatch 会给它补 ItemAgent_MeleeWeapon，
// PrepareRuntimeHoldAgentVisual 再往上挂光棍（AstralStaffHandVisual）。
//
// 幂等：TryConfigure 会被 ItemFactory 配置器、动态注册、读档补配重复调用，结果一致。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    internal static class AstralStaffWeaponConfig
    {
        private static readonly HashSet<string> DisplayStats = new HashSet<string>
        {
            "Damage", "MoveSpeedMultiplier", "CritRate", "CritDamageFactor",
            "ArmorPiercing", "AttackSpeed", "AttackRange", "StaminaCost"
        };

        // ====================================================================
        // 注册
        // ====================================================================

        /// <summary>
        /// 确保 500104 已进 ItemAssetsCollection。BossRushDynamicItemRegistry 的 FallbackLoader 调它。
        /// </summary>
        internal static bool EnsureRuntimeRegistration()
        {
            return RuntimeItemFallback.Ensure(AstralStaffConfig.TypeId, AstralStaffConfig.PrefabName,
                AstralStaffConfig.LogPrefix, FindCloneSource, clone =>
            {
                try { clone.Variables.Clear(); } catch { /* 克隆源没有变量表时忽略 */ }
                try { if (clone.Modifiers != null) clone.Modifiers.Clear(); } catch { /* 同上 */ }
                if (clone.Tags != null) clone.Tags.Clear();

                ItemSetting_Gun gunSetting = clone.GetComponent<ItemSetting_Gun>();
                if (gunSetting != null) UnityEngine.Object.DestroyImmediate(gunSetting, true);
                // 克隆的是消耗品底壳，不能让遗留的「使用」行为消耗这把武器或触发船票/日志。
                UsageBehavior[] usages = clone.GetComponentsInChildren<UsageBehavior>(true);
                for (int i = 0; i < usages.Length; i++) UnityEngine.Object.DestroyImmediate(usages[i], true);
                UsageUtilities utility = clone.GetComponent<UsageUtilities>();
                if (utility != null) UnityEngine.Object.DestroyImmediate(utility, true);

                EquipmentFactory.RegisterMeleeWeapon(AstralStaffConfig.TypeId);
                if (!TryConfigure(clone))
                    throw new InvalidOperationException("星陨法杖配置未完成");
            }, existing => TryConfigure(existing));
        }

        private static Item FindCloneSource()
        {
            return RuntimeItemFallback.FindSource(new[] { BossRushItemIds.BossRushTicket, BossRushItemIds.AdventureJournal, 500014 });
        }

        // ====================================================================
        // 配置
        // ====================================================================

        /// <summary>写入全部近战配置。prefab 与实例都可调用，重复调用结果一致。</summary>
        internal static bool TryConfigure(Item item)
        {
            if (item == null) return false;
            try
            {
                ApplyStats(item);
                ApplyMeleeAgent(item);
                ApplyMeleeSetting(item);
                ApplyTags(item);
                ApplyAttributes(item);
                DisableItemRenderers(item);
                InjectLocalization();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 配置失败: " + e.Message);
                return false;
            }
        }

        private static void ApplyStats(Item item)
        {
            StatCollection stats = item.Stats;
            if (stats == null)
            {
                item.CreateStatsComponent();
                stats = item.Stats;
            }
            if (stats == null) return;

            SetStat(stats, "Damage", AstralStaffConfig.Damage);
            SetStat(stats, "DamageFactorToZombie", 1f);
            SetStat(stats, "MoveSpeedMultiplier", AstralStaffConfig.MoveSpeedMultiplier);
            SetStat(stats, "BlockBullet", AstralStaffConfig.BlockBullet);
            SetStat(stats, "CritRate", AstralStaffConfig.CritRate);
            SetStat(stats, "CritDamageFactor", AstralStaffConfig.CritDamageFactor);
            SetStat(stats, "ArmorPiercing", AstralStaffConfig.ArmorPiercing);
            SetStat(stats, "AttackSpeed", AstralStaffConfig.AttackSpeed);
            SetStat(stats, "AttackRange", AstralStaffConfig.AttackRange);
            SetStat(stats, "DealDamageTime", AstralStaffConfig.DealDamageTime);
            SetStat(stats, "StaminaCost", AstralStaffConfig.StaminaCost);
            SetStat(stats, "BleedChance", AstralStaffConfig.BleedChance);

            // StatCollection 第一次 GetStat 后缓存字典，之后 Add 的 Stat 进不去，
            // 战斗中读到 0（伤害/攻速/范围全失效）——与噬魂挽歌同一个坑。
            InvalidateStatsDictionary(stats);
        }

        private static void SetStat(StatCollection stats, string key, float value)
        {
            Stat existing = stats.GetStat(key);
            if (existing != null)
            {
                existing.BaseValue = value;
                return;
            }
            stats.Add(new Stat(key, value, DisplayStats.Contains(key)));
        }

        private static void InvalidateStatsDictionary(StatCollection stats)
        {
            try
            {
                FieldInfo dictField = typeof(StatCollection).GetField(
                    "_cachedStatsDictionary", BindingFlags.NonPublic | BindingFlags.Instance);
                if (dictField != null) dictField.SetValue(stats, null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 重置 Stat 字典失败: " + e.Message);
            }
        }

        private static void ApplyMeleeAgent(Item item)
        {
            ItemAgent_MeleeWeapon agent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (agent == null) agent = item.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
            agent.handheldSocket = HandheldSocketTypes.normalHandheld;
            agent.handAnimationType = HandheldAnimationType.meleeWeapon;
            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null) soundKeyField.SetValue(agent, "Default");
            }
            catch { /* 写不进去就用默认音效键 */ }
            // 轻击刀光由 AstralStaffAttackPatch 画金色光弧。slashFx 仍挂一个空占位：
            // 第三方 VTModifiers 的 CA_Attack.OnStop 后缀不判空就改 meleeWeapon.slashFx 的缩放，
            // 字段为空时每次收招都抛 NRE（2026-10-08 Player.log 实证，IL 偏移 0x32）。占位被官方实例化后当帧自毁。
            agent.slashFx = AstralStaffSlashStub.GetTemplate();
        }

        private static void ApplyMeleeSetting(Item item)
        {
            ItemSetting_MeleeWeapon setting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (setting == null) setting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            setting.element = ElementTypes.physics;
            setting.dealExplosionDamage = false;
            setting.buffChance = 0f;
        }

        private static void ApplyTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "MeleeWeapon");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
            try { item.SetBool("IsMeleeWeapon", true, true); } catch { /* 旧版本没有该变量 */ }
        }

        private static void ApplyAttributes(Item item)
        {
            item.Quality = AstralStaffConfig.Quality;
            // 官方 Durability setter 会被 MaxDurability 钳住，先写上限
            item.MaxDurability = AstralStaffConfig.MaxDurability;
            if (item.Durability <= 0f) item.Durability = AstralStaffConfig.MaxDurability;
            EquipmentHelper.AddRepairableTag(item);
            item.MaxStackCount = 1;
            if (item.StackCount <= 0) item.StackCount = 1;
            item.Value = AstralStaffConfig.Value;
            item.DisplayNameRaw = AstralStaffConfig.DisplayNameKey;
            // 没有模型：地上掉落时用图标精灵代替模型（官方 InteractablePickup.CreateGraphic）
            item.useSpriteForPickup = true;
        }

        private static void DisableItemRenderers(Item item)
        {
            try
            {
                Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = false;
                }
            }
            catch { /* 克隆源没有渲染器时无事可做 */ }
        }

        // ====================================================================
        // 手持表现
        // ====================================================================

        /// <summary>
        /// 运行时手持代理就绪后挂光棍。RuntimeConfiguredMeleeHoldItemPatch 每次换到星阙都会调一次，
        /// 代理随换手销毁，光棍跟着走，不需要另外收尾。
        /// </summary>
        internal static void PrepareRuntimeHoldAgentVisual(GameObject holdAgent)
        {
            if (holdAgent == null) return;
            try
            {
                if (holdAgent.GetComponent<AstralStaffHandVisual>() == null)
                {
                    holdAgent.AddComponent<AstralStaffHandVisual>();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 挂光棍失败: " + e.Message);
            }
        }

        /// <summary>仅本场冠军显式生成的棍卫可启用 NPC 光棍，不启动玩家蓄势或 HUD。</summary>
        internal static void PrepareSummonedMinionHoldAgentVisual(GameObject holdAgent, SandstormChampionMinionMarker marker)
        {
            if (holdAgent == null || marker == null || marker.Character == null
                || !SandstormChampionMinionMarker.IsSummonedMinion(marker.Character)) return;
            PrepareRuntimeHoldAgentVisual(holdAgent);
            AstralStaffHandVisual visual = holdAgent.GetComponent<AstralStaffHandVisual>();
            if (visual != null) visual.BindSummonedMinion(marker);
        }

        // ====================================================================
        // 本地化
        // ====================================================================

        /// <summary>语言在取用时解析：玩家切语言后宿主会重跑整条注入链。</summary>
        internal static void InjectLocalization()
        {
            try
            {
                string name = L10n.T(AstralStaffConfig.DisplayNameCN, AstralStaffConfig.DisplayNameEN);
                string desc = L10n.T(AstralStaffConfig.DescriptionCN, AstralStaffConfig.DescriptionEN);
                LocalizationHelper.InjectLocalization(AstralStaffConfig.DisplayNameKey, name);
                LocalizationHelper.InjectLocalization(AstralStaffConfig.DisplayNameKey + "_Desc", desc);
                LocalizationHelper.InjectLocalization("Item_" + AstralStaffConfig.TypeId, name);
                LocalizationHelper.InjectLocalization("Item_" + AstralStaffConfig.TypeId + "_Desc", desc);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 本地化注入失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            AstralStaffSlashStub.ResetStaticCaches();
        }
    }

    /// <summary>
    /// 星阙的空 slashFx 占位：没有渲染器、没有逻辑。模板常驻且标记为模板；
    /// 官方 CA_Attack 每次挥击 Instantiate 出来的副本没有这个标记（私有字段不随实例化复制），Awake 里立刻自毁。
    /// </summary>
    internal sealed class AstralStaffSlashStub : MonoBehaviour
    {
        private static GameObject template;
        private bool _isTemplate;

        internal static GameObject GetTemplate()
        {
            if (template != null) return template;
            GameObject go = new GameObject("AstralStaff_SlashStub");
            go.SetActive(false);
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AstralStaffSlashStub>()._isTemplate = true;
            go.SetActive(true);
            template = go;
            return template;
        }

        private void Awake()
        {
            if (!_isTemplate) Destroy(gameObject);
        }

        internal static void ResetStaticCaches()
        {
            if (template != null) UnityEngine.Object.Destroy(template);
            template = null;
        }
    }
}
