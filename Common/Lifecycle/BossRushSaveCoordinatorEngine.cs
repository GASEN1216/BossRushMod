// ============================================================================
// BossRushSaveCoordinatorEngine.cs - 内容子系统落盘协调器的共享引擎
// ============================================================================
// 为什么需要它（2026-09-06，D-2）：
//   征程 / 图鉴 / 日报 / 遗种巢四个协调器此前是逐字复制的同一份状态机
//   （pending 合并成一批 -> FlushPending 交给 SavesSystem -> 每批至多一次 SaveFile；
//   IsSaving / 非基地 / 每帧闸 三种推迟 + 600 帧重试预算 + 「欠一次 SaveFile」独立记账）。
//   同一条 bug（重试链被自身消费，CR-2026-09-04-01x）曾经要在四份里各修一遍，守卫也只能
//   逐份锁同一条不变式。现在状态机只有这一份；子系统只保留一行式门面 + 自己的数据源
//   （IBossRushSaveBatchSource：typed pending、单向故障、现金/实物快照义务）。
//
// 语义（与四个原协调器逐条对齐；由 SaveCoordinatorRetryGuard 等守卫钉死）：
//   - 「没有 pending」**不等于**「无事可做」：FlushPending 成功后 pending 即被消费，
//     若随后的 SaveFile 失败，只看 HasPendingWrite 会把重试与宿主销毁兜底一起吃掉——
//     数据停在 SavesSystem 内存里永不落盘。因此「欠一次 SaveFile」独立成
//     _saveFilePending，早返条件同时看它，只有 SaveFile 真正成功才清；
//   - SavesSystem.IsSaving 时不强写，只登记 deferred，由宿主 Tick 重试；
//   - deferOutsideBaseScene=true 的子系统只在基地物理落盘：官方 SaveFile 会做备份拷贝 +
//     整档同步写盘，而写入点天然可能落在交火帧上；非基地一律推迟到回基地补写，
//     宿主销毁与关停走 bypassGates 兜底（最后机会，宁可在战斗帧写一次也不丢进度）；
//   - 非基地的 Tick **不消耗重试预算**，否则长时间战斗会把 pending 丢成 budget_exhausted；
//   - deferred 重试有预算上限，超预算保留 pending 并报告失败，不静默丢弃；
//   - 快照义务（现金 / 实物）与 typed pending 同批：物理写失败或被节流后仍保留义务，
//     只有 SaveFile 成功后才经 OnPhysicalSaveSucceeded 由数据源清掉；
//   - 单向故障（IsStoreFaulted）后不再物理落盘：typed 数据已在 ES3 缓存，官方任一次存盘会带走；
//   - 每批至多一次 SaveFile，且经 BossRushSaveFileThrottle 的跨子系统每帧闸；
//   - SaveFile(false) 不触发 OnCollectSaveData，因此不能拿它当「采集已完成」的证明。
//
// 这是四个内容子系统**唯一**的 SavesSystem.SaveFile 调用点。Mode G / Mode H 的协调器
// 因战斗帧顺延与多 key 屏障语义不同，仍各自独立。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 落盘协调引擎的数据源：一个子系统的 typed pending 与可选的快照义务。
    /// 实现者是各子系统协调器门面里的私有嵌套类。
    /// </summary>
    internal interface IBossRushSaveBatchSource
    {
        /// <summary>日志前缀（含尾随空格），例如 "[Codex] "。</summary>
        string LogPrefix { get; }

        /// <summary>是否有已入队、尚未交给 SavesSystem 的 typed pending。</summary>
        bool HasPendingWrite { get; }

        /// <summary>typed 存档门面是否已进入单向故障。</summary>
        bool IsStoreFaulted { get; }

        /// <summary>是否欠一次快照（现金 / 实物）采集：与 pending 同批，SaveFile 成功前不得清。</summary>
        bool HasSnapshotObligation { get; }

        /// <summary>typed 门面最近一次失败原因（可为 null）。</summary>
        string LastError { get; }

        /// <summary>采集快照。无义务时直接返回 true；失败返回 false 并给出原因 id。</summary>
        bool CollectSnapshot(out string error);

        /// <summary>把 typed pending 写进 SavesSystem 内存（不 SaveFile）。IsSaving / 写失败返回 false。</summary>
        bool FlushPending();

        /// <summary>物理落盘成功后的回调：清快照义务、做子系统自己的成功后处理。</summary>
        void OnPhysicalSaveSucceeded();
    }

    /// <summary>
    /// 落盘协调器的共享状态机。一个子系统一个实例，由该子系统的 static 门面持有。
    /// 全部入口 no-throw。
    /// </summary>
    internal sealed class BossRushSaveCoordinatorEngine
    {
        /// <summary>deferred 重试上限；超过后保留 pending 并报告失败，不静默丢弃。</summary>
        private const int MaxDeferredRetries = 600;

        private readonly object _lock = new object();
        private readonly IBossRushSaveBatchSource _source;
        private readonly bool _deferOutsideBaseScene;

        private bool _deferredFlushPending;
        private int _deferredRetryCount;

        /// <summary>
        /// pending 已经交给 SavesSystem、但物理落盘（SaveFile）尚未成功。
        /// 必须与 _deferredFlushPending 分开记：FlushPending 一旦成功，HasPendingWrite
        /// 就变 false，此后只靠它判断「有没有事要做」会把「还欠一次 SaveFile」误判成
        /// 「无事可做」，重试链就此断掉。
        /// </summary>
        private bool _saveFilePending;

        private string _lastError;

        /// <param name="source">子系统数据源。</param>
        /// <param name="deferOutsideBaseScene">
        /// true = 只在基地物理落盘（征程 / 图鉴 / 日报）；
        /// false = 任何场景都立即试写（遗种巢：实物资产屏障要求 Store 之后立刻 durable）。
        /// 两种模式下 Tick 的重试都按基地门控且不计预算。
        /// </param>
        internal BossRushSaveCoordinatorEngine(IBossRushSaveBatchSource source, bool deferOutsideBaseScene)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            _deferOutsideBaseScene = deferOutsideBaseScene;
        }

        #region 只读

        /// <summary>是否存在等待落盘的批次。</summary>
        internal bool HasDeferredFlush { get { lock (_lock) { return _deferredFlushPending; } } }

        /// <summary>最后一次失败原因。</summary>
        internal string LastError { get { lock (_lock) { return _lastError; } } }

        #endregion

        #region 对外入口

        /// <summary>
        /// 请求把当前 pending 落盘。成功返回 true；
        /// IsSaving / 非基地 / 节流 / 写失败时返回 false 并保留 pending（由 Tick 重试）。
        /// </summary>
        internal bool RequestFlush(out string error)
        {
            return FlushBatch(out error, false);
        }

        /// <summary>
        /// 同上；<paramref name="bypassGates"/> = true 时绕过基地闸与每帧闸。
        /// 只有宿主销毁 / 切槽最后机会 / 实物资产屏障这类「没有下一帧」的时点才允许传 true。
        /// </summary>
        internal bool RequestFlush(out string error, bool bypassGates)
        {
            return FlushBatch(out error, bypassGates);
        }

        /// <summary>宿主 tick：重试被推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal void Tick()
        {
            if (!_deferredFlushPending) return;
            // 非基地：保留 pending、不试写、**不计重试预算**。
            // 战斗可以持续远超 600 帧，在这里消耗预算会把 pending 直接丢成预算耗尽。
            if (!IsBaseLevelSafe()) return;

            lock (_lock)
            {
                _deferredRetryCount++;
                if (_deferredRetryCount > MaxDeferredRetries)
                {
                    _lastError = "flush_deferred_budget_exhausted";
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    ModBehaviour.DevLog(_source.LogPrefix
                        + "[ERROR] 存档 deferred 重试预算耗尽，pending 保留待下次写入触发");
                    return;
                }
            }

            string error;
            FlushBatch(out error, false);
        }

        /// <summary>宿主销毁时尽力提交一次（绕过所有闸）；失败只记录，不抛出。</summary>
        internal bool TryFlushOnHostDestroy()
        {
            try
            {
                string error;
                return FlushBatch(out error, true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>切档 / 删档：清空 deferred 状态。旧槽欠的 SaveFile 不该拿新槽去补。</summary>
        internal void NotifySlotChanged()
        {
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                _saveFilePending = false;
                _lastError = null;
            }
        }

        /// <summary>静态复位（Mod 卸载 / 宿主重建）。与切档同语义。</summary>
        internal void Reset()
        {
            NotifySlotChanged();
        }

        #endregion

        #region 批次落盘（唯一物理写入点）

        private bool FlushBatch(out string error, bool bypassGates)
        {
            error = null;
            try
            {
                bool saveFileOwed;
                lock (_lock)
                {
                    saveFileOwed = _saveFilePending;
                }

                // 「没有 pending」不等于「无事可做」（见文件头）；快照义务同样不能被 pending 消费掉。
                if (!_source.HasPendingWrite && !saveFileOwed && !_source.HasSnapshotObligation)
                {
                    lock (_lock)
                    {
                        _deferredFlushPending = false;
                        _deferredRetryCount = 0;
                    }
                    return true;
                }

                if (_deferOutsideBaseScene && !bypassGates && !IsBaseLevelSafe())
                {
                    return Defer("flush_deferred_not_base", out error);
                }

                if (SavesSystem.IsSaving)
                {
                    return Defer("flush_deferred_is_saving", out error);
                }

                // 快照（现金 / 实物）排在 typed pending 之前：领取标记不得先于资产落盘。
                string snapshotError;
                if (!_source.CollectSnapshot(out snapshotError))
                {
                    return Defer(snapshotError ?? "snapshot_unavailable", out error);
                }

                if (_source.HasPendingWrite)
                {
                    if (!_source.FlushPending())
                    {
                        return Defer(_source.LastError ?? "key_flush_failed", out error);
                    }

                    // 已进 SavesSystem 内存，但还没写盘：从这一刻起就欠一次 SaveFile，
                    // 直到 SaveFile 真的成功才清掉。
                    lock (_lock)
                    {
                        _saveFilePending = true;
                    }
                }

                if (_source.IsStoreFaulted)
                {
                    // 单向故障不登记 deferred：重试无意义，typed 数据由官方存盘顺带带走
                    error = "store_faulted";
                    lock (_lock) { _lastError = error; }
                    return false;
                }

                // 跨子系统的每帧闸：回基地首帧极易多个协调器挤在同一帧。
                // 被拒时沿用 deferred + Tick 重试链在后续帧补写，与 IsSaving 分支同语义。
                if (!BossRushSaveFileThrottle.TryBeginSaveFile(bypassGates))
                {
                    return Defer("flush_deferred_savefile_frame_busy", out error);
                }

                SavesSystem.SaveFile(false);

                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    _saveFilePending = false;
                    _lastError = null;
                }
                _source.OnPhysicalSaveSucceeded();
                return true;
            }
            catch (Exception e)
            {
                error = "flush_exception:" + e.GetType().Name;
                lock (_lock)
                {
                    _deferredFlushPending = true;
                    _lastError = error;
                }
                ModBehaviour.DevLog(_source.LogPrefix + "[ERROR] 存档批次落盘异常: " + e.Message);
                return false;
            }
        }

        /// <summary>登记一次推迟：置 deferred、记原因、返回 false。</summary>
        private bool Defer(string reason, out string error)
        {
            error = reason;
            lock (_lock)
            {
                _deferredFlushPending = true;
                _lastError = reason;
            }
            return false;
        }

        /// <summary>
        /// 当前是否在基地场景。读不到 LevelManager 时按「非基地」处理（保守推迟），
        /// 宿主销毁 / 关停路径有 bypassGates 兜底，不会因此丢盘。
        /// </summary>
        internal static bool IsBaseLevelSafe()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}
