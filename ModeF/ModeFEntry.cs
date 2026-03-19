using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 入口

        /// <summary>Mode F 会话序号，防止上一局的异步对象晚到并污染新局。</summary>
        private int modeFSessionSerial = 0;

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

        private bool IsModeFSessionStillValid(int sessionToken, int relatedScene)
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
        private Item DetectBloodhuntTransponder()
        {
            return FindFirstPlayerInventoryItemByTypeId(
                BloodhuntTransponderConfig.TYPE_ID,
                "ModeF",
                "血猎收发器");
        }

        private Item DetectBossRushTicketItem()
        {
            return FindFirstPlayerInventoryItemByTypeId(GetBossRushTicketTypeId());
        }

        private bool IsPlayerNakedForModeF()
        {
            return IsPlayerNakedWithAllowedItems(
                "ModeF",
                GetBossRushTicketTypeId(),
                BloodhuntTransponderConfig.TYPE_ID,
                false);
        }

        /// <summary>
        /// 检测并尝试启动 Mode F
        /// 条件：裸装 + 船票 + 血猎收发器 + 无营旗
        /// </summary>
        public bool TryStartModeF()
        {
            bool transponderConsumed = false;
            bool ticketConsumed = false;
            try
            {
                if (modeFActive || modeFState.IsActive)
                {
                    DevLog("[ModeF] Mode F 已在运行，忽略重复启动请求");
                    return false;
                }

                if (modeDActive || modeEActive)
                {
                    DevLog("[ModeF] Mode D 或 Mode E 已激活，跳过 Mode F 启动");
                    return false;
                }

                Item ticket = DetectBossRushTicketItem();
                bool ticketPrepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
                Item transponder = DetectBloodhuntTransponder();
                if ((ticket == null && !ticketPrepaid) || transponder == null)
                {
                    DevLog("[ModeF] 未检测到船票或血猎收发器，不启动 Mode F");
                    return false;
                }

                var (faction, flagItem) = DetectFactionFlag();
                if (faction.HasValue || flagItem != null)
                {
                    DevLog("[ModeF] 检测到营旗，按优先级不进入 Mode F");
                    return false;
                }

                if (!IsPlayerNakedForModeF())
                {
                    DevLog("[ModeF] 玩家不满足裸装条件，拒绝启动");
                    ShowMessage(L10n.T(
                        "血猎追击模式需要裸装入场！请清空所有装备后重试。",
                        "Bloodhunt mode requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                // H3: 先验证两个道具都存在，再一起消耗
                // 如果任一消耗失败，仍尝试启动（道具已部分消耗，不启动更糟）
                transponderConsumed = TryConsumeModeEntryItem(transponder, "ModeF", "血猎收发器");
                ticketConsumed = ticketPrepaid || TryConsumeModeEntryItem(ticket, "ModeF", "船票");

                if (!transponderConsumed && !ticketConsumed)
                {
                    // 两个都没消耗成功，安全退出
                    ShowMessage(L10n.T(
                        "血猎追击模式启动失败：入场道具消耗异常。",
                        "Bloodhunt start failed: unable to consume the entry items."
                    ));
                    return false;
                }

                if (!transponderConsumed || !ticketConsumed)
                {
                    DevLog("[ModeF] [WARNING] 部分道具消耗失败 (transponder=" + transponderConsumed + ", ticket=" + ticketConsumed + ")，仍尝试启动以避免道具丢失");
                }

                bool started = StartModeF();
                if (!started)
                {
                    RefundModeFStartupEntryItems(ticketConsumed, transponderConsumed);
                    ShowMessage(L10n.T(
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
                DevLog("[ModeF] [ERROR] TryStartModeF 失败: " + e.Message);
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
            if (TryConsumeModeEntryItem(originalItem, "ModeF", itemLabel))
            {
                DevLog("[ModeF] 启动成功后已补偿消耗" + itemLabel);
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
                if (TryConsumeModeEntryItem(fallbackItem, "ModeF", itemLabel))
                {
                    DevLog("[ModeF] 启动成功后已通过重新检索补偿消耗" + itemLabel);
                    return true;
                }
            }

            DevLog("[ModeF] [WARNING] 启动成功，但未能补偿消耗" + itemLabel + "，请留意背包状态");
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
                    GetBossRushTicketTypeId(),
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
                DevLog("[ModeF] [WARNING] 启动失败后的入场道具返还未成功，请检查背包与地面掉落。");
            }
        }

        private bool TryRefundModeFStartupEntryItem(int typeId, string displayName)
        {
            bool refunded = TryGiveItemToPlayerOrDrop(typeId, displayName, false);
            if (!refunded)
            {
                DevLog("[ModeF] [WARNING] 返还入场道具失败: typeId=" + typeId + ", displayName=" + displayName);
            }

            return refunded;
        }

        /// <summary>
        /// 启动 Mode F 模式
        /// </summary>
        private bool StartModeF()
        {
            try
            {
                if (modeFActive || modeFState.IsActive)
                {
                    DevLog("[ModeF] [WARNING] StartModeF 在模式已激活时被重复调用，已忽略");
                    return false;
                }

                DevLog("[ModeF] 启动 Mode F 血猎追击模式");

                modeFActive = true;
                modeFState.Reset();
                modeFActiveBossSet.Clear();
                modeFState.IsActive = true;
                int modeFSessionToken = BeginModeFSession();
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                ClearEnemyRecoveryMonitorState();
                PrepareModeESharedRuntimeForModeF();

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                InitializeModeDEnemyPools();
                BuildModeEFactionPresetCaches();
                EnsureModeDGlobalItemPool();

                // 订阅龙息Buff处理器
                DragonBreathBuffHandler.Subscribe();

                // 分配刷怪点（复用 Mode E 逻辑）
                PreCacheMapSpawnerPositions();
                AllocateSpawnPoints();

                // 传送玩家到安全位置
                TeleportPlayerToSafePosition();

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备
                ModeEGiveColdWeatherGear();

                // 额外发放折叠掩体包 x1（背包满时掉在脚下，避免静默丢失）
                try
                {
                    GiveModeFItem(FoldableCoverPackConfig.TYPE_ID, L10n.T("折叠掩体包", "Foldable Cover Pack"));
                    DevLog("[ModeF] 发放折叠掩体包 x1");
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 发放折叠掩体包失败: " + e.Message);
                }

                // 快照初始最大生命值
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null)
                    {
                        modeFState.InitialMaxHealthSnapshot = player.Health.MaxHealth;
                        DevLog("[ModeF] 初始最大生命快照: " + modeFState.InitialMaxHealthSnapshot);
                    }
                }
                catch { }

                // 清除原始撤离点
                ClearOriginalExtractionPoints();

                // 一次性生成所有 Boss（复用 Mode E 逻辑）
                #pragma warning disable CS4014
                ModeESpawnAllBosses(modeFSessionToken, relatedScene);
                #pragma warning restore CS4014

                // 生成神秘商人 NPC
                #pragma warning disable CS4014
                SpawnModeEMerchant(modeFSessionToken, relatedScene);
                #pragma warning restore CS4014

                // 生成快递员
                SpawnCourierNPC();

                // 启动状态机
                StartModeFRun();

                ShowMessage(L10n.T(
                    "血猎追击模式已激活！持续掉血，击杀Boss回血续命！",
                    "Bloodhunt mode activated! You're bleeding out - kill bosses to survive!"
                ));
                ShowBigBanner(L10n.T(
                    "欢迎来到 <color=red>血猎追击</color>！",
                    "Welcome to <color=red>Bloodhunt</color>!"
                ));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] StartModeF 失败: " + e.Message);
                try { ExitModeF(false); } catch { }
                return false;
            }
        }

        #endregion
    }
}
