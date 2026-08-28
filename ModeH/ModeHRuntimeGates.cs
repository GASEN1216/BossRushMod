using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 真实资产 journal 的轻量风险头（§18.1）。
    /// 只用于回答“当前槽是否存在未终结的真实资产事务”，不加载完整 journal payload。
    /// </summary>
    [Serializable]
    public sealed class ModeHStakeJournalHeaderDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>签名算法版本。</summary>
        public int signatureAlgorithmVersion;
        /// <summary>事务 ID。</summary>
        public string txId;
        /// <summary>存档槽 ID。</summary>
        public int slotId;
        /// <summary>存档槽 generation。</summary>
        public int slotGeneration;
        /// <summary>ModeHStakePhase 的整数值。</summary>
        public int phase;
        /// <summary>阶段序号。</summary>
        public int phaseSequence;
    }

    /// <summary>
    /// Mode H 运行时门（设计提案 §18.1、§24.3）。
    ///
    /// 对外只暴露五个 no-throw、slot-aware 的只读结果，语义严格互不混用：
    /// - IsModeHRunOwnerActive：当前唯一 runtime 实例是否持有运行 owner；
    /// - IsModeHRiskScanReady：当前 slot 的真实资产 journal 轻量风险头扫描是否完成；
    /// - IsModeHContentReady：H 配置、Season/HallOfFame 读取、资源、地图、静态候选与
    ///   口令兼容矩阵是否已具备运行正式自检的条件（不代表 runtime 已通过生产认证）；
    /// - IsModeHExternalAssetRiskBlocked：是否存在未终结真实资产 journal / operation 或风险未知；
    /// - IsModeHRecoveryOnlyBlocked：是否存在 Season 恢复壳、late cleanup 或 Mode H slot barrier。
    ///
    /// 旧模式最终入口只读取 IsModeHRiskScanReady 与 IsModeHExternalAssetRiskBlocked，
    /// 绝不等待 H 内容或恢复壳状态。
    /// </summary>
    public static class ModeHRuntimeGates
    {
        #region 状态

        private static readonly object _lock = new object();

        private static bool _riskScanReady;
        private static bool _externalAssetRiskBlocked;
        private static bool _contentReady;
        private static bool _recoveryOnlyBlocked;
        private static bool _runOwnerActive;
        private static int _slotGeneration;
        private static string _riskReasonId;
        private static string _contentReasonId;

        // ERROR 完整互换（§17.6.5 部件二）：看台身体解冻门。
        // 只有 ModeHCombatControl 在持有有效 run owner token 且互换已完成时才置位；
        // 释放、倒地、比赛结束、技术中止、切图与 OnDestroy 都必须清零。
        // 两个 Harmony postfix 读的是它的零分配快路径。
        private static volatile bool _standInActive;
        private static int _standInBodyInstanceId;

        #endregion

        #region 五个只读结果

        /// <summary>当前唯一 runtime 实例是否持有运行 owner。</summary>
        public static bool IsModeHRunOwnerActive
        {
            get
            {
                try { return _runOwnerActive; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>当前 slot 的真实资产风险头扫描是否完成。</summary>
        public static bool IsModeHRiskScanReady
        {
            get
            {
                try { return _riskScanReady; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>H 内容是否已具备运行正式生产自检的条件。</summary>
        public static bool IsModeHContentReady
        {
            get
            {
                try { return _contentReady; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>是否存在未终结真实资产事务或其风险无法判定。</summary>
        public static bool IsModeHExternalAssetRiskBlocked
        {
            get
            {
                try { return _externalAssetRiskBlocked; }
                catch (Exception) { return true; }
            }
        }

        /// <summary>是否存在恢复壳 / late-cleanup / slot barrier。</summary>
        public static bool IsModeHRecoveryOnlyBlocked
        {
            get
            {
                try { return _recoveryOnlyBlocked; }
                catch (Exception) { return true; }
            }
        }

        /// <summary>风险扫描失败原因（诊断展示用）。</summary>
        public static string RiskReasonId { get { return _riskReasonId; } }

        /// <summary>内容不可用原因（诊断展示用）。</summary>
        public static string ContentReasonId { get { return _contentReasonId; } }

        /// <summary>当前记录的存档槽 generation。</summary>
        public static int SlotGeneration { get { return _slotGeneration; } }

        #endregion

        #region 生命周期

        /// <summary>
        /// 槽位切换 / 删档 / Mod 重启时先把全部结果置回未就绪。
        /// 未就绪意味着旧模式最终入口会等待一次轻量风险扫描。
        /// </summary>
        public static void ResetForSlotChange()
        {
            lock (_lock)
            {
                _riskScanReady = false;
                _externalAssetRiskBlocked = false;
                _contentReady = false;
                _recoveryOnlyBlocked = false;
                _runOwnerActive = false;
                _riskReasonId = null;
                _contentReasonId = null;
            }
        }

        /// <summary>清空全部静态状态（Mod 卸载 / 宿主重建）。</summary>
        public static void ResetStaticCaches()
        {
            ResetForSlotChange();
            // 看台解冻门必须随 Mod 卸载归零（ModeHIsolationGuard 断言退出后为 false）
            SetStandInActive(false, 0);
            lock (_lock)
            {
                _slotGeneration = 0;
            }
        }

        /// <summary>
        /// OnSetFile 后立即执行的轻量风险扫描（§18.1）。
        /// 只调用 KeyExisits 并读取 stake journal 的 raw envelope header；
        /// 不加载 Season、HallOfFame、bundle、JSON、候选池或完整 journal payload。
        /// modeHEnabled=false 也不能跳过该检查。
        /// </summary>
        public static void InitializeRiskForSlot(int slotGeneration)
        {
            lock (_lock)
            {
                _slotGeneration = slotGeneration;
                _riskScanReady = false;
                _externalAssetRiskBlocked = false;
                _riskReasonId = null;
            }

            bool keyExists;
            try
            {
                keyExists = SavesSystem.KeyExisits(ModeHConfig.StakeJournalStorageKey);
            }
            catch (Exception e)
            {
                lock (_lock)
                {
                    _riskScanReady = false;
                    _externalAssetRiskBlocked = true;
                    _riskReasonId = "risk_key_probe_failed:" + e.GetType().Name;
                }
                return;
            }

            if (!keyExists)
            {
                // 全新槽或从未押过真实物品：同步得到 ready + unblocked，不产生可见等待
                lock (_lock)
                {
                    _riskScanReady = true;
                    _externalAssetRiskBlocked = false;
                    _riskReasonId = null;
                }
                return;
            }

            ModeHStakeJournalHeaderDto header = null;
            try
            {
                header = SavesSystem.Load<ModeHStakeJournalHeaderDto>(ModeHConfig.StakeJournalStorageKey);
            }
            catch (Exception e)
            {
                lock (_lock)
                {
                    _riskScanReady = false;
                    _externalAssetRiskBlocked = true;
                    _riskReasonId = "risk_header_unreadable:" + e.GetType().Name;
                }
                return;
            }

            if (header == null)
            {
                lock (_lock)
                {
                    _riskScanReady = false;
                    _externalAssetRiskBlocked = true;
                    _riskReasonId = "risk_header_null";
                }
                return;
            }

            ModeHStakePhase phase = ModeHStateModel.ToStakePhase(header.phase);
            if (phase == ModeHStakePhase.Unknown)
            {
                lock (_lock)
                {
                    _riskScanReady = false;
                    _externalAssetRiskBlocked = true;
                    _riskReasonId = "risk_phase_unknown";
                }
                return;
            }

            bool terminal = phase == ModeHStakePhase.None || ModeHStateModel.IsStakePhaseTerminal(phase);
            lock (_lock)
            {
                _riskScanReady = true;
                _externalAssetRiskBlocked = !terminal;
                _riskReasonId = terminal ? null : "risk_active_journal:" + phase.ToString();
            }
        }

        /// <summary>
        /// 内容扫描（配置开启或存在 H 恢复记录时才执行）。
        /// 校验静态数据、候选目录与口令矩阵是否具备运行正式自检的条件；
        /// 不执行 runtime 生产认证，也不把未执行的认证伪装为通过。
        /// </summary>
        public static void InitializeContentForSlot()
        {
            bool ready = false;
            string reason = null;
            try
            {
                if (!ModeHContentCatalog.EnsureLoaded())
                {
                    reason = ModeHContentCatalog.LastError;
                }
                else if (!ModeHProfileRegistry.EnsureValidated())
                {
                    reason = ModeHProfileRegistry.LastError;
                }
                else if (!ModeHLoadoutKitRegistry.EnsureValidated())
                {
                    reason = ModeHLoadoutKitRegistry.LastError;
                }
                else if (!ModeHCommandCompatibilityRegistry.EnsureValidated())
                {
                    reason = ModeHCommandCompatibilityRegistry.LastError;
                }
                else
                {
                    ready = true;
                }
            }
            catch (Exception e)
            {
                ready = false;
                reason = "content_scan_exception:" + e.GetType().Name;
            }

            lock (_lock)
            {
                _contentReady = ready;
                _contentReasonId = reason;
            }
        }

        /// <summary>
        /// 看台身体是否已解冻（§17.6.5 部件二的唯一门）。
        /// 热路径读取：零分配 bool，不加锁、不建集合。
        /// </summary>
        public static bool IsModeHStandInActive { get { return _standInActive; } }

        /// <summary>
        /// 置位/清零看台解冻门。唯一写入点由 ModeHCombatControl 持有；
        /// bodyInstanceId 用于 postfix 的 O(1) 身份比对，清零时一并置 0。
        /// </summary>
        public static void SetStandInActive(bool active, int bodyInstanceId)
        {
            _standInBodyInstanceId = active ? bodyInstanceId : 0;
            _standInActive = active && bodyInstanceId != 0;
        }

        /// <summary>该 GameObject 是否是本场已登记的玩家身体（postfix 的 O(1) 身份查询）。</summary>
        public static bool IsModeHStandInBody(int instanceId)
        {
            return _standInActive && instanceId != 0 && instanceId == _standInBodyInstanceId;
        }

        /// <summary>由唯一 runtime 实例登记/撤销运行 owner。</summary>
        public static void SetRunOwnerActive(bool active)
        {
            lock (_lock)
            {
                _runOwnerActive = active;
            }
        }

        /// <summary>
        /// 设置 recovery-only 阻断（Season 恢复壳、late cleanup、slot barrier）。
        /// 它只阻止新的 Mode H 赛季，绝不阻断旧模式入口。
        /// </summary>
        public static void SetRecoveryOnlyBlocked(bool blocked, string reasonId)
        {
            lock (_lock)
            {
                _recoveryOnlyBlocked = blocked;
                if (blocked && !string.IsNullOrEmpty(reasonId))
                {
                    _contentReasonId = reasonId;
                }
            }
        }

        /// <summary>
        /// 真实资产路径在事务生命周期内更新 external risk。
        /// 只有未终结 journal / operation 或风险未知才允许置 true。
        /// </summary>
        public static void SetExternalAssetRiskBlocked(bool blocked, string reasonId)
        {
            lock (_lock)
            {
                _externalAssetRiskBlocked = blocked;
                _riskReasonId = blocked ? reasonId : null;
            }
        }

        #endregion

        #region 组合判定

        /// <summary>
        /// 旧模式最终入口使用的唯一组合：风险扫描完成且没有未终结真实资产事务。
        /// 不读取 H content、run owner 或 recovery-only 状态。
        /// </summary>
        public static bool IsLegacyModeEntryAllowed()
        {
            return IsModeHRiskScanReady && !IsModeHExternalAssetRiskBlocked;
        }

        /// <summary>
        /// Mode H 新赛季入口使用的组合（还需 owner.IsModeHConfiguredEnabled() 与
        /// 进入场景后的 ModeHProductionCertification.IsCurrentRuntimePassed）。
        /// </summary>
        public static bool IsNewSeasonEntryAllowed()
        {
            return IsModeHRiskScanReady
                && IsModeHContentReady
                && !IsModeHExternalAssetRiskBlocked
                && !IsModeHRecoveryOnlyBlocked
                && !IsModeHRunOwnerActive;
        }

        /// <summary>恢复入口：只要存在恢复壳或未终结真实资产事务就开放，且允许配置关闭。</summary>
        public static bool IsRecoveryEntryAllowed()
        {
            return IsModeHRecoveryOnlyBlocked || IsModeHExternalAssetRiskBlocked;
        }

        #endregion
    }
}
