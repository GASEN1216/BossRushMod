// ============================================================================
// AchievementTriggers.cs - 成就触发逻辑集成
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 成就触发逻辑 - 集成到 ModBehaviour 的 partial class
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 私有字段

        private bool achievementSystemInitialized = false;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化成就系统
        /// </summary>
        private void InitializeAchievementSystem()
        {
            if (achievementSystemInitialized) return;

            try
            {
                BossRushAchievementManager.Initialize();
                SteamAchievementPopup.EnsureInstance();
                SubscribeAchievementEvents();
                achievementSystemInitialized = true;
                DevLog("[Achievement] 成就系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 成就系统初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 重置本局成就追踪状态
        /// </summary>
        private void ResetAchievementTracking()
        {
            if (!achievementSystemInitialized) return;
            try { AchievementTracker.ResetSessionStats(); }
            catch { }
        }

        #endregion

        #region 通关成就

        /// <summary>
        /// 检查通关相关成就
        /// </summary>
        private void CheckClearAchievements()
        {
            if (!achievementSystemInitialized) return;

            try
            {
                AchievementTracker.OnClear();
                BossRushAchievementManager.TryUnlock("first_clear");

                // 根据难度触发对应成就
                if (bossesPerWave <= 1)
                {
                    BossRushAchievementManager.TryUnlock("easy_clear");
                    if (!AchievementTracker.HasTakenDamage)
                        BossRushAchievementManager.TryUnlock("flawless_easy");
                }
                else
                {
                    BossRushAchievementManager.TryUnlock("normal_clear");
                    if (!AchievementTracker.HasTakenDamage)
                        BossRushAchievementManager.TryUnlock("flawless_normal");
                }

                // 速通成就
                float elapsedTime = AchievementTracker.GetElapsedTime();
                if (elapsedTime <= 60f)
                {
                    BossRushAchievementManager.TryUnlock("speedrun_1min");
                    BossRushAchievementManager.TryUnlock("speedrun_2min");
                    BossRushAchievementManager.TryUnlock("speedrun_3min");
                    BossRushAchievementManager.TryUnlock("speedrun_5min");
                }
                else if (elapsedTime <= 120f)
                {
                    BossRushAchievementManager.TryUnlock("speedrun_2min");
                    BossRushAchievementManager.TryUnlock("speedrun_3min");
                    BossRushAchievementManager.TryUnlock("speedrun_5min");
                }
                else if (elapsedTime <= 180f)
                {
                    BossRushAchievementManager.TryUnlock("speedrun_3min");
                    BossRushAchievementManager.TryUnlock("speedrun_5min");
                }
                else if (elapsedTime <= 300f)
                {
                    BossRushAchievementManager.TryUnlock("speedrun_5min");
                }

                // 累计通关成就
                if (AchievementTracker.TotalClears >= 10)
                    BossRushAchievementManager.TryUnlock("clear_10_times");
                if (AchievementTracker.TotalClears >= 50)
                    BossRushAchievementManager.TryUnlock("clear_50_times");
                if (AchievementTracker.TotalClears >= 100)
                    BossRushAchievementManager.TryUnlock("clear_100_times");

                // 检查成就收集者成就
                CheckCompletionistAchievement();
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 通关成就检查失败: " + e.Message);
            }
        }

        #endregion

        #region 无间炼狱成就

        /// <summary>
        /// 检查无间炼狱波次成就
        /// </summary>
        private void CheckInfiniteHellAchievements(int waveNumber)
        {
            if (!achievementSystemInitialized) return;

            try
            {
                // 记录最高波次
                AchievementTracker.OnInfiniteHellWaveComplete(waveNumber);

                if (waveNumber >= 10)
                {
                    BossRushAchievementManager.TryUnlock("hell_10");
                    if (!AchievementTracker.HasUsedHealItem)
                        BossRushAchievementManager.TryUnlock("hell_no_heal");
                    if (!AchievementTracker.HasTakenDamage)
                        BossRushAchievementManager.TryUnlock("flawless_hell_10");
                }

                if (waveNumber >= 25)
                    BossRushAchievementManager.TryUnlock("hell_25");

                if (waveNumber >= 50)
                    BossRushAchievementManager.TryUnlock("hell_50");

                if (waveNumber >= 100)
                    BossRushAchievementManager.TryUnlock("hell_100");

                if (waveNumber >= 200)
                    BossRushAchievementManager.TryUnlock("hell_200");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 无间炼狱成就检查失败: " + e.Message);
            }
        }

        #endregion

        #region Mode D 成就

        /// <summary>
        /// 检查 Mode D 通关成就
        /// </summary>
        private void CheckModeDClearAchievements()
        {
            if (!achievementSystemInitialized) return;

            try
            {
                BossRushAchievementManager.TryUnlock("mode_d_clear");
                if (!AchievementTracker.HasPickedUpItem)
                    BossRushAchievementManager.TryUnlock("mode_d_no_pickup");
                if (!AchievementTracker.HasTakenDamage)
                    BossRushAchievementManager.TryUnlock("flawless_mode_d");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] Mode D 成就检查失败: " + e.Message);
            }
        }

        #endregion

        #region Boss 击杀成就

        /// <summary>
        /// 检查 Boss 击杀成就
        /// </summary>
        private void CheckBossKillAchievements(string bossType)
        {
            if (!achievementSystemInitialized) return;

            try
            {
                AchievementTracker.OnBossKilled(bossType);

                if (bossType == "DragonDescendant")
                {
                    BossRushAchievementManager.TryUnlock("kill_dragon_descendant");
                    // 龙裔遗族无伤击杀（检查本局是否受伤）
                    if (!AchievementTracker.HasTakenDamage)
                        BossRushAchievementManager.TryUnlock("kill_dragon_descendant_flawless");
                }
                else if (bossType == "DragonKing")
                {
                    BossRushAchievementManager.TryUnlock("kill_dragon_king");
                    if (AchievementTracker.DragonKingKilledFlawless)
                        BossRushAchievementManager.TryUnlock("kill_dragon_king_flawless");
                    // 累计龙王击杀成就
                    if (AchievementTracker.TotalDragonKingKills >= 10)
                        BossRushAchievementManager.TryUnlock("dragon_slayer_master");
                }

                // 累计击杀成就
                if (AchievementTracker.TotalBossKills >= 50)
                    BossRushAchievementManager.TryUnlock("kill_50_bosses");
                if (AchievementTracker.TotalBossKills >= 100)
                    BossRushAchievementManager.TryUnlock("kill_100_bosses");
                if (AchievementTracker.TotalBossKills >= 500)
                    BossRushAchievementManager.TryUnlock("kill_500_bosses");
                if (AchievementTracker.TotalBossKills >= 1000)
                    BossRushAchievementManager.TryUnlock("kill_1000_bosses");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] Boss击杀成就检查失败: " + e.Message);
            }
        }

        /// <summary>
        /// 识别 Boss 类型
        /// </summary>
        private string IdentifyBossType(CharacterMainControl bossMain)
        {
            if (bossMain == null) return "Normal";

            try
            {
                string bossName = bossMain.name.ToLower();
                if (bossName.Contains("dragondescendant") || bossName.Contains("dragon_descendant") || bossName.Contains("龙裔"))
                    return "DragonDescendant";
                if (bossName.Contains("dragonking") || bossName.Contains("dragon_king") || bossName.Contains("龙王") || bossName.Contains("焚天"))
                    return "DragonKing";
            }
            catch { }

            return "Normal";
        }

        #endregion

        #region 玩家状态追踪

        /// <summary>
        /// 记录玩家受伤
        /// </summary>
        private void TrackPlayerDamage(float damage)
        {
            if (!achievementSystemInitialized) return;
            try { AchievementTracker.OnPlayerTakeDamage(damage); }
            catch { }
        }

        /// <summary>
        /// 记录玩家拾取物品
        /// </summary>
        private void TrackPlayerPickup()
        {
            if (!achievementSystemInitialized) return;
            try { AchievementTracker.OnPlayerPickupItem(); }
            catch { }
        }

        /// <summary>
        /// 记录玩家使用治疗物品
        /// </summary>
        private void TrackPlayerHeal()
        {
            if (!achievementSystemInitialized) return;
            try { AchievementTracker.OnPlayerUseHealItem(); }
            catch { }
        }

        #endregion

        #region 事件回调

        /// <summary>
        /// 玩家受伤事件回调（用于无伤成就追踪）
        /// 订阅于 Health.OnHurt 静态事件
        /// </summary>
        private void OnPlayerHurtForAchievement(Health health, DamageInfo damageInfo)
        {
            // 仅在 BossRush 激活时追踪
            if (!IsActive && !bossRushArenaActive) return;
            if (!achievementSystemInitialized) return;

            try
            {
                // 检查是否是玩家受伤
                if (health == null) return;
                
                // 获取玩家 Health 组件进行比对
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health != health) return;

                // 记录玩家受伤（用于无伤成就判定）
                // DamageInfo 是 struct，使用 finalDamage 字段获取实际伤害值
                float damage = damageInfo.finalDamage;
                if (damage > 0)
                {
                    TrackPlayerDamage(damage);
                    DevLog("[Achievement] 玩家受伤: " + damage + ", HasTakenDamage=" + AchievementTracker.HasTakenDamage);
                }
            }
            catch { }
        }

        /// <summary>
        /// 物品拾取事件回调（用于 Mode D 无拾取成就追踪）
        /// 订阅于 InteractablePickup.OnPickupSuccess 静态事件
        /// </summary>
        private void OnItemPickupForAchievement(InteractablePickup pickup, CharacterMainControl character)
        {
            // 仅在 Mode D 激活时追踪
            if (!modeDActive) return;
            if (!achievementSystemInitialized) return;

            try
            {
                // 检查是否是玩家拾取
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || character != player) return;

                // 记录玩家拾取物品
                TrackPlayerPickup();
                DevLog("[Achievement] 玩家拾取物品, HasPickedUpItem=" + AchievementTracker.HasPickedUpItem);
            }
            catch { }
        }

        // 用于追踪玩家血量变化（检测治疗）
        private float lastPlayerHealth = -1f;

        /// <summary>
        /// 玩家血量变化事件回调（用于无间炼狱无治疗成就追踪）
        /// 订阅于玩家 Health.OnHealthChange 实例事件
        /// </summary>
        private void OnPlayerHealthChangeForAchievement(Health health)
        {
            // 仅在无间炼狱模式激活时追踪
            if (!infiniteHellMode) return;
            if (!achievementSystemInitialized) return;

            try
            {
                if (health == null) return;
                
                float currentHealth = health.CurrentHealth;
                
                // 如果是首次记录，只保存当前血量
                if (lastPlayerHealth < 0)
                {
                    lastPlayerHealth = currentHealth;
                    return;
                }

                // 检测血量增加（治疗）
                if (currentHealth > lastPlayerHealth)
                {
                    float healAmount = currentHealth - lastPlayerHealth;
                    TrackPlayerHeal();
                    DevLog("[Achievement] 玩家治疗: +" + healAmount + ", HasUsedHealItem=" + AchievementTracker.HasUsedHealItem);
                }

                lastPlayerHealth = currentHealth;
            }
            catch { }
        }

        /// <summary>
        /// 检查成就收集者成就（解锁所有非隐藏成就）
        /// </summary>
        private void CheckCompletionistAchievement()
        {
            if (!achievementSystemInitialized) return;

            try
            {
                var allAchievements = BossRushAchievementManager.GetAllAchievements();
                int nonHiddenCount = 0;
                int unlockedNonHiddenCount = 0;

                foreach (var achievement in allAchievements)
                {
                    // 跳过隐藏成就和成就收集者本身
                    if (achievement.isHidden || achievement.id == "completionist")
                        continue;

                    nonHiddenCount++;
                    if (BossRushAchievementManager.IsUnlocked(achievement.id))
                        unlockedNonHiddenCount++;
                }

                // 如果解锁了所有非隐藏成就（不包括成就收集者本身）
                if (unlockedNonHiddenCount >= nonHiddenCount && nonHiddenCount > 0)
                {
                    BossRushAchievementManager.TryUnlock("completionist");
                }
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 检查成就收集者失败: " + e.Message);
            }
        }

        /// <summary>
        /// 订阅成就追踪事件
        /// </summary>
        private void SubscribeAchievementEvents()
        {
            try
            {
                // 订阅物品拾取事件（Mode D 无拾取成就）
                InteractablePickup.OnPickupSuccess += OnItemPickupForAchievement;
                DevLog("[Achievement] 已订阅物品拾取事件");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 订阅物品拾取事件失败: " + e.Message);
            }

            try
            {
                // 订阅玩家血量变化事件（无间炼狱无治疗成就）
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    player.Health.OnHealthChange.AddListener(OnPlayerHealthChangeForAchievement);
                    lastPlayerHealth = player.Health.CurrentHealth;
                    DevLog("[Achievement] 已订阅玩家血量变化事件");
                }
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 订阅玩家血量变化事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消订阅成就追踪事件
        /// </summary>
        private void UnsubscribeAchievementEvents()
        {
            try
            {
                InteractablePickup.OnPickupSuccess -= OnItemPickupForAchievement;
            }
            catch { }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    player.Health.OnHealthChange.RemoveListener(OnPlayerHealthChangeForAchievement);
                }
            }
            catch { }

            lastPlayerHealth = -1f;
        }

        #endregion
    }
}
