using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class ModeFRuntimeModule
    {
        #region Mode F 入口

        /// <summary>Mode F 会话序号，防止上一局的异步对象晚到并污染新局。</summary>
        private int modeFSessionSerial = 0;
        internal int CurrentModeFSessionToken { get { return modeFState.RuntimeSessionToken; } }

        private int BeginModeFSession()
        {
            modeFState.RuntimeSessionToken = ++modeFSessionSerial;
            return modeFState.RuntimeSessionToken;
        }

        private void InvalidateModeFSession()
        {
            modeFSessionSerial++;
            modeFState.RuntimeSessionToken = 0;
        }

        internal bool IsModeFSessionStillValid(int sessionToken, int relatedScene)
        {
            if (sessionToken <= 0)
            {
                return false;
            }

            return modeFActive &&
                   modeFState.IsActive &&
                   modeFState.RuntimeSessionToken == sessionToken &&
                   UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == relatedScene;
        }

        /// <summary>
        /// 检测玩家背包中是否存在血猎收发器
        /// </summary>
        internal Item DetectBloodhuntTransponder()
        {
            return ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(
                BloodhuntTransponderConfig.TYPE_ID,
                "ModeF",
                "血猎收发器");
        }

        internal Item DetectBossRushTicketItem()
        {
            return ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(getTicketTypeId());
        }

        internal bool IsPlayerNakedForModeF()
        {
            return ModeEntryInventory.IsPlayerNakedWithAllowedItems(
                "ModeF",
                getTicketTypeId(),
                BloodhuntTransponderConfig.TYPE_ID,
                false);
        }

        /// <summary>
        /// 检测并尝试启动 Mode F
        /// 条件：裸装 + 船票 + 血猎收发器 + 无营旗
        /// </summary>
        public bool TryStartModeF()
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
                    owner.ShowMessage(L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey()));
                    ModBehaviour.DevLog("[BossRush] 入口被 Mode H 真实资产风险门拒绝");
                    return false;
                }
            }
            catch
            {
                // 门查询本身 no-throw；异常只表示未能判定，放行旧模式既有流程
            }

            bool transponderConsumed = false;
            bool ticketConsumed = false;
            try
            {
                if (modeFActive || modeFState.IsActive)
                {
                    ModBehaviour.DevLog("[ModeF] Mode F 已在运行，忽略重复启动请求");
                    return false;
                }

                if (modeD.IsActive || modeE.IsModeEActive)
                {
                    ModBehaviour.DevLog("[ModeF] Mode D 或 Mode E 已激活，跳过 Mode F 启动");
                    return false;
                }

                Item ticket = DetectBossRushTicketItem();
                bool ticketPrepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
                Item transponder = DetectBloodhuntTransponder();
                if ((ticket == null && !ticketPrepaid) || transponder == null)
                {
                    ModBehaviour.DevLog("[ModeF] 未检测到船票或血猎收发器，不启动 Mode F");
                    return false;
                }

                var (faction, flagItem) = modeE.DetectFactionFlag();
                if (faction.HasValue || flagItem != null)
                {
                    ModBehaviour.DevLog("[ModeF] 检测到营旗，按优先级不进入 Mode F");
                    return false;
                }

                if (!IsPlayerNakedForModeF())
                {
                    ModBehaviour.DevLog("[ModeF] 玩家不满足裸装条件，拒绝启动");
                    owner.ShowMessage(L10n.T(
                        "血猎追击模式需要裸装入场！请清空所有装备后重试。",
                        "Bloodhunt mode requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                // 原子性扣费：任一失败则退还已消耗的道具并中止
                transponderConsumed = modeE.TryConsumeModeEntryItem(transponder, "ModeF", "血猎收发器");
                if (!transponderConsumed)
                {
                    owner.ShowMessage(L10n.T(
                        "血猎追击模式启动失败：无法消耗血猎收发器。",
                        "Bloodhunt start failed: unable to consume the Bloodhunt Transponder."
                    ));
                    return false;
                }

                ticketConsumed = ticketPrepaid || modeE.TryConsumeModeEntryItem(ticket, "ModeF", "船票");
                if (!ticketConsumed)
                {
                    // 退还已消耗的收发器
                    RefundModeFStartupEntryItems(false, transponderConsumed);
                    transponderConsumed = false;
                    owner.ShowMessage(L10n.T(
                        "血猎追击模式启动失败：无法消耗船票，已退还血猎收发器。",
                        "Bloodhunt start failed: unable to consume the ticket. Transponder refunded."
                    ));
                    return false;
                }

                bool started = StartModeF();
                if (!started)
                {
                    RefundModeFStartupEntryItems(ticketConsumed, transponderConsumed);
                    owner.ShowMessage(L10n.T(
                        "血猎追击模式启动失败，已返还入场道具。",
                        "Bloodhunt start failed. Entry items were refunded."
                    ));
                }
                else
                {
                    FinalizeModeFStartupEntryConsumption(
                        ticket,
                        ticketPrepaid,
                        ref ticketConsumed,
                        transponder,
                        ref transponderConsumed);
                }

                return started;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] TryStartModeF 失败: " + e.Message);
                RefundModeFStartupEntryItems(ticketConsumed, transponderConsumed);
                return false;
            }
        }

        private void FinalizeModeFStartupEntryConsumption(
            Item ticket,
            bool ticketPrepaid,
            ref bool ticketConsumed,
            Item transponder,
            ref bool transponderConsumed)
        {
            if (!ticketPrepaid && !ticketConsumed)
            {
                ticketConsumed = TryFinalizeModeFStartupEntryItemConsumption(
                    ticket,
                    DetectBossRushTicketItem,
                    "船票");
            }

            if (!transponderConsumed)
            {
                transponderConsumed = TryFinalizeModeFStartupEntryItemConsumption(
                    transponder,
                    DetectBloodhuntTransponder,
                    "血猎收发器");
            }
        }

        private bool TryFinalizeModeFStartupEntryItemConsumption(
            Item originalItem,
            Func<Item> fallbackFinder,
            string itemLabel)
        {
            if (modeE.TryConsumeModeEntryItem(originalItem, "ModeF", itemLabel))
            {
                ModBehaviour.DevLog("[ModeF] 启动成功后已补偿消耗" + itemLabel);
                return true;
            }

            Item fallbackItem = null;
            try
            {
                if (fallbackFinder != null)
                {
                    fallbackItem = fallbackFinder();
                }
            }
            catch { }

            if (fallbackItem != null && !object.ReferenceEquals(fallbackItem, originalItem))
            {
                if (modeE.TryConsumeModeEntryItem(fallbackItem, "ModeF", itemLabel))
                {
                    ModBehaviour.DevLog("[ModeF] 启动成功后已通过重新检索补偿消耗" + itemLabel);
                    return true;
                }
            }

            ModBehaviour.DevLog("[ModeF] [WARNING] 启动成功，但未能补偿消耗" + itemLabel + "，请留意背包状态");
            return false;
        }

        private void RefundModeFStartupEntryItems(bool refundTicket, bool refundTransponder)
        {
            bool attemptedRefund = false;
            bool refundedAny = false;

            if (refundTicket)
            {
                attemptedRefund = true;
                refundedAny |= TryRefundModeFStartupEntryItem(
                    getTicketTypeId(),
                    L10n.T("船票", "Boss Rush Ticket"));
            }

            if (refundTransponder)
            {
                attemptedRefund = true;
                refundedAny |= TryRefundModeFStartupEntryItem(
                    BloodhuntTransponderConfig.TYPE_ID,
                    L10n.T("血猎收发器", "Bloodhunt Transponder"));
            }

            if (attemptedRefund && !refundedAny)
            {
                ModBehaviour.DevLog("[ModeF] [WARNING] 启动失败后的入场道具返还未成功，请检查背包与地面掉落。");
            }
        }

        private bool TryRefundModeFStartupEntryItem(int typeId, string displayName)
        {
            bool refunded = TryGiveItemToPlayerOrDrop(typeId, displayName, false);
            if (!refunded)
            {
                ModBehaviour.DevLog("[ModeF] [WARNING] 返还入场道具失败: typeId=" + typeId + ", displayName=" + displayName);
            }

            return refunded;
        }

        /// <summary>
        /// 启动 Mode F 模式
        /// </summary>
        internal bool StartModeF()
        {
            ModeEFSpawnProfiler profiler = new ModeEFSpawnProfiler("StartModeF");
            try
            {
                if (modeFActive || modeFState.IsActive)
                {
                    ModBehaviour.DevLog("[ModeF] [WARNING] StartModeF 在模式已激活时被重复调用，已忽略");
                    profiler.Complete("skipped: already active");
                    return false;
                }

                ModBehaviour.DevLog("[ModeF] 启动 Mode F 血猎追击模式");

                // 清理可能从无间炼狱残留的状态，避免 InfiniteHellCashMagnet/UI 提示误激活
                owner.ResetArenaForModeD();

                modeFActive = true;
                modeFState.Reset();
                modeFActiveBossSet.Clear();
                ClearModeFBossRegenCache();
                modeFState.IsActive = true;
                int modeFSessionToken = BeginModeFSession();
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                owner.ClearModeDEnemyRecoveryState();
                PrepareModeESharedRuntimeForModeF();
                profiler.Mark("ResetState");

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                modeE.EnsureModeEFSpawnPoolsReady("StartModeF");
                equipment.EnsureModeDGlobalItemPool();
                profiler.Mark("WarmPools");

                // 订阅龙息Buff处理器
                DragonBreathBuffHandler.Subscribe();
                profiler.Mark("SubscribeDragonBreath");

                // 分配刷怪点（复用 Mode E 逻辑）
                spawnPreparation.PreCacheMapSpawnerPositions();
                spawnPreparation.AllocateSpawnPoints();
                profiler.Mark("AllocateSpawnPoints");

                // 传送玩家到安全位置
                spawnPreparation.TeleportPlayerToSafePosition();
                profiler.Mark("TeleportPlayer");

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                equipment.GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备
                modeE.ModeEGiveColdWeatherGear();
                profiler.Mark("GiveLoadout");

                owner.CaptureModeEFLootboxBaseline();
                profiler.Mark("CaptureLootboxBaseline");

                // 额外发放折叠掩体包 x1（背包满时掉在脚下，避免静默丢失）
                try
                {
                    GiveModeFItem(FoldableCoverPackConfig.TYPE_ID, L10n.T("折叠掩体包", "Foldable Cover Pack"));
                    ModBehaviour.DevLog("[ModeF] 发放折叠掩体包 x1");
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeF] [WARNING] 发放折叠掩体包失败: " + e.Message);
                }

                // 快照初始最大生命值
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null)
                    {
                        modeFState.InitialMaxHealthSnapshot = player.Health.MaxHealth;
                        ModBehaviour.DevLog("[ModeF] 初始最大生命快照: " + modeFState.InitialMaxHealthSnapshot);
                    }
                }
                catch { }

                // 清除原始撤离点
                ClearOriginalExtractionPoints();
                profiler.Mark("ClearExtractionPoints");

                // 抽取并应用本局变异词条（必须先于 Boss 生成，敌人增益才能作用；流血加速词条仅 ModeF 可抽）
                owner.TryRollMutatorsForArena("ModeF");
                profiler.Mark("RollMutators");

                // 一次性生成所有 Boss（复用 Mode E 逻辑）
                #pragma warning disable CS4014
                modeE.ModeESpawnAllBosses(modeFSessionToken, relatedScene);
                #pragma warning restore CS4014
                profiler.Mark("ScheduleBosses");

                // 生成神秘商人 NPC
                #pragma warning disable CS4014
                merchantRuntime.SpawnModeEMerchant(modeFSessionToken, relatedScene);
                #pragma warning restore CS4014
                profiler.Mark("ScheduleMerchant");

                // 生成快递员
                owner.SpawnCourierNPC();
                profiler.Mark("SpawnCourier");

                // 启动状态机
                StartModeFRun();
                profiler.Mark("StartRuntime");

                owner.ShowMessage(L10n.T(
                    "血猎追击模式已激活！持续掉血，击杀Boss回血续命！",
                    "Bloodhunt mode activated! You're bleeding out - kill bosses to survive!"
                ));
                owner.ShowBigBanner(L10n.T(
                    "欢迎来到 " + ModBehaviour.RichDangerTag + "血猎追击</color>！",
                    "Welcome to " + ModBehaviour.RichDangerTag + "Bloodhunt</color>!"
                ));
                profiler.Complete("success");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] StartModeF 失败: " + e.Message);
                profiler.Complete("failed");
                try { ExitModeF(false); } catch { }
                return false;
            }
        }

        #endregion
    }
}
