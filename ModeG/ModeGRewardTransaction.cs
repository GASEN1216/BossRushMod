using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode G 奖励分带（§7 冻结）。
    /// </summary>
    public enum ModeGRewardBand
    {
        /// <summary>GeneralBase：仅 &lt;P75（槽 1-6）</summary>
        GeneralBase,
        /// <summary>PremiumP75P95：P75..P95 含边界（槽 7-10）</summary>
        PremiumP75P95,
        /// <summary>&gt;P95：ExtremeExcluded，首版不进任何槽</summary>
        ExtremeExcluded
    }

    /// <summary>
    /// Mode G 奖励候选 DTO（Starting 一次构建，TypeID 升序去重冻结）。
    /// 禁字段初始化器无关（非存档 DTO），保持只读字段。
    /// </summary>
    public struct ModeGRewardCandidate
    {
        public readonly int typeId;
        /// <summary>单件估值（long，防溢出）</summary>
        public readonly long priceEach;
        /// <summary>默认堆叠数（估值 = priceEach × defaultStackCount）</summary>
        public readonly int defaultStackCount;

        public ModeGRewardCandidate(int typeId, long priceEach, int defaultStackCount)
        {
            this.typeId = typeId;
            this.priceEach = priceEach;
            this.defaultStackCount = defaultStackCount < 1 ? 1 : defaultStackCount;
        }

        /// <summary>整槽估值（priceEach × defaultStackCount）</summary>
        public long EstimatedSlotValue { get { return priceEach * defaultStackCount; } }
    }

    /// <summary>
    /// Mode G 严格奖励事务（C6 裁决重写版：Q5+ strict）。方案文档 §7/§13。
    ///
    /// 硬约束：
    /// - 10 槽 RewardSlotPlan；Resolve 0-2→6 件、3-5→7、6-8→8、9→9、10-11→10（owner tunable）；
    /// - 分带：GeneralBase 仅 &lt;P75（槽 1-6 至少 7 项）；PremiumP75P95 含边界（槽 7-10 至少 5 项）；
    ///   &gt;P95 ExtremeExcluded 不进槽；两池严格互斥；
    /// - nearest-rank = clamp(0, N-1, ceil(p*N)-1)；
    /// - 估值 long priceEach × defaultStackCount；
    /// - rewardNonce/attemptNonce 双 nonce；Rewarding 死亡先失效 attempt；
    /// - strict materializer 每帧最多 1 件 InstantiateSync（复用 ModeGRewardStrictMaterializer）；
    /// - 胜利安全 lease（materializer 完成前 sink 隔离不归零）；
    /// - 胜利结算额外幂等返还消耗的信物（TypeID 500057）一次。
    /// 奖励只在 Victory 后发放；Defeat 不发放任何奖励。
    /// </summary>
    public static class ModeGRewardTransaction
    {
        #region Constants

        /// <summary>槽位总数（冻结 10）</summary>
        public const int SlotCount = 10;
        /// <summary>GeneralBase 槽范围（0-based 0..5）</summary>
        public const int GeneralSlotBegin = 0;
        public const int GeneralSlotEnd = 6;
        /// <summary>Premium 槽范围（0-based 6..9）</summary>
        public const int PremiumSlotBegin = 6;
        public const int PremiumSlotEnd = 10;
        /// <summary>GeneralBase 池最小候选数（owner 冻结值 7）</summary>
        public const int GeneralPoolMinimum = 7;
        /// <summary>Premium 池最小候选数（owner 冻结值 5）</summary>
        public const int PremiumPoolMinimum = 5;
        /// <summary>分位数（nearest-rank）</summary>
        private const double Percentile75 = 0.75;
        private const double Percentile95 = 0.95;
        /// <summary>Resolve 上限</summary>
        public const int MaxResolve = 11;
        /// <summary>信物 TypeID（胜利返还，TypeID 500057 永不复用）</summary>
        public const int RelicTypeId = FateEchoRelicConfig.TYPE_ID;

        #endregion

        #region Slot Plan

        /// <summary>
        /// 奖励槽位计划（不可变）。
        /// </summary>
        public struct RewardSlotPlan
        {
            public readonly int typeId;
            public readonly ModeGRewardBand band;
            /// <summary>整槽估值（priceEach × defaultStackCount）</summary>
            public readonly long estimatedValue;

            public RewardSlotPlan(int typeId, ModeGRewardBand band, long estimatedValue)
            {
                this.typeId = typeId;
                this.band = band;
                this.estimatedValue = estimatedValue;
            }
        }

        #endregion

        #region Resolve -> Item Count（owner tunable，§7）

        /// <summary>
        /// Resolve 计数 -> 奖励件数：0-2→6、3-5→7、6-8→8、9→9、10-11→10。
        /// </summary>
        public static int GetRewardItemCount(int resolveCount)
        {
            if (resolveCount < 0) resolveCount = 0;
            if (resolveCount > MaxResolve) resolveCount = MaxResolve;
            if (resolveCount <= 2) return 6;
            if (resolveCount <= 5) return 7;
            if (resolveCount <= 8) return 8;
            if (resolveCount == 9) return 9;
            return 10;
        }

        #endregion

        #region Percentile / Banding（nearest-rank）

        /// <summary>
        /// nearest-rank 分位值：index = clamp(0, N-1, ceil(p*N)-1)，输入须为升序价格表。
        /// </summary>
        public static long NearestRankValue(long[] sortedPricesAscending, double p)
        {
            if (sortedPricesAscending == null || sortedPricesAscending.Length == 0) return 0;
            int n = sortedPricesAscending.Length;
            int idx = (int)Math.Ceiling(p * n) - 1;
            if (idx < 0) idx = 0;
            if (idx > n - 1) idx = n - 1;
            return sortedPricesAscending[idx];
        }

        /// <summary>
        /// 分带：GeneralBase 仅 &lt;P75；Premium P75..P95 含边界；&gt;P95 ExtremeExcluded。
        /// 两池严格互斥（同一候选只进一池）。
        /// </summary>
        public static ModeGRewardBand ClassifyBand(long price, long p75Value, long p95Value)
        {
            if (price > p95Value) return ModeGRewardBand.ExtremeExcluded;
            if (price >= p75Value) return ModeGRewardBand.PremiumP75P95;
            return ModeGRewardBand.GeneralBase;
        }

        #endregion

        #region Build Slot Plan（确定性，Victory 下一帧提交）

        /// <summary>
        /// 构建 10 槽冻结计划，并按 Resolve 截取前 count 件。
        /// 候选不足（GeneralBase &lt;7 或 Premium &lt;5）返回 null，调用方 fail-closed。
        /// </summary>
        public static List<RewardSlotPlan> BuildSlotPlan(
            ulong runSeed,
            int resolveCount,
            IList<ModeGRewardCandidate> candidates)
        {
            try
            {
                if (candidates == null || candidates.Count == 0) return null;

                // TypeID 升序去重（冻结输入序列）
                ModeGRewardCandidate[] sorted = SortCandidatesByTypeId(candidates);

                // 升序价格表（nearest-rank 输入）
                long[] prices = new long[sorted.Length];
                for (int i = 0; i < sorted.Length; i++) prices[i] = sorted[i].EstimatedSlotValue;
                Array.Sort(prices);
                long p75 = NearestRankValue(prices, Percentile75);
                long p95 = NearestRankValue(prices, Percentile95);

                // 分池（互斥）
                List<ModeGRewardCandidate> generalPool = new List<ModeGRewardCandidate>();
                List<ModeGRewardCandidate> premiumPool = new List<ModeGRewardCandidate>();
                for (int i = 0; i < sorted.Length; i++)
                {
                    ModeGRewardBand band = ClassifyBand(sorted[i].EstimatedSlotValue, p75, p95);
                    if (band == ModeGRewardBand.GeneralBase) generalPool.Add(sorted[i]);
                    else if (band == ModeGRewardBand.PremiumP75P95) premiumPool.Add(sorted[i]);
                    // ExtremeExcluded 不进任何槽
                }
                if (generalPool.Count < GeneralPoolMinimum) return null;
                if (premiumPool.Count < PremiumPoolMinimum) return null;

                // 确定性抽样（Reward domain，无重复）
                ulong state = ModeGDeterministicRandom.SeedDomain(runSeed,
                    ModeGDeterministicRandom.DomainConstants.Reward, string.Empty, 0);

                List<RewardSlotPlan> full = new List<RewardSlotPlan>(SlotCount);
                HashSet<int> used = new HashSet<int>();
                for (int slot = GeneralSlotBegin; slot < GeneralSlotEnd; slot++)
                {
                    ModeGRewardCandidate pick;
                    if (!TryPickDistinct(ref state, generalPool, used, out pick)) return null;
                    full.Add(new RewardSlotPlan(pick.typeId, ModeGRewardBand.GeneralBase, pick.EstimatedSlotValue));
                }
                for (int slot = PremiumSlotBegin; slot < PremiumSlotEnd; slot++)
                {
                    ModeGRewardCandidate pick;
                    if (!TryPickDistinct(ref state, premiumPool, used, out pick)) return null;
                    full.Add(new RewardSlotPlan(pick.typeId, ModeGRewardBand.PremiumP75P95, pick.EstimatedSlotValue));
                }

                // Resolve -> 件数截取（前 count 槽）
                int count = GetRewardItemCount(resolveCount);
                if (count > full.Count) count = full.Count;
                List<RewardSlotPlan> result = new List<RewardSlotPlan>(count);
                for (int i = 0; i < count; i++) result.Add(full[i]);
                return result;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] BuildSlotPlan 失败: " + e.Message);
                return null;
            }
        }

        private static bool TryPickDistinct(ref ulong state, List<ModeGRewardCandidate> pool,
            HashSet<int> used, out ModeGRewardCandidate pick)
        {
            pick = default(ModeGRewardCandidate);
            if (pool.Count == 0) return false;
            // 无偏遍历：随机起点 + 顺序探测未使用项
            int start = ModeGDeterministicRandom.NextInt(ref state, pool.Count);
            for (int step = 0; step < pool.Count; step++)
            {
                ModeGRewardCandidate candidate = pool[(start + step) % pool.Count];
                if (used.Add(candidate.typeId))
                {
                    pick = candidate;
                    return true;
                }
            }
            return false;
        }

        private static ModeGRewardCandidate[] SortCandidatesByTypeId(IList<ModeGRewardCandidate> candidates)
        {
            // TypeID 升序去重
            ModeGRewardCandidate[] arr = new ModeGRewardCandidate[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) arr[i] = candidates[i];
            Array.Sort(arr, (a, b) => a.typeId.CompareTo(b.typeId));
            int write = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (write == 0 || arr[i].typeId != arr[write - 1].typeId)
                {
                    arr[write++] = arr[i];
                }
            }
            if (write == arr.Length) return arr;
            ModeGRewardCandidate[] trimmed = new ModeGRewardCandidate[write];
            Array.Copy(arr, trimmed, write);
            return trimmed;
        }

        /// <summary>
        /// 计划总估值（long，防溢出）。
        /// </summary>
        public static long EstimateTotalValue(List<RewardSlotPlan> plan)
        {
            if (plan == null) return 0;
            long total = 0;
            for (int i = 0; i < plan.Count; i++) total += plan[i].estimatedValue;
            return total;
        }

        #endregion

        #region Nonces

        private static ulong _rewardNonce;
        private static ulong _attemptNonce;
        private static bool _noncesInitialized;
        private static readonly object _nonceLock = new object();

        /// <summary>
        /// Starting 时派生双 nonce（Reward domain）。
        /// </summary>
        public static void InitializeNonces(ulong runSeed)
        {
            lock (_nonceLock)
            {
                ulong state = ModeGDeterministicRandom.SeedDomain(runSeed,
                    ModeGDeterministicRandom.DomainConstants.Reward, "nonce", 0);
                _rewardNonce = ModeGDeterministicRandom.SplitMix64Next(ref state);
                _attemptNonce = ModeGDeterministicRandom.SplitMix64Next(ref state);
                _noncesInitialized = true;
            }
        }

        /// <summary>
        /// 失效 attempt nonce（Rewarding 死亡/host destroy 路径；幂等）。
        /// </summary>
        public static void InvalidateAttemptNonce()
        {
            lock (_nonceLock)
            {
                _attemptNonce = 0;
            }
        }

        /// <summary>
        /// 失效全部 nonce（host destroy 路径；幂等）。
        /// </summary>
        public static void InvalidateAllNonces()
        {
            lock (_nonceLock)
            {
                _rewardNonce = 0;
                _attemptNonce = 0;
                _noncesInitialized = false;
            }
        }

        private static bool AreNoncesValid()
        {
            lock (_nonceLock)
            {
                return _noncesInitialized && _rewardNonce != 0 && _attemptNonce != 0;
            }
        }

        #endregion

        #region Execute（strict，Victory only）

        private static int _relicReturnExecuted; // Interlocked CAS：信物返还幂等一次

        /// <summary>
        /// 将单件奖励提交到背包或仓库缓冲。官方 API 可能在完成写入后由事件回调抛异常，
        /// 因此异常路径必须以后验归属/缓冲计数判断是否已经提交，避免重复交付。
        /// </summary>
        internal static bool TryCommitItemToInventoryOrStorage(
            ItemStatsSystem.Item item,
            ItemStatsSystem.Inventory inventory,
            string itemLabel)
        {
            if (item == null || inventory == null) return false;

            try
            {
                if (inventory.AddAndMerge(item, 0)) return true;
            }
            catch (Exception inventoryException)
            {
                if (IsCommittedToInventory(item, inventory))
                {
                    ModBehaviour.DevLog("[ModeG] " + (itemLabel ?? "奖励")
                        + "背包回调异常，但物品已提交: " + inventoryException.Message);
                    return true;
                }

                ModBehaviour.DevLog("[ModeG] [WARNING] " + (itemLabel ?? "奖励")
                    + "写入背包失败，尝试仓库缓冲: " + inventoryException.Message);
            }

            // AddAndMerge 可能把整个堆叠合并并销毁传入实例后才由回调抛错。
            if (IsCommittedToInventory(item, inventory)) return true;

            int bufferCountBefore = GetIncomingBufferCount();
            try
            {
                ItemUtilities.SendToPlayerStorage(item, true);
                return true;
            }
            catch (Exception storageException)
            {
                // SendToPlayerStorage 的顺序是 Detach -> Buffer.Add -> DestroyTree -> callback。
                // 缓冲计数增加或实例已被销毁都表示交付已经完成，禁止再次掉落/销毁。
                if (DidStorageBufferAcceptItem(item, bufferCountBefore))
                {
                    ModBehaviour.DevLog("[ModeG] " + (itemLabel ?? "奖励")
                        + "仓库回调异常，但物品已提交: " + storageException.Message);
                    return true;
                }

                ModBehaviour.DevLog("[ModeG] [WARNING] " + (itemLabel ?? "奖励")
                    + "写入仓库缓冲失败: " + storageException.Message);
                return false;
            }
        }

        /// <summary>
        /// Mode G 关键物品的最终交付：背包/仓库均未提交时，验证地面拾取代理。
        /// </summary>
        internal static bool TryCommitItemWithGroundFallback(
            ItemStatsSystem.Item item,
            ItemStatsSystem.Inventory inventory,
            CharacterMainControl player,
            string itemLabel)
        {
            if (item == null) return false;
            if (TryCommitItemToInventoryOrStorage(item, inventory, itemLabel)) return true;
            if (player == null) return false;

            try
            {
                DuckovItemAgent pickup = item.Drop(
                    player.transform.position + UnityEngine.Vector3.up * 0.3f,
                    true, UnityEngine.Vector3.forward, 0f);
                if (pickup == null) return false;
                ModBehaviour.DevLog("[ModeG] " + (itemLabel ?? "关键物品")
                    + "已掉落在玩家脚下");
                return true;
            }
            catch (Exception dropException)
            {
                try
                {
                    if (item != null && item.ActiveAgent != null)
                    {
                        ModBehaviour.DevLog("[ModeG] " + (itemLabel ?? "关键物品")
                            + "掉落回调异常，但拾取代理已生成: " + dropException.Message);
                        return true;
                    }
                }
                catch (Exception verificationException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 地面交付归属核对失败: "
                        + verificationException.Message);
                }
                ModBehaviour.DevLog("[ModeG] [WARNING] " + (itemLabel ?? "关键物品")
                    + "地面交付失败: " + dropException.Message);
                return false;
            }
        }

        private static bool IsCommittedToInventory(
            ItemStatsSystem.Item item,
            ItemStatsSystem.Inventory inventory)
        {
            try
            {
                return item == null || item.StackCount <= 0
                    || ReferenceEquals(item.InInventory, inventory);
            }
            catch
            {
                return item == null;
            }
        }

        private static int GetIncomingBufferCount()
        {
            try
            {
                List<ItemStatsSystem.Data.ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                return buffer != null ? buffer.Count : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool DidStorageBufferAcceptItem(ItemStatsSystem.Item item, int bufferCountBefore)
        {
            try
            {
                List<ItemStatsSystem.Data.ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                if (bufferCountBefore >= 0 && buffer != null && buffer.Count > bufferCountBefore)
                    return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 仓库交付归属核对失败: " + e.Message);
            }

            try { return item == null; }
            catch { return false; }
        }

        /// <summary>
        /// 执行严格奖励事务（胜利结算）。
        /// 守卫：仅 Victory；nonce 有效；materializer 每帧最多 1 件（Jimmy 侧组件内部保证）；
        /// 胜利安全 lease 在 materializer 完成前保持 sink 隔离。
        /// 接入任务 #7 已实现的 TryStartModeGRewardMaterialization_LootAndRewards 入口，
        /// 不自建实例化循环。
        /// </summary>
        /// <param name="state">当前 run state</param>
        /// <param name="host">ModBehaviour 实例（materializer 入口宿主）</param>
        /// <param name="inventory">玩家背包</param>
        /// <param name="plan">冻结的 &lt;=10 槽计划</param>
        public static bool Execute(
            ModeGRunState state,
            ModBehaviour host,
            ItemStatsSystem.Inventory inventory,
            List<RewardSlotPlan> plan,
            Action<int, int, int> onCompleted)
        {
            try
            {
                if (state == null || host == null || inventory == null || plan == null || plan.Count == 0)
                {
                    return false;
                }
                // 严格守卫：只有 Victory 才发放奖励
                if (state.battleResult != ModeGBattleResult.Victory)
                {
                    ModBehaviour.DevLog("[ModeG] 奖励事务拒绝执行：非 Victory 状态");
                    return false;
                }
                // nonce 守卫（Rewarding 死亡已失效 attempt 时拒绝）
                if (!AreNoncesValid() || state.rewardNonceInvalidated)
                {
                    ModBehaviour.DevLog("[ModeG] 奖励事务拒绝执行：nonce 已失效");
                    return false;
                }

                // 固定 TypeID 快照
                int[] typeIds = new int[plan.Count];
                for (int i = 0; i < plan.Count; i++) typeIds[i] = plan[i].typeId;

                // 信物属于胜利必达品，先完成可靠交付再启动逐帧普通奖励。
                if (!TryReturnRelicOnce(inventory))
                {
                    ModBehaviour.DevLog("[ModeG] [ERROR] 信物可靠交付失败，奖励事务 fail-closed");
                    return false;
                }

                // 胜利安全 lease：materializer 完成前 sink 隔离不归零
                int leaseId = ModeGLateCleanupSink.AcquireLease("reward_materializer");
                int completionGate = 0;

                string failureReason;
                bool started = host.TryStartModeGRewardMaterialization_LootAndRewards(
                    typeIds,
                    inventory,
                    null,
                    (total, succeeded, failed) =>
                    {
                        if (System.Threading.Interlocked.Exchange(ref completionGate, 1) != 0) return;
                        try
                        {
                            ModeGLateCleanupSink.ReleaseLease(leaseId);
                            ModBehaviour.DevLog("[ModeG] strict materializer 完成: total=" + total
                                + " succeeded=" + succeeded + " failed=" + failed);
                        }
                        catch { /* no-throw */ }
                        try { if (onCompleted != null) onCompleted(total, succeeded, failed); }
                        catch (Exception callbackException)
                        {
                            ModBehaviour.DevLog("[ModeG] [WARNING] 奖励完成回调异常: "
                                + callbackException.Message);
                        }
                    },
                    out failureReason);

                if (!started)
                {
                    if (System.Threading.Interlocked.Exchange(ref completionGate, 1) == 0)
                        ModeGLateCleanupSink.ReleaseLease(leaseId);
                    ModBehaviour.DevLog("[ModeG] [ERROR] strict materializer 启动失败: " + (failureReason ?? "unknown"));
                    return false;
                }

                // 消费 nonce（一次性事务）
                InvalidateAllNonces();

                ModBehaviour.DevLog("[ModeG] 奖励事务已提交 strict materializer，slots=" + plan.Count);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] 奖励事务执行异常: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 幂等返还 1 枚宿命回响信物（胜利结算专属，全局一次/run）。
        /// internal：胜利已锁定但奖励事务未能启动时，ModeGDeathRouting 需在放弃分支补返，
        /// _relicReturnExecuted 的 CAS 保证与 Execute 内部调用不会双发。
        /// </summary>
        internal static bool TryReturnRelicOnce(ItemStatsSystem.Inventory inventory)
        {
            if (System.Threading.Interlocked.Exchange(ref _relicReturnExecuted, 1) != 0) return true;
            ItemStatsSystem.Item relic = null;
            try
            {
                relic = ItemStatsSystem.ItemAssetsCollection.InstantiateSync(RelicTypeId);
                if (relic == null)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 信物返还失败：InstantiateSync 返回 null");
                    System.Threading.Interlocked.Exchange(ref _relicReturnExecuted, 0);
                    return false;
                }

                if (TryCommitItemWithGroundFallback(
                    relic,
                    inventory,
                    CharacterMainControl.Main,
                    L10n.T("宿命回响信物", "Fate Echo Relic")))
                {
                    ModBehaviour.DevLog("[ModeG] 宿命回响信物已可靠返还 x1（幂等）");
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 信物返还异常: " + e.Message);
            }

            try { if (relic != null) relic.DestroyTree(); }
            catch (Exception cleanupException)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 信物返还失败后的实例清理异常: "
                    + cleanupException.Message);
            }
            System.Threading.Interlocked.Exchange(ref _relicReturnExecuted, 0);
            return false;
        }

        /// <summary>
        /// run 结束重置信物返还门（下一 run 可再次返还）。
        /// </summary>
        public static void ResetRelicReturnGate()
        {
            System.Threading.Interlocked.Exchange(ref _relicReturnExecuted, 0);
        }

        #endregion
    }
}
