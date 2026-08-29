// ============================================================================
// AchievementTriggers.cs - 成就触发逻辑集成
// ============================================================================

using System;
using System.Collections.Generic;
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
        private readonly HashSet<CharacterMainControl> achievementCountedBossKills = new HashSet<CharacterMainControl>();

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
            try
            {
                AchievementTracker.ResetSessionStats();
                ResetAchievementBossKillTracking();
            }
            catch { }
        }

        /// <summary>
        /// 在真正开始一局挑战时重置会话状态。
        /// </summary>
        private void BeginAchievementSession(string sessionName)
        {
            if (!achievementSystemInitialized) return;

            try
            {
                ResetAchievementTracking();

                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    lastPlayerHealth = player.Health.CurrentHealth;
                }
                else
                {
                    lastPlayerHealth = -1f;
                }

                DevLog("[Achievement] 成就会话开始: " + sessionName + ", startTime=" + AchievementTracker.ArenaEnterTime);
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 成就会话初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 按当前会话耗时解锁速通成就。
        /// </summary>
        private void TryUnlockSpeedrunAchievements(string source)
        {
            if (!achievementSystemInitialized) return;

            try
            {
                float elapsedTime = AchievementTracker.GetElapsedTime();
                DevLog("[Achievement] 检查速通成就: source=" + source + ", elapsed=" + elapsedTime);

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
            }
            catch (Exception e)
            {
                DevLog("[Achievement] 速通成就检查失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查 Mode D 无伤成就。
        /// </summary>
        private void CheckModeDFlawlessAchievement()
        {
            if (!achievementSystemInitialized) return;

            try
            {
                if (!AchievementTracker.HasTakenDamage)
                    BossRushAchievementManager.TryUnlock("flawless_mode_d");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] Mode D 无伤成就检查失败: " + e.Message);
            }
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
                TryUnlockSpeedrunAchievements("BossRushClear");

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
                    TryUnlockSpeedrunAchievements("InfiniteHellWave10");
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
                TryUnlockSpeedrunAchievements("ModeDClear");
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
                    // 龙王无伤击杀（与龙裔遗族使用相同的判定逻辑）
                    if (!AchievementTracker.HasTakenDamage)
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
        /// 按角色实例去重的 Boss 击杀成就入口。
        /// </summary>
        private bool CheckBossKillAchievementsOnce(CharacterMainControl bossMain, string bossTypeOverride = null)
        {
            if (!achievementSystemInitialized) return false;

            try
            {
                if (bossMain == null)
                    return false;

                if (achievementCountedBossKills.Contains(bossMain))
                    return false;

                achievementCountedBossKills.Add(bossMain);

                string bossType = string.IsNullOrEmpty(bossTypeOverride)
                    ? IdentifyBossType(bossMain)
                    : bossTypeOverride;

                CheckBossKillAchievements(bossType);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[Achievement] Boss击杀成就去重检查失败: " + e.Message);
                return false;
            }
        }

        private void ResetAchievementBossKillTracking()
        {
            achievementCountedBossKills.Clear();
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

        // ===== Mode G 独立成就 session（加法分支）=====
        // Mode G 不复用 Legacy IsActive 语义，按 (token, bossType, wasFlawlessAtDeath)
        // 纯数据窄去重上报；Legacy 路径不受影响。

        /// <summary>
        /// Mode G 成就上报去重键：纯数据三元组，不持有任何游戏对象引用。
        /// </summary>
        private struct ModeGAchievementReportKey : System.IEquatable<ModeGAchievementReportKey>
        {
            public int token;
            public string bossType;
            public bool wasFlawlessAtDeath;

            public ModeGAchievementReportKey(int token, string bossType, bool wasFlawlessAtDeath)
            {
                this.token = token;
                this.bossType = bossType ?? string.Empty;
                this.wasFlawlessAtDeath = wasFlawlessAtDeath;
            }

            public bool Equals(ModeGAchievementReportKey other)
            {
                return token == other.token
                    && string.Equals(bossType, other.bossType, System.StringComparison.Ordinal)
                    && wasFlawlessAtDeath == other.wasFlawlessAtDeath;
            }

            public override bool Equals(object obj)
            {
                return obj is ModeGAchievementReportKey && Equals((ModeGAchievementReportKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + token;
                    hash = hash * 31 + (bossType != null ? bossType.GetHashCode() : 0);
                    hash = hash * 31 + (wasFlawlessAtDeath ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly System.Collections.Generic.HashSet<ModeGAchievementReportKey> modeGCountedAchievementReports
            = new System.Collections.Generic.HashSet<ModeGAchievementReportKey>();
        private bool modeGAchievementSessionActive = false;

        /// <summary>
        /// Mode G 开局时调用：重置本模式 flawless 判定基准、开启独立 session 并清空去重集。
        /// </summary>
        internal void BeginModeGAchievementSession()
        {
            try
            {
                // flawless 基准归零：HasTakenDamage 是进程级静态，Mode G 启动路径不经过
                // BeginAchievementSession，跨局残留会让「同进程受过一次伤」之后的真无伤
                // 击杀永远解锁不了 kill_dragon_king_flawless / kill_dragon_descendant_flawless。
                // 这里刻意不调 AchievementTracker.ResetSessionStats()：它还会重置
                // ArenaEnterTime（Legacy/Mode D 速通基准）、HasUsedHealItem（无间炼狱
                // hell_no_heal）、HasPickedUpItem（Mode D 无拾取），这三项 Mode G 一个都不
                // 消费，整体 reset 只会污染 Legacy 会话语义。Mode G flawless 真正依赖的
                // 只有 HasTakenDamage（消费点：ModeGRuntimeModule 的 wasFlawlessAtDeath 快照）。
                AchievementTracker.HasTakenDamage = false;
                modeGCountedAchievementReports.Clear();
                modeGAchievementSessionActive = true;
                DevLog("[Achievement] [ModeG] 独立成就 session 已开启");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] [ModeG] session 开启失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode G 结束时调用：关闭 session。去重集保留至下次开局再清，
        /// 防止结束后的延迟击杀回调重复上报。
        /// </summary>
        internal void EndModeGAchievementSession()
        {
            try
            {
                modeGAchievementSessionActive = false;
                DevLog("[Achievement] [ModeG] 独立成就 session 已关闭");
            }
            catch (Exception e)
            {
                DevLog("[Achievement] [ModeG] session 关闭失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode G Boss 击杀成就上报入口（纯数据参数，不依赖 CharacterMainControl 引用）。
        /// 窄去重：(token, bossType, wasFlawlessAtDeath) 三元组每 session 仅计一次。
        /// wasFlawlessAtDeath 使用传入值（Mode G 死亡时刻快照），不重读 HasTakenDamage。
        /// </summary>
        internal void ReportModeGBossKillAchievement(int token, string bossType, bool wasFlawlessAtDeath)
        {
            if (!modeGAchievementSessionActive) return;
            if (!achievementSystemInitialized) return;

            try
            {
                ModeGAchievementReportKey key = new ModeGAchievementReportKey(token, bossType, wasFlawlessAtDeath);
                if (!modeGCountedAchievementReports.Add(key))
                    return;

                // 复用现有统计与解锁逻辑（OnBossKilled 负责 TotalBossKills/TotalDragonKingKills 累计）
                CheckBossKillAchievements(string.IsNullOrEmpty(bossType) ? "Normal" : bossType);

                // Mode G 无伤成就：以传入的死亡时刻快照为准
                if (wasFlawlessAtDeath)
                {
                    if (bossType == "DragonDescendant")
                        BossRushAchievementManager.TryUnlock("kill_dragon_descendant_flawless");
                    else if (bossType == "DragonKing")
                        BossRushAchievementManager.TryUnlock("kill_dragon_king_flawless");
                }

                DevLog("[Achievement] [ModeG] Boss击杀上报: token=" + token + ", type=" + bossType + ", flawless=" + wasFlawlessAtDeath);
            }
            catch (Exception e)
            {
                DevLog("[Achievement] [ModeG] Boss击杀成就上报失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode G 成就伤害窗口查询（no-throw，异常返回 false 保 Legacy 行为）。
        /// 窗口语义由 ModeGRuntimeGates 保证：仅 Active + Fighting/LastStand 为 true。
        /// </summary>
        private static bool IsModeGAchievementDamageWindowActiveSafe()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGAchievementDamageWindowActive;
            }
            catch
            {
                return false;
            }
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
            // 仅在 BossRush 激活时追踪；Mode G 门控（加法分支）：
            // Mode G 成就伤害窗口（仅 Active+Fighting/LastStand，默认 false）同样纳入追踪。
            // 不写 IsActive 本身；窗口查询 no-throw。
            if (!IsActive && !IsModeGAchievementDamageWindowActiveSafe()) return;
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
        /// 检查成就收集者成就（解锁所有成就）
        /// </summary>
        private void CheckCompletionistAchievement()
        {
            if (!achievementSystemInitialized) return;

            try
            {
                BossRushAchievementManager.CheckCompletionistAchievement();
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
