// ============================================================================
// ModeEIntegrityAndHelpers.cs - Mode E integrity checks and shared helpers
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 自检机制

        /// <summary>
        /// Mode E 存活敌人列表自检：清理已死亡/已销毁的敌人引用，补偿丢失的死亡事件
        /// <para>防止敌人死亡事件丢失（瞬杀、事件触发时机等）导致列表残留和缩放计算不准确</para>
        /// </summary>
        private void ModeEIntegrityCheck()
        {
            try
            {
                if (!modeEActive) return;

                int removedCount = 0;
                for (int i = modeEAliveEnemies.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = modeEAliveEnemies[i];
                    if (object.ReferenceEquals(enemy, null))
                    {
                        modeEAliveEnemies.RemoveAt(i);
                        MarkModeEBossRegenCacheDirty();
                        removedCount++;
                        continue;
                    }

                    if (enemy == null || enemy.gameObject == null || enemy.Health == null || enemy.Health.IsDead)
                    {
                        // 从虚拟 SpawnerRoot 移除
                        UnregisterModeEEnemyFromSpawnerRoot(enemy);

                        // 补偿丢失的死亡事件：递增该阵营死亡计数
                        // [修复] 当 enemy.Team 访问失败时，从所有阵营列表中暴力移除，防止残留无效引用
                    try
                    {
                        if (!(enemy == null))
                        {
                            Teams faction = enemy.Team;
                            if (modeEFactionDeathCount.ContainsKey(faction))
                            {
                                modeEFactionDeathCount[faction]++;
                            }
                            CleanupModeEEnemyRuntimeState(enemy, faction);
                        }
                        else
                        {
                            CleanupModeEEnemyRuntimeState(enemy);
                        }
                    }
                    catch (Exception e)
                    {
                        // Unity 已销毁对象，无法读取 Team —— 从所有阵营列表中暴力移除
                        DevLog("[ModeE] [WARNING] 自检时读取敌人阵营失败，改为全量清理: index=" + i + ", " + e.Message);
                        CleanupModeEEnemyRuntimeState(enemy);
                        }

                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    DevLog("[ModeE] 自检清理了 " + removedCount + " 个已死亡/已销毁的敌人引用");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEIntegrityCheck 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 辅助方法

        private void MarkModeEBossRegenCacheDirty()
        {
            modeEBossRegenCacheDirty = true;
        }

        private void ClearModeEBossRegenCache()
        {
            modeEBossRegenCache.Clear();
            modeEBossRegenCacheDirty = false;
        }

        private List<MonoBehaviour> GetModeEBossRegenCache()
        {
            if (!modeEBossRegenCacheDirty)
            {
                return modeEBossRegenCache;
            }

            modeEBossRegenCache.Clear();
            for (int i = 0; i < modeEAliveEnemies.Count; i++)
            {
                CharacterMainControl boss = modeEAliveEnemies[i];
                if (boss != null)
                {
                    modeEBossRegenCache.Add(boss);
                }
            }

            modeEBossRegenCacheDirty = false;
            return modeEBossRegenCache;
        }

        /// <summary>
        /// 零度挑战地图专用：发放保暖装备（头盔 + 护甲）
        /// 仅在 Level_ChallengeSnow 场景下生效，硬编码物品ID
        /// </summary>
        private void ModeEGiveColdWeatherGear()
        {
            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // 零度挑战地图和37号实验区都需要发放保暖装备
                if (currentScene != "Level_ChallengeSnow" && currentScene != "Level_SnowMilitaryBase") return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 零度挑战地图：发放保暖装备...");

                // 头盔 ID:1312
                Item helmet = ItemAssetsCollection.InstantiateSync(1312);
                if (helmet != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(helmet, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(helmet, false);
                    DevLog("[ModeE] 发放保暖头盔: " + helmet.DisplayName);
                }

                // 护甲 ID:1307
                Item armor = ItemAssetsCollection.InstantiateSync(1307);
                if (armor != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(armor, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(armor, false);
                    DevLog("[ModeE] 发放保暖护甲: " + armor.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveColdWeatherGear 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 独狼阵营专属补给：发放3个id=881和3个id=660的物品到玩家背包
        /// </summary>
        private void ModeEGiveLoneWolfSupplies()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 独狼阵营：发放专属补给物品...");

                // 发放3个 id=881 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item881 = ItemAssetsCollection.InstantiateSync(881);
                    if (item881 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item881, false);
                        DevLog("[ModeE] 独狼补给：发放物品 881 - " + item881.DisplayName);
                    }
                }

                // 发放3个 id=660 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item660 = ItemAssetsCollection.InstantiateSync(660);
                    if (item660 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item660, false);
                        DevLog("[ModeE] 独狼补给：发放物品 660 - " + item660.DisplayName);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveLoneWolfSupplies 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取阵营的中文显示名称
        /// </summary>
        private string GetFactionDisplayName(Teams faction)
        {
            switch (faction)
            {
                case Teams.scav:    return L10n.T("拾荒者", "Scav");
                case Teams.usec:    return L10n.T("USEC", "USEC");
                case Teams.bear:    return L10n.T("BEAR", "BEAR");
                case Teams.lab:     return L10n.T("实验室", "Lab");
                case Teams.wolf:    return L10n.T("狼群", "Wolf");
                case Teams.player:  return L10n.T("独狼", "Lone Wolf");
                default:            return string.Empty;   // 未知阵营不念枚举名（UB-30）
            }
        }

        // 阵营后缀的字色：每个阵营一个 token 色（审美审查 UB-30），类型初始化时算一次；自己人（独狼）用成功色。
        private static readonly string ModeEFactionTagNeutral = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">";
        private static readonly string ModeEFactionTagUsec = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.RarityRare) + ">";
        private static readonly string ModeEFactionTagBear = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";
        private static readonly string ModeEFactionTagLab = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.RarityEpic) + ">";
        private static readonly string ModeEFactionTagWolf = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        private static readonly string ModeEFactionTagPlayer = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText) + ">";

        private static string GetModeEFactionTag(Teams faction)
        {
            switch (faction)
            {
                case Teams.usec:    return ModeEFactionTagUsec;
                case Teams.bear:    return ModeEFactionTagBear;
                case Teams.lab:     return ModeEFactionTagLab;
                case Teams.wolf:    return ModeEFactionTagWolf;
                case Teams.player:  return ModeEFactionTagPlayer;
                default:            return ModeEFactionTagNeutral;
            }
        }

        /// <summary>
        /// 获取阵营后缀字符串（供 Harmony 补丁在 HealthBar 名字后追加）
        /// 格式：" · 阵营名"，按阵营着 token 色（旧版是纯文本「 - 阵营名」，不同阵营看起来一样，UB-30）；
        /// 非 Mode E 阵营返回 null。剥离旧后缀（StripModeEFactionSuffix）按同一个字符串比对，两边永远一致。
        /// </summary>
        public string GetModeEFactionSuffix(Teams faction)
        {
            string name = GetFactionDisplayName(faction);
            if (string.IsNullOrEmpty(name)) return null;
            return " " + GetModeEFactionTag(faction) + "· " + name + "</color>";
        }

        /// <summary>
        /// 贝壳商店购买键底色：还原官方商店自己的「可买 / 不可买」两色（StockShopView.buttonColor_Interactable /
        /// _NotInteractable，官方 RefreshInteractionButton 也写这两个）。旧版刷成 (0.2,0.8,0.35) 荧光绿 / Color.gray，
        /// 在暖色调的官方商店里扎眼（2026-09-23 审美审查 UB-21）。两个都是官方私有序列化字段（WIRE+，经 ReflectionCache 缓存）：
        /// 读不到时退回 token 主操作色 / 禁用色，不影响交易，也不进壳层 fail-closed 的契约清单。
        /// </summary>
        private static Color GetModeEShellOfficialButtonColor(global::Duckov.Economy.UI.StockShopView view, bool interactable)
        {
            try
            {
                FieldInfo field = BossRush.Common.Utils.ReflectionCache.GetField(typeof(global::Duckov.Economy.UI.StockShopView),
                    interactable ? "buttonColor_Interactable" : "buttonColor_NotInteractable",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (view != null && field != null && field.FieldType == typeof(Color))
                {
                    return (Color)field.GetValue(view);
                }
            }
            catch (Exception)
            {
                // 官方字段改名 / 类型变了：走下面的 token 回退
            }
            return interactable ? BossRushUIColors.AccentFill : BossRushUIColors.Disabled;
        }

        private void ClearModeEHealthBarOverrideCache(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            int barId = healthBar.GetInstanceID();
            modeEHealthBarBaseTextByBarId.Remove(barId);
            modeEHealthBarDesiredTextByBarId.Remove(barId);
            modeEHealthBarTargetIdsByBarId.Remove(barId);
            modeEHealthBarAppliedVersionByBarId.Remove(barId);
        }

        private string BuildModeEDesiredHealthBarText(
            CharacterMainControl character,
            TextMeshProUGUI nameText,
            int barId,
            int targetId,
            bool forceShowName)
        {
            if (character == null)
            {
                return null;
            }

            string baseText = null;
            if (forceShowName)
            {
                baseText = GetModeEPlayerName();
            }
            else
            {
                int cachedTargetId;
                bool needsBaseRefresh =
                    !modeEHealthBarBaseTextByBarId.TryGetValue(barId, out baseText) ||
                    string.IsNullOrEmpty(baseText) ||
                    !modeEHealthBarTargetIdsByBarId.TryGetValue(barId, out cachedTargetId) ||
                    cachedTargetId != targetId;

                if (needsBaseRefresh)
                {
                    baseText = nameText != null ? StripModeEFactionSuffix(nameText.text) : null;
                    if (string.IsNullOrEmpty(baseText))
                    {
                        baseText = GetModeEActorDisplayName(character);
                    }

                    if (!string.IsNullOrEmpty(baseText))
                    {
                        modeEHealthBarBaseTextByBarId[barId] = baseText;
                    }
                }
            }

            if (string.IsNullOrEmpty(baseText))
            {
                return null;
            }

            Teams displayFaction = forceShowName ? ModeEPlayerFaction : character.Team;
            string factionSuffix = GetModeEFactionSuffix(displayFaction);
            return string.IsNullOrEmpty(factionSuffix) ? baseText : baseText + factionSuffix;
        }

        /// <summary>
        /// 在 HealthBar 名字后追加阵营后缀，供统一的 HealthBar patch 调用。
        /// </summary>
        internal void ApplyModeEHealthBarNameOverride(HealthBar healthBar, TextMeshProUGUI nameText)
        {
            if (healthBar == null || nameText == null) return;

            RegisterModeEHealthBar(healthBar);

            Health target = healthBar.target;
            if (target == null)
            {
                ClearModeEHealthBarOverrideCache(healthBar);
                return;
            }

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null)
            {
                ClearModeEHealthBarOverrideCache(healthBar);
                return;
            }

            bool forceShowName = character.IsMainCharacter;
            if (!forceShowName && !nameText.gameObject.activeSelf) return;

            SyncModeEHealthBarNameLanguageState();

            int barId = healthBar.GetInstanceID();
            int targetId = target.GetInstanceID();
            string desiredText = null;
            int appliedVersion = 0;
            int cachedTargetId = 0;
            bool targetChanged =
                !modeEHealthBarTargetIdsByBarId.TryGetValue(barId, out cachedTargetId) ||
                cachedTargetId != targetId;
            if (targetChanged)
            {
                modeEHealthBarBaseTextByBarId.Remove(barId);
                modeEHealthBarDesiredTextByBarId.Remove(barId);
                modeEHealthBarAppliedVersionByBarId.Remove(barId);
            }

            bool needsRebuild =
                forceShowName ||
                targetChanged ||
                !modeEHealthBarDesiredTextByBarId.TryGetValue(barId, out desiredText) ||
                string.IsNullOrEmpty(desiredText) ||
                !modeEHealthBarAppliedVersionByBarId.TryGetValue(barId, out appliedVersion) ||
                appliedVersion != modeEHealthBarNameVersion;

            if (needsRebuild)
            {
                desiredText = BuildModeEDesiredHealthBarText(character, nameText, barId, targetId, forceShowName);
                if (string.IsNullOrEmpty(desiredText))
                {
                    ClearModeEHealthBarOverrideCache(healthBar);
                    return;
                }

                modeEHealthBarDesiredTextByBarId[barId] = desiredText;
                modeEHealthBarAppliedVersionByBarId[barId] = modeEHealthBarNameVersion;
                modeEHealthBarTargetIdsByBarId[barId] = targetId;
            }

            if (forceShowName && !nameText.gameObject.activeSelf)
            {
                nameText.gameObject.SetActive(true);
            }

            if (!string.Equals(nameText.text, desiredText, StringComparison.Ordinal))
            {
                nameText.text = desiredText;
            }
        }

        private string StripModeEFactionSuffix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string sanitized = text;
            while (TryTrimTrailingModeEFactionSuffix(ref sanitized))
            {
            }
            return sanitized;
        }

        private bool TryTrimTrailingModeEFactionSuffix(ref string text)
        {
            return TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.scav)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.usec)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.bear)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.lab)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.wolf)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.player));
        }

        private static bool TryTrimOneModeEFactionSuffix(ref string text, string suffix)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(suffix)) return false;
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) return false;
            text = text.Substring(0, text.Length - suffix.Length);
            return true;
        }

        #region Mode E 阵营存活列表管理（P4性能优化）

        /// <summary>
        /// 将敌人添加到阵营独立存活列表
        /// </summary>
        private void AddToFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (!modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                list = new List<CharacterMainControl>(8);
                modeEFactionAliveMap[faction] = list;
            }

            list.Add(enemy);
        }

        /// <summary>
        /// 从阵营独立存活列表中移除敌人
        /// </summary>
        private void RemoveFromFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (object.ReferenceEquals(list[i], enemy))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 将敌人登记为 Mode E 运行时存活对象，避免重复加入全局/阵营列表。
        /// </summary>
        private void TrackModeEAliveEnemy(CharacterMainControl enemy, Teams faction)
        {
            if (enemy == null)
            {
                return;
            }

            if (!modeEAliveEnemySet.Add(enemy))
            {
                return;
            }

            modeEAliveEnemies.Add(enemy);
            modeEAliveEnemyFactionMap[enemy] = faction;
            AddToFactionAliveList(faction, enemy);
            MarkModeEBossRegenCacheDirty();
        }

        /// <summary>
        /// 从 Mode E 运行时存活对象登记中移除敌人。
        /// </summary>
        private void UntrackModeEAliveEnemy(CharacterMainControl enemy, Teams? faction = null)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            bool removedFromSet = modeEAliveEnemySet.Remove(enemy);
            bool removedFromList = modeEAliveEnemies.Remove(enemy);
            if (removedFromSet || removedFromList)
            {
                MarkModeEBossRegenCacheDirty();
            }

            Teams trackedFaction;
            if (!faction.HasValue && modeEAliveEnemyFactionMap.TryGetValue(enemy, out trackedFaction))
            {
                faction = trackedFaction;
            }

            modeEAliveEnemyFactionMap.Remove(enemy);

            if (faction.HasValue)
            {
                RemoveFromFactionAliveList(faction.Value, enemy);
                return;
            }

            foreach (KeyValuePair<Teams, List<CharacterMainControl>> kvp in modeEFactionAliveMap)
            {
                RemoveFromFactionAliveList(kvp.Key, enemy);
            }
        }

        /// <summary>
        /// 获取指定阵营的存活敌人列表（只读访问，用于缩放遍历）
        /// </summary>
        private List<CharacterMainControl> GetFactionAliveList(Teams faction)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                return list;
            }
            return null;
        }

        #endregion

        #endregion
    }
}
