using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode G 入口预览（不可变冻结，规格第五节第 2 步 / §3.2）。
    /// 首次过门控开确认页时创建：runSeed/runFormat/署名轮换/两个契约候选/scene pair 全部冻结。
    /// 取消重开不刷契约（复用同一实例）；过期 preview 不进 Starting。
    /// </summary>
    public sealed class ModeGEntryPreview
    {
        /// <summary>preview 有效期（秒）。过期不进 Starting（owner tunable：300s）。</summary>
        public const double ExpirySeconds = 300.0;

        public readonly ulong runSeed;
        public readonly long sessionCounter;
        public readonly ModeGRunFormat runFormat;
        /// <summary>署名 Boss 轮换顺序（nameKey，确定性排序）</summary>
        public readonly string[] signatureRotation;
        /// <summary>入口二选一契约候选（稳定 ID，升序；恰好 2 个）</summary>
        public readonly int[] contractCandidateIds;
        /// <summary>冻结 scene pair（exact sceneName + sceneID，来自 MapSupportRegistry Verified 记录）</summary>
        public readonly string sceneName;
        public readonly string sceneId;
        /// <summary>能力/验证 revision（冻结时快照）</summary>
        public readonly string verificationRevision;
        /// <summary>创建时间戳（UtcNow.Ticks）</summary>
        public readonly long createdTicks;

        public ModeGEntryPreview(ulong runSeed, long sessionCounter, ModeGRunFormat runFormat,
            string[] signatureRotation, int[] contractCandidateIds,
            string sceneName, string sceneId, string verificationRevision, long createdTicks)
        {
            this.runSeed = runSeed;
            this.sessionCounter = sessionCounter;
            this.runFormat = runFormat;
            this.signatureRotation = signatureRotation ?? new string[0];
            this.contractCandidateIds = contractCandidateIds ?? new int[0];
            this.sceneName = sceneName ?? string.Empty;
            this.sceneId = sceneId;
            this.verificationRevision = verificationRevision ?? string.Empty;
            this.createdTicks = createdTicks;
        }

        /// <summary>
        /// 是否仍在有效期内。过期 preview 不进 Starting。
        /// </summary>
        public bool IsFresh(long nowTicks)
        {
            double elapsedSeconds = (nowTicks - createdTicks) / (double)TimeSpan.TicksPerSecond;
            return elapsedSeconds >= 0.0 && elapsedSeconds <= ExpirySeconds;
        }
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode G 入口

        private bool modeGActive = false;
        private ModeGRuntimeModule modeGRuntime;
        private ModeGHUD modeGHUD;
        private ModeGEntryPreview modeGEntryPreview;
        private static long modeGSessionCounter = 0;
        /// <summary>确认页所选契约 ID（-1 = 默认取候选首位；启动时消费一次）</summary>
        private int modeGSelectedContractId = -1;

        /// <summary>
        /// 确认页契约选择写入（ModeGInteractable 确认时调用；启动时消费）。
        /// </summary>
        internal void SetModeGSelectedContractId(int contractId)
        {
            modeGSelectedContractId = contractId;
        }

        /// <summary>
        /// 检测玩家背包中是否存在宿命回响信物
        /// </summary>
        private Item DetectFateEchoRelic()
        {
            return FindFirstPlayerInventoryItemByTypeId(
                FateEchoRelicConfig.TYPE_ID,
                "ModeG",
                "宿命回响信物");
        }

        /// <summary>
        /// Mode G 允许玩家携带现有装备、弹药和消耗品入场。
        /// 营旗/血猎收发器由 TryStartModeG 单独检查，避免与 Mode E/F 入口冲突。
        /// </summary>
        private bool IsModeGLoadoutEligible()
        {
            return true;
        }

        private bool RefreshModeGSignatureEligibility()
        {
            try
            {
                bool descendantReady = (FindQuestionMarkPreset() != null || FindFallbackPreset() != null)
                    && ItemAssetsCollection.GetPrefab(DragonDescendantConfig.DRAGON_HELM_TYPE_ID) != null
                    && ItemAssetsCollection.GetPrefab(DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID) != null
                    && ItemAssetsCollection.GetPrefab(DragonDescendantConfig.DRAGON_BREATH_TYPE_ID) != null;

                string modPath = GetModPath();
                bool kingReady = FindDragonKingBasePreset() != null
                    && ItemAssetsCollection.GetPrefab(DragonKingConfig.DRAGON_KING_HELM_TYPE_ID) != null
                    && ItemAssetsCollection.GetPrefab(DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID) != null
                    && !string.IsNullOrEmpty(modPath)
                    && File.Exists(Path.Combine(modPath, DragonKingConfig.AssetBundlePath));

                bool witchReady = FindPhantomWitchBasePreset() != null
                    && (ItemAssetsCollection.GetPrefab(PhantomWitchConfig.ReservedScytheTypeId) != null
                        || ItemAssetsCollection.GetPrefab(PhantomWitchConfig.PlaceholderScytheTypeId) != null);

                ModeGEncounterVariation.SetSignatureEligibility(
                    ModeGEncounterVariation.ManagedDragonDescendantKey, descendantReady);
                ModeGEncounterVariation.SetSignatureEligibility(
                    ModeGEncounterVariation.ManagedDragonKingKey, kingReady);
                ModeGEncounterVariation.SetSignatureEligibility(
                    ModeGEncounterVariation.ManagedPhantomWitchKey, witchReady);
                // 单个托管 Boss 不可用时由 WavePlan 以合格官方 Boss 替换对应私有槽。
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] 署名 Boss 能力预检异常: " + e.Message);
                return false;
            }
        }

        private bool HasMinimumModeGOfficialBossPool()
        {
            ModeGBossSnapshot snapshot = CreateModeGBossSnapshot();
            return snapshot != null
                && snapshot.officialKeys.Count
                    >= ModeGOfficialBossEligibilityRegistry.MinimumProductionOfficialBossCount;
        }

        #endregion

        #region Entry Preview（不可变冻结）

        /// <summary>
        /// 创建或复用入口 preview（确认页打开时调用）。
        /// 取消重开不刷契约：有效期内复用同一冻结实例。
        /// 每次创建递增 sessionCounter 并派生新 runSeed（规格第五节第 2 步）。
        /// </summary>
        public ModeGEntryPreview GetOrCreateModeGEntryPreview()
        {
            try
            {
                // 存档订阅属于 Mod runtime 生命周期，不能随单局结束退订。
                // 先订阅再读，确保局间切槽/删档会清空对应槽缓存。
                ModeGNemesisPersistence.EnsureSubscribed();
                ModeGProfilePersistence.EnsureSubscribed();

                if (!HasMinimumModeGOfficialBossPool())
                {
                    DevLog("[ModeG] preview 创建失败：当前过滤 Boss 池没有可用的官方 Boss key");
                    return null;
                }

                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string sceneId;
                if (!ModeGMapSupportRegistry.TryGetVerifiedPairForScene(sceneName, out sceneId))
                {
                    DevLog("[ModeG] preview 创建失败：当前场景不在地图选择 UI 的有效配置中");
                    return null;
                }

                long nowTicks = DateTime.UtcNow.Ticks;
                if (modeGEntryPreview != null
                    && modeGEntryPreview.IsFresh(nowTicks)
                    && string.Equals(modeGEntryPreview.sceneName, sceneName, StringComparison.Ordinal)
                    && string.Equals(modeGEntryPreview.sceneId, sceneId, StringComparison.Ordinal)
                    && string.Equals(modeGEntryPreview.verificationRevision,
                        ModeGAvailability.CurrentVerificationRevision, StringComparison.Ordinal))
                {
                    return modeGEntryPreview;
                }

                if (!RefreshModeGSignatureEligibility())
                {
                    DevLog("[ModeG] preview 创建失败：署名 Boss 能力预检未通过");
                    return null;
                }

                modeGSessionCounter++;
                ulong runSeed = ModeGDeterministicRandom.DeriveRunSeed(modeGSessionCounter);

                // runFormat：有胜利记录 -> RematchMix，否则首胜叙事
                ModeGRunFormat format = ModeGProfilePersistence.HasAnyVictory()
                    ? ModeGRunFormat.RematchMix
                    : ModeGRunFormat.FirstClearNarrative;

                // 署名轮换：eligible 署名 Boss 的稳定排序快照
                string[] signatureRotation = ModeGEncounterVariation.GetEligibleSignatureKeys();

                // 契约二选一候选（确定性，按 Contract domain 派生）
                int[] candidates = ModeGFateContract.SelectEntryCandidatePair(
                    runSeed, ModeGProfilePersistence.GetLastSelectedContractId());

                modeGEntryPreview = new ModeGEntryPreview(
                    runSeed,
                    modeGSessionCounter,
                    format,
                    signatureRotation,
                    candidates,
                    sceneName,
                    sceneId,
                    ModeGAvailability.CurrentVerificationRevision,
                    nowTicks);
                return modeGEntryPreview;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] GetOrCreateModeGEntryPreview 失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 启动成功后消费 preview（防止过期复用）。
        /// </summary>
        private void ConsumeModeGEntryPreview()
        {
            modeGEntryPreview = null;
        }

        internal bool IsModeGEntryPreviewValidForCurrentScene(ModeGEntryPreview preview)
        {
            if (preview == null || !preview.IsFresh(DateTime.UtcNow.Ticks)) return false;
            string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return string.Equals(activeScene, preview.sceneName, StringComparison.Ordinal)
                && string.Equals(preview.verificationRevision,
                    ModeGAvailability.CurrentVerificationRevision, StringComparison.Ordinal)
                && ModeGMapSupportRegistry.IsSupported(
                    preview.sceneName, preview.sceneId, preview.verificationRevision);
        }

        #endregion

        #region TryStartModeG

        /// <summary>
        /// 检测并尝试启动 Mode G（船票 + 500057 信物；玩家可携带自己的装备）。
        /// 不调用 IsAnyBossRushLikeModeActive()（含 IsBossRushArenaActive 会死锁，规格 §11 685 行），
        /// 分别拒绝 IsActive/ModeD/ModeE/ModeF/ZombieMode/IsModeGEntryBlocked。
        /// </summary>
        public bool TryStartModeG()
        {
            // Mode H 真实资产风险门（加法分支，设计提案 §24.3）：
            // 只在存在未终结真实资产事务或风险未知时拒绝；no-throw，
            // 新档/无 journal 时同步 ready 且不阻断，旧模式行为逐字不变。
            try
            {
                if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
                {
                    // 被拒的成因有两种：扫描本身失败（可自愈）与确有未结算押品。
                    // 先给一次重试机会，再按真实成因取文案，别把读档出错说成「你有笔账没结」。
                    ShowMessage(L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey()));
                    DevLog("[BossRush] 入口被 Mode H 真实资产风险门拒绝");
                    return false;
                }
            }
            catch
            {
                // 门查询本身 no-throw；异常只表示未能判定，放行旧模式既有流程
            }

            bool relicConsumed = false;
            bool ticketConsumed = false;
            try
            {
                if (modeGActive)
                {
                    DevLog("[ModeG] Mode G 已在运行，忽略重复启动请求");
                    return false;
                }

                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                {
                    DevLog("[ModeG] 发布闸关闭，拒绝启动且不消费入场物品");
                    return false;
                }
                if (!HasMinimumModeGOfficialBossPool())
                {
                    DevLog("[ModeG] 当前过滤 Boss 池没有可用的官方 Boss key，拒绝启动且不消费入场物品");
                    return false;
                }
                if (!ModeGPresentationAssetCache.TryPreflight())
                {
                    DevLog("[ModeG] 展示资源预检失败，拒绝启动且不消费入场物品");
                    return false;
                }

                // 分别拒绝各模式（禁止走聚合判定）
                if (IsActive)
                {
                    DevLog("[ModeG] Legacy BossRush 激活中，拒绝 Mode G 启动");
                    return false;
                }
                if (modeDActive || modeEActive || modeFActive)
                {
                    DevLog("[ModeG] Mode D/E/F 已激活，拒绝 Mode G 启动");
                    return false;
                }
                if (IsZombieModeActive)
                {
                    DevLog("[ModeG] ZombieMode 激活中，拒绝 Mode G 启动");
                    return false;
                }
                if (ModeGRuntimeGates.IsModeGEntryBlocked)
                {
                    DevLog("[ModeG] Mode G 入口被隔离（run 进行中或 late sink 未归零），拒绝启动");
                    return false;
                }

                if (ModeGNemesisPersistence.IsStoreFaulted
                    || ModeGProfilePersistence.IsStoreFaulted
                    || ModeGPersistenceFlushCoordinator.IsFaulted)
                {
                    DevLog("[ModeG] 持久化写屏障已故障，本 runtime fail-closed 拒绝启动");
                    ShowMessage(L10n.T(
                        "宿命回响存档写入发生故障，请重启游戏后再试。你的物品不会被消耗。",
                        "Fate Echo save persistence faulted. Restart the game and try again. Your items were not consumed."));
                    return false;
                }

                Item ticket = DetectBossRushTicketItem();
                bool ticketPrepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
                Item relic = DetectFateEchoRelic();
                if ((ticket == null && !ticketPrepaid) || relic == null)
                {
                    DevLog("[ModeG] 未检测到船票或宿命回响信物，不启动 Mode G");
                    return false;
                }

                var (faction, flagItem) = DetectFactionFlag();
                if (faction.HasValue || flagItem != null)
                {
                    DevLog("[ModeG] 检测到营旗，按优先级不进入 Mode G");
                    return false;
                }

                Item transponder = DetectBloodhuntTransponder();
                if (transponder != null)
                {
                    DevLog("[ModeG] 检测到血猎收发器，Mode F 优先级更高，不进入 Mode G");
                    return false;
                }

                if (!IsModeGLoadoutEligible())
                {
                    DevLog("[ModeG] 玩家装备状态不满足 Mode G 入场条件，拒绝启动");
                    return false;
                }

                // golden vectors 自检（fail-closed）
                if (!ModeGDeterministicRandom.ValidateGoldenVectors())
                {
                    DevLog("[ModeG] [ERROR] golden vectors 自检失败，fail-closed 拒绝启动");
                    return false;
                }

                // 武器计分兼容矩阵验证（规格 §20 第 9 条；revision 过期/普通攻击预期不符 fail-closed）
                string weaponMatrixFailReason;
                if (!ModeGWeaponScoringCompatibilityMatrix.IsMatrixValid(out weaponMatrixFailReason))
                {
                    DevLog("[ModeG] [ERROR] 武器计分兼容矩阵验证失败，fail-closed 拒绝启动: " + weaponMatrixFailReason);
                    return false;
                }

                if (!RefreshModeGSignatureEligibility())
                {
                    DevLog("[ModeG] 署名 Boss 资源/adapter 能力预检失败，fail-closed 拒绝启动");
                    ShowMessage(L10n.T(
                        "宿命回响资源未完整加载，请返回基地后重试。",
                        "Fate Echo resources are incomplete. Return to base and try again."));
                    return false;
                }

                // Automatic arena entry mirrors Mode F. Freeze the preview here when the
                // player did not open the optional information interaction first.
                // 不用 `modeGEntryPreview ?? GetOrCreate()`：字段里若是过期 preview，短路会跳过
                // GetOrCreate 自带的「过期即重建」，玩家挂机超时后点迎战只能拿到静默失败。
                ModeGEntryPreview cachedPreview = modeGEntryPreview;
                ModeGEntryPreview preview = GetOrCreateModeGEntryPreview();
                if (!IsModeGEntryPreviewValidForCurrentScene(preview))
                {
                    DevLog("[ModeG] preview 缺失、过期或与当前 verified scene pair/revision 不一致");
                    // 与上面同款：场内无可达入口，指向撤离重进而不是「重新打开入口」。
                    ShowMessage(L10n.T(
                        "宿命回响预览已失效，请撤离返回基地后重新入场。",
                        "Fate Echo preview is no longer valid. Extract to base, then re-enter."));
                    return false;
                }
                if (cachedPreview != null && !ReferenceEquals(cachedPreview, preview))
                {
                    // 过期重建：契约候选与 runSeed 已换新，确认页上的旧选择随之作废。
                    // 不静默沿用（玩家会拿到从未见过的契约）。
                    modeGSelectedContractId = -1;
                    // 文案只能指向真实可达的操作：场内没有 Mode G 交互物（路牌不注入该
                    // 选项），auto 确认页只在进图协程里开一次且协程已退出，玩家唯一出路
                    // 是撤离回基地重新入场——不要再写「重新打开确认页」。
                    ShowMessage(L10n.T(
                        "宿命契约候选已刷新，本次入场取消；请撤离返回基地后重新携带船票与信物入场。",
                        "Fate contract candidates refreshed - entry cancelled. Extract to base, then re-enter with a ticket and relic."));
                    return false;
                }

                relicConsumed = TryConsumeModeEntryItem(relic, "ModeG", "宿命回响信物");
                if (!relicConsumed) return false;

                ticketConsumed = ticketPrepaid || TryConsumeModeEntryItem(ticket, "ModeG", "船票");
                if (!ticketConsumed)
                {
                    TryRefundModeGEntryItem(FateEchoRelicConfig.TYPE_ID, L10n.T("宿命回响信物", "Fate Echo Relic"));
                    relicConsumed = false;
                    return false;
                }
                if (ticketPrepaid)
                {
                    // Mode G now owns the UI-prepaid ticket; startup failure refunds it below.
                    BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                }

                bool startupRefundOwnedByRuntime;
                bool started = StartModeGRuntime(
                    preview,
                    refundTicketOnStartupFailure: ticketConsumed,
                    refundRelicOnStartupFailure: relicConsumed,
                    startupRefundOwnedByRuntime: out startupRefundOwnedByRuntime);
                if (!started && !startupRefundOwnedByRuntime)
                {
                    if (ticketConsumed)
                    {
                        TryRefundModeGEntryItem(GetBossRushTicketTypeId(), L10n.T("船票", "Boss Rush Ticket"));
                    }
                    if (relicConsumed)
                    {
                        TryRefundModeGEntryItem(FateEchoRelicConfig.TYPE_ID, L10n.T("宿命回响信物", "Fate Echo Relic"));
                    }
                    ticketConsumed = false;
                    relicConsumed = false;
                }
                return started;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] TryStartModeG 失败: " + e.Message);
                if (ticketConsumed) TryRefundModeGEntryItem(GetBossRushTicketTypeId(), L10n.T("船票", "Boss Rush Ticket"));
                if (relicConsumed) TryRefundModeGEntryItem(FateEchoRelicConfig.TYPE_ID, L10n.T("宿命回响信物", "Fate Echo Relic"));
                return false;
            }
        }

        private bool TryRefundModeGEntryItem(int typeId, string displayName)
        {
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                CharacterMainControl player = CharacterMainControl.Main;
                Inventory inventory = player != null && player.CharacterItem != null
                    ? player.CharacterItem.Inventory
                    : null;
                if (item != null && ModeGRewardTransaction.TryCommitItemWithGroundFallback(
                    item, inventory, player, displayName))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] 入场道具返还异常 typeId=" + typeId + ": " + e.Message);
            }

            try { if (item != null) item.DestroyTree(); }
            catch (Exception cleanupException)
            {
                DevLog("[ModeG] [WARNING] 返还失败后的物品清理异常: " + cleanupException.Message);
            }
            return false;
        }

        internal bool TryRefundModeGPendingPrepaidTicket()
        {
            bool wasPrepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
            // Always clear the direct/map-selection marker. Clear ownership before refunding so
            // repeated cancel/failure callbacks cannot duplicate a prepaid ticket.
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            RollbackModeGStagedArenaEntry();
            if (!wasPrepaid) return false;
            return TryRefundModeGEntryItem(
                GetBossRushTicketTypeId(),
                L10n.T("船票", "Boss Rush Ticket"));
        }

        internal void RollbackModeGStagedArenaEntry()
        {
            if (modeGActive || IsActive || modeDActive || modeEActive || modeFActive || IsZombieModeActive)
            {
                return;
            }

            // OnSceneLoaded marks the arena active before the deferred Mode G confirmation.
            // Cancellation/preflight failure must release only that staging ownership.
            bossRushArenaActive = false;
            bossRushArenaPlanned = false;
            spawnersDisabled = false;
        }

        internal bool TryRefundModeGStartupItem(int typeId, string displayName)
        {
            return TryRefundModeGEntryItem(typeId, displayName);
        }

        internal int GetModeGTicketTypeId()
        {
            return GetBossRushTicketTypeId();
        }

        /// <summary>
        /// 启动 Mode G 运行时（preview 冻结值驱动，Starting 阶段）。
        /// </summary>
        private bool StartModeGRuntime(ModeGEntryPreview preview,
            bool refundTicketOnStartupFailure, bool refundRelicOnStartupFailure,
            out bool startupRefundOwnedByRuntime)
        {
            startupRefundOwnedByRuntime = false;
            ModeGRunState state = null;
            try
            {
                string playerGuid = GetPlayerGuid();
                long timestamp = DateTime.UtcNow.Ticks;

                // runId：runSeed 与时间戳混合（确定性，不用 GetHashCode）
                ulong runId;
                unchecked
                {
                    ulong idState = preview.runSeed ^ (ulong)timestamp;
                    runId = ModeGDeterministicRandom.SplitMix64Next(ref idState);
                }

                state = new ModeGRunState(runId, preview.runSeed, playerGuid, timestamp, preview.sessionCounter);

                // 契约：确认页所选 > 候选首位 > 默认 TriadBreaker（选择仅限冻结候选对）
                int contractId = ModeGFateContract.IdTriadBreaker;
                if (preview.contractCandidateIds.Length > 0)
                {
                    contractId = preview.contractCandidateIds[0];
                    for (int i = 0; i < preview.contractCandidateIds.Length; i++)
                    {
                        if (preview.contractCandidateIds[i] == modeGSelectedContractId)
                        {
                            contractId = modeGSelectedContractId;
                            break;
                        }
                    }
                }
                state.fateContractId = contractId;
                modeGSelectedContractId = -1; // 消费一次

                // 创建 RuntimeModule（Starting 阶段一次性构建 Boss 池快照/预分配缓存）
                modeGRuntime = new ModeGRuntimeModule();
                if (!modeGRuntime.Initialize(state, preview))
                {
                    DevLog("[ModeG] RuntimeModule 初始化失败（fail-closed）");
                    modeGRuntime = null;
                    return false;
                }
                // None -> Starting（CAS 一次）
                if (!state.TryAdvanceLifecycle(ModeGLifecyclePhase.Starting))
                {
                    DevLog("[ModeG] [ERROR] lifecycle Starting 推进失败");
                    modeGRuntime.Dispose();
                    modeGRuntime = null;
                    return false;
                }

                // 绑定运行上下文（门控 API 从此可见 run 进行中）
                ModeGRunContext.Bind(state, modeGRuntime);

                // 启动
                modeGRuntime.ArmStartupRefund(
                    refundTicketOnStartupFailure, refundRelicOnStartupFailure);
                startupRefundOwnedByRuntime = true;
                if (!modeGRuntime.StartRun())
                {
                    DevLog("[ModeG] RuntimeModule 启动失败（fail-closed）");
                    modeGRuntime.End(ModeGExitReason.TechnicalIntegrityLoss);
                    ModeGRunContext.Unbind(state);
                    modeGRuntime.Dispose();
                    modeGRuntime = null;
                    return false;
                }

                if (!ModeGProfilePersistence.RecordSelectedContract(contractId))
                {
                    DevLog("[ModeG] [WARNING] 上一局契约选择未能持久化，下一局防重复可能不可用");
                }

                // 创建 HUD（刷新上限 4Hz 由 HUD 内部节流）
                modeGHUD = new ModeGHUD(modeGRuntime);

                modeGActive = true;
                ConsumeModeGEntryPreview();

                ShowBigBanner(L10n.T(
                    "<color=#B8860B>宿命回响</color> 已启动",
                    "<color=#B8860B>Fate Echo</color> Started"
                ));

                // 发现性：放弃入口只有快捷键，开局告知一次当前绑定键
                try
                {
                    int abandonKey = config != null ? config.modeGAbandonHotkey : 0;
                    if (abandonKey > 0)
                    {
                        string keyName = ((UnityEngine.KeyCode)abandonKey).ToString();
                        ShowMessage(L10n.T(
                            "按 [" + keyName + "] 可打开放弃挑战确认页。",
                            "Press [" + keyName + "] to open the abandon confirmation."));
                    }
                }
                catch { /* 提示失败不影响开局 */ }

                DevLog("[ModeG] Mode G 启动成功 runId=" + runId.ToString("x"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] StartModeGRuntime 异常: " + e.Message);
                if (startupRefundOwnedByRuntime && modeGRuntime != null)
                {
                    try { modeGRuntime.End(ModeGExitReason.TechnicalIntegrityLoss); }
                    catch (Exception endException)
                    {
                        DevLog("[ModeG] [WARNING] 启动失败后的 Runtime 终止异常: " + endException.Message);
                    }
                }
                if (state != null) ModeGRunContext.Unbind(state);
                ShutdownModeG();
                return false;
            }
        }

        /// <summary>
        /// 每帧更新 Mode G（ModeRuntimeHooks.UpdateModeG 兼容入口，内部委托给 module）。
        /// </summary>
        private void UpdateModeG(float deltaTime)
        {
            if (!modeGActive || modeGRuntime == null) return;

            try
            {
                TryHandleModeGAbandonHotkey();
                modeGRuntime.Update(deltaTime);
                if (modeGHUD != null) modeGHUD.Update(deltaTime);

                // 检查是否已结束
                ModeGRunState state = modeGRuntime.State;
                if (state != null && state.IsTerminal)
                {
                    DevLog("[ModeG] 检测到终态，准备关闭");
                    ShutdownModeG();
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeG] UpdateModeG 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 放弃挑战快捷键轮询（每帧一次 bool 链 + 一次 GetKeyDown，零分配）。
        /// 双闸：只在战斗进行中受理（Starting/Rewarding/终局不受理，防止弃奖误触），
        /// 且已有模态（含本确认页自身）时不叠开。
        /// </summary>
        private void TryHandleModeGAbandonHotkey()
        {
            try
            {
                if (modeGRuntime == null) return;
                ModeGRunState state = modeGRuntime.State;
                if (state == null || !state.IsActive) return;
                if (ModeGAbandonPresenter.IsOpen) return;
                if (ZombieModeUIHelper.IsModalInputPaused) return;

                int keyCode = config != null ? config.modeGAbandonHotkey : 0;
                if (keyCode <= 0) return;
                if (!UnityEngine.Input.GetKeyDown((UnityEngine.KeyCode)keyCode)) return;

                ModeGAbandonPresenter.TryOpen(modeGRuntime);
            }
            catch (Exception e)
            {
                DevLog("[ModeG] 放弃快捷键处理异常: " + e.Message);
            }
        }

        /// <summary>
        /// 关闭 Mode G（本地清理；九种终局的完整幂等 End 在 ModeGCleanupController）。
        /// </summary>
        private void ShutdownModeG()
        {
            try
            {
                ModeGInteractable.CloseActiveConfirmation();
                ModeGAbandonPresenter.CloseIfOpen();
                ModeGRunState state = modeGRuntime != null ? modeGRuntime.State : null;

                if (modeGHUD != null)
                {
                    modeGHUD.Dispose();
                    modeGHUD = null;
                }

                if (modeGRuntime != null)
                {
                    modeGRuntime.Dispose();
                    modeGRuntime = null;
                }

                if (state != null)
                {
                    ModeGRunContext.Unbind(state);
                }

                modeGActive = false;
                DevLog("[ModeG] Mode G 已关闭");
            }
            catch (Exception e)
            {
                DevLog("[ModeG] ShutdownModeG 异常: " + e.Message);
                modeGActive = false;
                modeGRuntime = null;
                modeGHUD = null;
            }
        }

        #endregion

        #region Player Guid（真实来源，禁硬编码）

        private static string modeGPlayerGuidCache;
        private static MethodInfo modeGGetSteamIdMethod;
        private static bool modeGSteamIdLookupFailed;

        /// <summary>
        /// 玩家 GUID：优先 Steam ID64（反射 Steamworks.SteamUser.GetSteamID().m_SteamID），
        /// 回退 Steam 人格名 FNV 哈希，最终回退本地固定标识。缓存一次。
        /// </summary>
        private string GetPlayerGuid()
        {
            if (!string.IsNullOrEmpty(modeGPlayerGuidCache)) return modeGPlayerGuidCache;
            try
            {
                // 1. Steam ID64（真实玩家来源）
                if (!modeGSteamIdLookupFailed)
                {
                    try
                    {
                        if (modeGGetSteamIdMethod == null)
                        {
                            Type steamUserType = AccessTools.TypeByName("Steamworks.SteamUser");
                            if (steamUserType != null)
                            {
                                modeGGetSteamIdMethod = steamUserType.GetMethod(
                                    "GetSteamID",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            }
                        }
                        if (modeGGetSteamIdMethod != null)
                        {
                            object cSteamId = modeGGetSteamIdMethod.Invoke(null, null);
                            if (cSteamId != null)
                            {
                                FieldInfo idField = cSteamId.GetType().GetField("m_SteamID",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (idField != null)
                                {
                                    object raw = idField.GetValue(cSteamId);
                                    if (raw is ulong)
                                    {
                                        ulong steamId64 = (ulong)raw;
                                        if (steamId64 != 0)
                                        {
                                            modeGPlayerGuidCache = "steam_" + steamId64.ToString();
                                            return modeGPlayerGuidCache;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            modeGSteamIdLookupFailed = true;
                        }
                    }
                    catch
                    {
                        modeGSteamIdLookupFailed = true;
                    }
                }

                // 2. 回退：Steam 人格名 FNV 哈希（稳定标识）
                string persona = TryGetSteamPersonaName();
                if (!string.IsNullOrEmpty(persona))
                {
                    modeGPlayerGuidCache = "persona_" + ModeGDeterministicRandom.Fnv1a64(persona).ToString("x");
                    return modeGPlayerGuidCache;
                }

                // 3. 最终回退：本地固定标识（同机同档稳定）
                modeGPlayerGuidCache = "local_player";
                return modeGPlayerGuidCache;
            }
            catch
            {
                return "local_player";
            }
        }

        #endregion
    }

    /// <summary>
    /// Mode G 静态入口（跨任务契约，任务 #7/#8）。
    /// 调用点唯一：ModBehaviour.OnDestroy 最前段（Jimmy 接线）。
    /// </summary>
    public static class ModeG
    {
        private static int _prepareHostDestroyState; // 0=未执行 1=已执行（Interlocked CAS 幂等）

        /// <summary>
        /// 宿主销毁准备（幂等 no-throw）。顺序（规格 §11 #20）：
        /// 1. token CAS 同步消费未 settled 成就 report；
        /// 2. 递增 run/epoch 语义（解绑当前 run 上下文）；
        /// 3. 退订事件（四事件按 owner 精确退订，委托 CleanupController）；
        /// 4. 失效 spawn lease 与 reward nonce；
        /// 5. strict reward cleanup；
        /// 6. 按 handle 移交已提交对象；
        /// 7. 未 settled factory continuation 转 ModeGLateCleanupSink。
        /// 无 Mode G run/report/lease 时 O(1) 早返。
        /// </summary>
        public static void PrepareHostDestroy()
        {
            try
            {
                if (System.Threading.Interlocked.Exchange(ref _prepareHostDestroyState, 1) != 0)
                {
                    return; // 幂等：已执行过
                }

                ModeGRunState state = ModeGRunContext.Current;
                // 即使没有 active run/late lease，也要先尽力提交 pending，再释放存档订阅。
                try { ModeGPersistenceFlushCoordinator.TryFlushOnHostDestroy(); }
                catch (Exception flushException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 宿主销毁最终存档提交异常: " + flushException.Message);
                }
                try { ModeGNemesisPersistence.ShutdownSubscription(); }
                catch (Exception nemesisException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 宿敌存档退订异常: " + nemesisException.Message);
                }
                try { ModeGProfilePersistence.ShutdownSubscription(); }
                catch (Exception profileException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 档案存档退订异常: " + profileException.Message);
                }
                if (state == null && !ModeGLateCleanupSink.HasPendingLeases)
                {
                    return; // O(1) 早返：无 run 无 lease
                }

                if (state != null)
                {
                    // 1. 消费未 settled 成就 report（正常终局已在 End 内消费，此处是宿主销毁兜底；
                    //    drain 幂等，队列为空时 O(1) 返回）
                    try
                    {
                        ModeGCombatTelemetry.ConsumePendingAchievementReports(state);
                    }
                    catch { /* no-throw 契约 */ }

                    // 4. 失效 spawn lease 与 reward nonce
                    state.spawnLeasesInvalidated = true;
                    state.rewardNonceInvalidated = true;

                    // 3/5/6/7. 委托清理控制器（退订/strict reward cleanup/handle 移交/sink 转移）
                    try
                    {
                        ModeGCleanupController.PrepareHostDestroyInternal(state);
                    }
                    catch { /* no-throw 契约 */ }

                    // 2. 解绑上下文（本地 None，sink 隔离由 HasPendingLeases 独立维持）
                    ModeGRunContext.Unbind(state);
                }
                else
                {
                    // 仅有 sink pending lease：交给 sink 自行 drain
                    try
                    {
                        ModeGCleanupController.PrepareHostDestroyInternal(null);
                    }
                    catch { /* no-throw 契约 */ }
                }
            }
            catch
            {
                // no-throw 契约：吞掉一切异常
            }
            finally
            {
                // 允许下次 host 重建后再次执行（新进程/新 host 语义）
                System.Threading.Interlocked.Exchange(ref _prepareHostDestroyState, 0);
            }
        }
    }
}
