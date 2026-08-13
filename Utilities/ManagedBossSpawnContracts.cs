using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode G 终结武器族（ManagedBossTerminalCredit 专用；
    /// 规格 §13.2 引用 WeaponFamily，项目无同名官方类型，按 §14 直伤分类语义在 Mode G 侧冻结）。
    /// </summary>
    internal enum WeaponFamily
    {
        /// <summary>不可计分/未知</summary>
        None,
        /// <summary>枪械直伤</summary>
        Gun,
        /// <summary>近战直伤</summary>
        Melee
    }

    /// <summary>
    /// 托管 Boss 所有权（规格 §13.2）。Legacy = 现有公开生成器语义；ModeG = 托管窄契约。
    /// </summary>
    internal enum ManagedBossOwner
    {
        Legacy,
        ModeG
    }

    /// <summary>
    /// 托管 Boss 角色（规格 §13.2）。
    /// </summary>
    internal enum ManagedBossRole
    {
        /// <summary>计分主 Boss</summary>
        Primary,
        /// <summary>辅助单位（女巫随从等）</summary>
        Auxiliary,
        /// <summary>阶段代理（龙王"孩儿护我"龙裔）</summary>
        PhaseProxy
    }

    /// <summary>
    /// 托管 Boss 清理原因（规格 §13.2）。
    /// </summary>
    internal enum ManagedBossCleanupReason
    {
        SpawnRejected,
        OwnerInvalid,
        TechnicalLoss,
        Death,
        RunEnded
    }

    /// <summary>
    /// 托管 Boss 终结归因快照（规格 §13.2）。死亡帧冻结，之后不可变。
    /// </summary>
    internal struct ManagedBossTerminalCredit
    {
        public CharacterMainControl FromCharacter;
        public Vector3 WorldPosition;
        public bool HasWorldPosition;
        public bool IsDirectPlayerHit;
        public WeaponFamily TerminalWeaponFamily;

        public static ManagedBossTerminalCredit FromCharacterHit(
            CharacterMainControl fromCharacter, Vector3 worldPosition,
            bool isDirectPlayerHit, WeaponFamily family)
        {
            ManagedBossTerminalCredit credit = new ManagedBossTerminalCredit();
            credit.FromCharacter = fromCharacter;
            credit.WorldPosition = worldPosition;
            credit.HasWorldPosition = true;
            credit.IsDirectPlayerHit = isDirectPlayerHit;
            credit.TerminalWeaponFamily = family;
            return credit;
        }
    }

    /// <summary>
    /// 托管 Boss 生成上下文（规格 §13.2）。
    /// 由 EnemySpawnCoreOptions.ManagedBossContext（object 类型）携带，Mode G 侧强转。
    /// 默认值 = Legacy 全开；Mode G 主 Boss 固定关闭八个 Legacy 行为开关（§13.2 803 行块）。
    /// </summary>
    internal sealed class ManagedBossSpawnContext
    {
        public ManagedBossOwner Owner = ManagedBossOwner.Legacy;
        public ManagedBossRole Role = ManagedBossRole.Primary;
        /// <summary>本次冻结的稳定 key（归因/遥测/宿敌用）</summary>
        public string CreditPresetKey;
        /// <summary>每次尝试独占的运行时 preset clone；owned overload 收到后禁止再查 base preset</summary>
        public CharacterRandomPreset FactoryPresetOverride;

        // ---- 八个 Legacy 行为开关（Legacy 默认全 true；Mode G 主 Boss 全 false）----
        public bool WriteStandardWaveState = true;
        public bool InstallLegacyDeathProgressionHandler = true;
        public bool InstallLegacyAchievementHandler = true;
        public bool RegisterStandardLootTracking = true;
        public bool DropBoxOnDead = true;
        public bool ActivateBeforeReturn = true;
        public bool RegisterRecoveryInternally = true;
        public bool ShowLegacyMessages = true;

        /// <summary>龙王联动击杀来源保留（仅 Mode G 传玩家来源）</summary>
        public bool PreserveLinkedKillAttribution;

        /// <summary>owner 存活校验（每个长 await 后重验）</summary>
        public Func<bool> IsOwnerValid;
        /// <summary>辅助单位激活前原子提交（返回 true 才可激活）</summary>
        public Func<CharacterMainControl, ManagedBossRole, bool> TryCommitAuxiliaryBeforeActivation;
        /// <summary>辅助单位释放通知（仅成功提交者，恰好一次）</summary>
        public Action<CharacterMainControl, ManagedBossRole> OnAuxiliaryReleased;

        /// <summary>
        /// 构造 Mode G 主 Boss 固定上下文（§13.2：八个开关全 false，PreserveLinkedKillAttribution=true）。
        /// </summary>
        public static ManagedBossSpawnContext CreateModeGPrimary(string creditPresetKey, Func<bool> isOwnerValid)
        {
            ManagedBossSpawnContext ctx = new ManagedBossSpawnContext();
            ctx.Owner = ManagedBossOwner.ModeG;
            ctx.Role = ManagedBossRole.Primary;
            ctx.CreditPresetKey = creditPresetKey;
            ctx.WriteStandardWaveState = false;
            ctx.InstallLegacyDeathProgressionHandler = false;
            ctx.InstallLegacyAchievementHandler = false;
            ctx.RegisterStandardLootTracking = false;
            ctx.DropBoxOnDead = false;
            ctx.ActivateBeforeReturn = false;
            ctx.RegisterRecoveryInternally = false;
            ctx.ShowLegacyMessages = false;
            ctx.PreserveLinkedKillAttribution = true;
            ctx.IsOwnerValid = isOwnerValid;
            return ctx;
        }
    }

    /// <summary>
    /// 托管 Boss 运行时 handle（规格 §13.2）。
    /// Activate/Cleanup 幂等；cleanup 按精确 Character owner 退订，不盲清全局单例/共享集合。
    /// </summary>
    internal sealed class ManagedBossRuntimeHandle
    {
        public CharacterMainControl Character;
        /// <summary>成就窄上报 bossType（ordinary 为 "Normal"）</summary>
        public string AchievementBossType;
        /// <summary>激活 GO/Character + 启动完整能力/FX/BGM（幂等）</summary>
        public Func<bool> Activate;
        /// <summary>真实死亡后幂等清理（死亡帧调用）</summary>
        public Action<DamageInfo> CleanupAfterDeath;
        /// <summary>统一幂等清理（按 reason 分类）</summary>
        public Action<ManagedBossCleanupReason> Cleanup;
        /// <summary>消费终结归因（一次性）</summary>
        public Func<ManagedBossTerminalCredit> ConsumeTerminalCredit = null;

        private bool _cleanupInvoked;
        private readonly object _cleanupLock = new object();

        /// <summary>
        /// 幂等调用 Cleanup（任何路径最多一次）。no-throw。
        /// </summary>
        public void CleanupOnce(ManagedBossCleanupReason reason)
        {
            bool shouldInvoke;
            lock (_cleanupLock)
            {
                shouldInvoke = !_cleanupInvoked;
                _cleanupInvoked = true;
            }
            if (!shouldInvoke || Cleanup == null) return;
            try
            {
                Cleanup(reason);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] managed handle cleanup 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 幂等调用 Activate。no-throw；异常返回 false。
        /// </summary>
        public bool ActivateOnce()
        {
            if (Activate == null) return false;
            try
            {
                return Activate();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] managed handle activate 异常: " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// PrepareManagedXxx 返回值（规格 §13.2）。
    /// </summary>
    internal sealed class ManagedBossPrepareResult
    {
        public CharacterMainControl Character;
        public ManagedBossRuntimeHandle Handle;
    }
}
