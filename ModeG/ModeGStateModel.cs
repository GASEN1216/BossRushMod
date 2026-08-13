using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 生命周期阶段。方案文档 §4（2026-08-10 裁决版）。
    /// None -> Starting -> Active -> Rewarding -> Exiting -> None
    /// 脚手架 Preview 阶段已按 C1/§12 裁决移除：preview 冻结由不可变 ModeGEntryPreview
    /// 表达，不占用生命周期槽位（IsModeGRunInProgress 因此严格等于 LifecyclePhase != None）。
    /// </summary>
    public enum ModeGLifecyclePhase
    {
        /// <summary>未激活</summary>
        None,
        /// <summary>RunState 已创建，正在执行预检/资源加载/首波生成</summary>
        Starting,
        /// <summary>战斗/休整循环中（含 Spawning/Fighting/Intermission/LastStand/WaveSettling）</summary>
        Active,
        /// <summary>第 9 波胜利后，奖励事务执行中</summary>
        Rewarding,
        /// <summary>统一退出中，等待 spawn drain 或完整移交 sink</summary>
        Exiting
    }

    /// <summary>
    /// Mode G 战斗阶段。方案文档 §4。
    /// 含 Fighting/LastStand/Victory/Defeat；Victory/Defeat 为终局战斗相位，
    /// 只能由 battleResultToken CAS 成功后写入。
    /// </summary>
    public enum ModeGCombatPhase
    {
        /// <summary>非战斗状态</summary>
        None,
        /// <summary>正在生成 Boss</summary>
        Spawning,
        /// <summary>战斗中</summary>
        Fighting,
        /// <summary>休整倒计时</summary>
        Intermission,
        /// <summary>最后一名 Boss 倒计时处决（12 秒，owner tunable）</summary>
        LastStand,
        /// <summary>波次结算中（冻结结果、安排下一波）</summary>
        WaveSettling,
        /// <summary>胜利终局相位（第 9 波锁定 Victory 后）</summary>
        Victory,
        /// <summary>失败终局相位（玩家死亡锁定 Defeat 后）</summary>
        Defeat
    }

    /// <summary>
    /// Mode G 战斗结果 token。只允许 Victory/Defeat CAS 一次。
    /// </summary>
    public enum ModeGBattleResult
    {
        /// <summary>尚未锁定</summary>
        Pending,
        /// <summary>胜利（第 9 波波末锁定）</summary>
        Victory,
        /// <summary>失败（玩家死亡锁定）</summary>
        Defeat
    }

    /// <summary>
    /// Mode G 退出原因。方案文档 §16，九种终局共用幂等 async End。
    /// </summary>
    public enum ModeGExitReason
    {
        None,
        Victory,
        PlayerDeath,
        SceneChanged,
        ManualExit,
        SpawnExhausted,
        TechnicalIntegrityLoss,
        RewardAbandoned,
        RewardInterruptedByDeath,
        ModDestroyed
    }

    /// <summary>
    /// Mode G 三轴反制类型。方案文档 §8。
    /// </summary>
    public enum ModeGCounterAxis
    {
        /// <summary>无反制</summary>
        None,
        /// <summary>距离回声（近距/远距反制）</summary>
        Distance,
        /// <summary>弹药禁令（点名禁用弹种）</summary>
        Ammo,
        /// <summary>属性封锁（枪械/近战伤害 ×0.75）</summary>
        Attribute
    }

    /// <summary>
    /// Mode G 生成槽结果终态。
    /// </summary>
    public enum ModeGSlotOutcome
    {
        /// <summary>尚未结案</summary>
        Pending,
        /// <summary>成功提交</summary>
        Committed,
        /// <summary>两次尝试耗尽</summary>
        Exhausted
    }

    /// <summary>
    /// Mode G 编排变体。方案文档 §6.2。
    /// </summary>
    public enum ModeGPlanVariant
    {
        /// <summary>标准三角分布</summary>
        Split,
        /// <summary>钳形包夹</summary>
        Pincer,
        /// <summary>弧形包围</summary>
        Arc
    }

    /// <summary>
    /// Mode G 宿敌性格。方案文档 §6.4（C5 裁决更名：Stalker/Bulwark/Chaos -> Hunter/Suppressor/Bulwark）。
    /// </summary>
    public enum ModeGNemesisTemperament
    {
        /// <summary>无性格（首胜局）</summary>
        None,
        /// <summary>追猎者：Pincer/近锚点编排偏好，Walk/Run 小幅提升（owner tunable 具体倍率）</summary>
        Hunter,
        /// <summary>压制者：Gun/Melee 伤害小幅提升（owner tunable 具体倍率）</summary>
        Suppressor,
        /// <summary>堡垒：MaxHealth 小幅提升（owner tunable 具体倍率）</summary>
        Bulwark
    }

    /// <summary>
    /// Mode G 相位守卫：纯函数判定，无状态。
    /// </summary>
    public static class ModeGPhaseGuards
    {
        /// <summary>
        /// 战斗相位判定：Fighting/LastStand 属于可计分战斗窗口。
        /// Spawning/Intermission/WaveSettling 不属于（不记遥测、不开成就伤害窗口）。
        /// </summary>
        public static bool IsCombatPhase(ModeGCombatPhase phase)
        {
            return phase == ModeGCombatPhase.Fighting || phase == ModeGCombatPhase.LastStand;
        }
    }

    /// <summary>
    /// Mode G 当前运行上下文持有者。仅 Mode G 内部写入，门控 API 只读。
    /// lifecycle 与 sink 分离：Current 归零但 sink 有 pending lease 时，
    /// IsModeGRunInProgress=false 而 IsModeGGlobalQuarantineActive=true。
    /// </summary>
    internal static class ModeGRunContext
    {
        private static readonly object _lock = new object();
        private static ModeGRunState _current;
        private static ModeGRuntimeModule _currentModule;

        public static ModeGRunState Current
        {
            get { lock (_lock) { return _current; } }
        }

        public static ModeGRuntimeModule CurrentModule
        {
            get { lock (_lock) { return _currentModule; } }
        }

        /// <summary>
        /// Starting 成功后绑定当前 run。幂等：重复绑定同一 state 不报错。
        /// </summary>
        public static void Bind(ModeGRunState state, ModeGRuntimeModule module)
        {
            lock (_lock)
            {
                _current = state;
                _currentModule = module;
            }
        }

        /// <summary>
        /// End 时按引用身份解绑；已被新 run 替换时不覆盖。
        /// </summary>
        public static void Unbind(ModeGRunState state)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_current, state))
                {
                    _current = null;
                    _currentModule = null;
                }
            }
        }
    }

    /// <summary>
    /// Mode G 跨任务门控 API（任务 #7/#8 契约，签名与 Jimmy 的共享层接线完全一致）。
    /// 所有成员 no-throw；异常路径全部返回安全默认值（false/空表）。
    /// </summary>
    public static class ModeGRuntimeGates
    {
        /// <summary>
        /// 生命周期进行中：LifecyclePhase != None。
        /// 只来自 lifecycle；sink 单独存在时不得为 true。
        /// </summary>
        public static bool IsModeGRunInProgress
        {
            get
            {
                try
                {
                    ModeGRunState state = ModeGRunContext.Current;
                    return state != null && state.lifecyclePhase != ModeGLifecyclePhase.None;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 全局隔离：仅 ModeGLateCleanupSink.HasPendingLeases。
        /// 普通地图 NPC/场景行为禁止读取；不得塞入 IsAnyBossRushLikeModeActive。
        /// </summary>
        public static bool IsModeGGlobalQuarantineActive
        {
            get
            {
                try
                {
                    return ModeGLateCleanupSink.HasPendingLeases;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 所有 BossRush-like 最终入口的统一闸门：RunInProgress || Quarantine。
        /// 移交 sink 后本地可为 None，但入口仍被隔离。
        /// </summary>
        public static bool IsModeGEntryBlocked
        {
            get
            {
                return IsModeGRunInProgress || IsModeGGlobalQuarantineActive;
            }
        }

        /// <summary>
        /// 成就伤害窗口：仅 Active + Fighting/LastStand 为 true。
        /// 消费点唯一：AchievementTriggers.OnPlayerHurtForAchievement。
        /// </summary>
        public static bool IsModeGAchievementDamageWindowActive
        {
            get
            {
                try
                {
                    ModeGRunState state = ModeGRunContext.Current;
                    if (state == null) return false;
                    return state.lifecyclePhase == ModeGLifecyclePhase.Active
                        && ModeGPhaseGuards.IsCombatPhase(state.combatPhase);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// OnDead 抑制查询：staging preset / 已登记 Character 的引用身份查询。
        /// no-throw；未登记角色返回 false（走原 handler）。
        /// </summary>
        public static bool IsModeGOnDeadSuppressionActive(Health h)
        {
            try
            {
                if (h == null) return false;
                ModeGRunState state = ModeGRunContext.Current;
                if (state == null) return false;
                return state.IsRegisteredBossHealth(h) || state.IsStagingBossHealth(h);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// OnDead 快速早返开关（CharacterOnDeadPatch Prefix 顶部使用，任务 #7 追加契约）。
        /// 先读静态快路径：无 run 时零分配 false。
        /// </summary>
        public static bool IsModeGSuppressionActive
        {
            get
            {
                try
                {
                    return ModeGRunContext.Current != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// staging 伤害屏障快路径开关（BossLethalHealthProtectionPatch 使用，任务 #7 追加契约）。
        /// 未激活时 false，查询方零分配早返。
        /// </summary>
        public static bool IsModeGStagingBarrierActive
        {
            get
            {
                try
                {
                    ModeGRunState state = ModeGRunContext.Current;
                    return state != null && (state.lifecyclePhase == ModeGLifecyclePhase.Starting
                        || (state.lifecyclePhase == ModeGLifecyclePhase.Active
                            && state.combatPhase == ModeGCombatPhase.Spawning));
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// staging 已登记 Health 查询（BossLethalHealthProtectionPatch 使用）。
        /// 命中返回 true（Prefix 返回 false 阻断 Legacy 致死保护逻辑）；no-throw，默认 false。
        /// </summary>
        public static bool IsModeGStagingHealthBlocked(Health h)
        {
            try
            {
                if (h == null) return false;
                ModeGRunState state = ModeGRunContext.Current;
                if (state == null) return false;
                return state.IsRegisteredBossHealth(h) || state.IsStagingBossHealth(h);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 大兴兴清理 owner 查询（ModBehaviour.TryCleanNonBossRushDaXingXing 使用，任务 #7 追加契约）。
        /// O(1)、no-throw、默认 false：Mode G 已提交/登记的 Character 不得被大兴兴清理误删。
        /// </summary>
        public static bool IsDaXingXingOwnedByModeG(CharacterMainControl character)
        {
            try
            {
                if (character == null) return false;
                ModeGRunState state = ModeGRunContext.Current;
                if (state == null) return false;
                return state.IsRegisteredBossCharacter(character);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// EnemyRecovery 用只读快照：当前已提交的托管 Boss 列表。可为空表，no-throw。
        /// </summary>
        public static List<CharacterMainControl> GetTrackedBosses()
        {
            List<CharacterMainControl> result = new List<CharacterMainControl>();
            try
            {
                ModeGRunState state = ModeGRunContext.Current;
                if (state == null) return result;
                state.CollectTrackedBosses(result);
            }
            catch
            {
                result.Clear();
            }
            return result;
        }
    }
}
