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

        private static Texture2D iconTexture;
        private static Sprite iconSprite;

        // ====================================================================
        // 注册
        // ====================================================================

        /// <summary>
        /// 确保 500104 已进 ItemAssetsCollection。BossRushDynamicItemRegistry 的 FallbackLoader 调它。
        /// </summary>
        internal static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(AstralStaffConfig.TypeId); } catch { /* 未注册时官方会抛 */ }
                if (existing != null)
                {
                    TryConfigure(existing);
                    return true;
                }

                Item source = FindCloneSource();
                if (source == null)
                {
                    ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " 找不到克隆源物品，跳过注册");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null) return false;

                clone.gameObject.name = AstralStaffConfig.PrefabName;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(AstralStaffConfig.TypeId);

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
                ItemAssetsCollection.AddDynamicEntry(clone);

                bool configured = TryConfigure(clone);
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " 已注册 (TypeID=" + AstralStaffConfig.TypeId
                    + ", configured=" + configured + ")");
                return configured;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 注册失败: " + e.Message);
                return false;
            }
        }

        private static Item FindCloneSource()
        {
            int[] ids = { BossRushItemIds.BossRushTicket, BossRushItemIds.AdventureJournal, 500014 };
            for (int i = 0; i < ids.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(ids[i]);
                    if (prefab != null) return prefab;
                }
                catch { /* 继续找下一个 */ }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(ids[i]);
                    if (loaded != null) return loaded;
                }
                catch { /* 继续找下一个 */ }
            }
            return null;
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
                ApplyIcon(item);
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
            // 不挂官方 slashFx：轻击刀光由 AstralStaffAttackPatch 画金色光弧
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
        // 图标（代码绘制）
        // ====================================================================

        private static void ApplyIcon(Item item)
        {
            Sprite sprite = GetIconSprite();
            if (sprite != null) item.Icon = sprite;
        }

        internal static Sprite GetIconSprite()
        {
            if (iconSprite != null && iconTexture != null) return iconSprite;
            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "AstralStaff_Icon";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            Vector2 a = new Vector2(40f, 36f);
            Vector2 b = new Vector2(216f, 220f);
            Vector2[] beans = { new Vector2(70f, 190f), new Vector2(98f, 214f), new Vector2(46f, 160f) };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = DistanceToSegment(p, a, b);
                    // 白芯 + 金辉 + 青色外晕，层层加色
                    float core = Mathf.Clamp01(1f - d / 3.2f);
                    float glow = Mathf.Exp(-d * d / 90f);
                    float halo = Mathf.Exp(-d * d / 700f) * 0.45f;
                    float r = core + glow * 1f + halo * 0.35f;
                    float g = core + glow * 0.78f + halo * 0.9f;
                    float bl = core + glow * 0.3f + halo * 1f;
                    float alpha = Mathf.Clamp01(core + glow * 0.95f + halo);

                    // 两端星芒
                    alpha = AddStar(p, a, 26f, ref r, ref g, ref bl, alpha);
                    alpha = AddStar(p, b, 30f, ref r, ref g, ref bl, alpha);
                    for (int i = 0; i < beans.Length; i++)
                    {
                        alpha = AddStar(p, beans[i], 13f, ref r, ref g, ref bl, alpha);
                    }

                    pixels[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f),
                        (byte)(Mathf.Clamp01(bl) * 255f), (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            if (iconSprite != null) UnityEngine.Object.Destroy(iconSprite);
            if (iconTexture != null) UnityEngine.Object.Destroy(iconTexture);
            iconTexture = texture;
            iconSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            iconSprite.name = "AstralStaff_IconSprite";
            iconSprite.hideFlags = HideFlags.HideAndDontSave;
            return iconSprite;
        }

        private static float AddStar(Vector2 p, Vector2 c, float radius, ref float r, ref float g, ref float b, float alpha)
        {
            Vector2 d = p - c;
            float dist = d.magnitude;
            if (dist > radius * 1.6f) return alpha;
            // 四芒星：两条细长十字 + 圆形亮核
            float cross = Mathf.Max(
                Mathf.Exp(-(d.x * d.x) / 4f) * Mathf.Clamp01(1f - Mathf.Abs(d.y) / radius),
                Mathf.Exp(-(d.y * d.y) / 4f) * Mathf.Clamp01(1f - Mathf.Abs(d.x) / radius));
            float dot = Mathf.Exp(-dist * dist / (radius * 1.8f));
            float s = Mathf.Clamp01(cross + dot);
            r += s;
            g += s * 0.92f;
            b += s * 0.7f;
            return Mathf.Clamp01(alpha + s);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
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
            if (iconSprite != null) UnityEngine.Object.Destroy(iconSprite);
            if (iconTexture != null) UnityEngine.Object.Destroy(iconTexture);
            iconSprite = null;
            iconTexture = null;
        }
    }
}
