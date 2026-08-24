using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private enum BossRushEntryMode
        {
            Normal,
            ModeD,
            ModeE,
            ModeF,
            ModeG
        }

        /// <summary>
        /// 统一判定 BossRush 入场模式，优先级：Mode E > Mode F > Mode D > Normal
        /// </summary>
        private BossRushEntryMode DetermineBossRushEntryMode(string context)
        {
            try
            {
                bool? isPlayerNaked = null;

                var (modeEFaction, modeEFlag) = DetectFactionFlag();
                if (modeEFaction.HasValue && modeEFlag != null)
                {
                    isPlayerNaked = IsPlayerNaked();
                    if (isPlayerNaked.Value)
                    {
                        return BossRushEntryMode.ModeE;
                    }
                }

                Item ticket = DetectBossRushTicketItem();
                bool hasPrepaidTicket = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
                Item transponder = DetectBloodhuntTransponder();
                if ((ticket != null || hasPrepaidTicket) && transponder != null)
                {
                    if (IsPlayerNakedForModeF())
                    {
                        return BossRushEntryMode.ModeF;
                    }

                    DevLog("[BossRush] " + context + ": 检测到船票+血猎收发器，但玩家不满足 Mode F 裸装条件，不进入 Mode F，继续后续入场判定");
                }

                // Mode G 检测：发布闸关闭时完全不劫持旧入口。FreezeEntryIntent
                // 发生在切图前，实际启动判定还必须命中 verified runtime scene。
                bool modeGEntryEnabled = ModeGAvailability.IsProductionReady
                    || ModeGAvailability.AllowDevTestEntry;
                bool modeGSceneEligible = string.Equals(context, "FreezeEntryIntent", StringComparison.Ordinal)
                    || ModeGMapSupportRegistry.IsVerifiedSceneName(SceneManager.GetActiveScene().name);
                Item relic = modeGEntryEnabled && modeGSceneEligible ? DetectFateEchoRelic() : null;
                if (modeGEntryEnabled && modeGSceneEligible
                    && (ticket != null || hasPrepaidTicket) && relic != null && transponder == null)
                {
                    if (IsModeGLoadoutEligible())
                    {
                        return BossRushEntryMode.ModeG;
                    }

                    DevLog("[BossRush] " + context + ": 检测到船票+宿命回响信物，但玩家装备状态不满足 Mode G 入场条件，不进入 Mode G，继续后续入场判定");
                }

                if (!isPlayerNaked.HasValue)
                {
                    isPlayerNaked = IsPlayerNaked();
                }

                if (isPlayerNaked.Value)
                {
                    return BossRushEntryMode.ModeD;
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] " + context + ": 检测 BossRush 入场模式失败: " + e.Message);
            }

            return BossRushEntryMode.Normal;
        }

        internal bool IsModeGEntryIntentNow()
        {
            return DetermineBossRushEntryMode("FreezeEntryIntent") == BossRushEntryMode.ModeG;
        }

        private IEnumerator SetupBossRushInDemoChallenge(Scene scene)
        {
            // 等待场景初始化（缩短等待时间，尽快传送玩家）
            yield return new WaitForSeconds(0.5f);

            // 提前检测 Mode E / Mode F / Mode D 条件（Mode E > Mode F > Mode D）
            // 必须在禁用 spawner 之前检测，因为 Mode E 需要先扫描 CharacterSpawnerRoot 位置
            BossRushEntryMode entryMode = DetermineBossRushEntryMode("SetupBossRushInDemoChallenge");

            // Mode E 提前分支：先扫描刷怪点，再禁用spawner和清理敌人，跳过路牌/气泡/快递员
            if (entryMode == BossRushEntryMode.ModeE)
            {
                DevLog("[BossRush] 检测到营旗+裸装入场，将启动 Mode E");
                ScheduleModeEStartupWarmup("DemoChallenge");
                PreCacheMapSpawnerPositions();

                // 禁用 spawner 和清理敌人（Mode E 仍需要）
                DisableAllSpawners();
                DevLog("[BossRush] Mode E: 已禁用竞技场范围内的敌怪生成器");
                ClearEnemiesForBossRush();

                yield return new WaitForSeconds(0.5f);
                bool startedModeE = TryStartModeE();
                if (startedModeE)
                {
                    bool verifiedModeE = false;
                    yield return StartCoroutine(WaitForModeEStartupVerification(result => verifiedModeE = result));
                    if (verifiedModeE)
                    {
                        SpawnCommonNPCs("DEMO场景 Mode E 初始化完成");
                        ScheduleRestoreFollowingSpouse(SceneManager.GetActiveScene().name, "DEMO场景 Mode E 初始化完成");
                        BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                        yield break;
                    }
                }

                StopModeEStartupWarmupIfPending();
                DevLog("[BossRush] [WARNING] Mode E 启动未通过验证，回退到普通 BossRush DEMO 初始化流程");
            }

            // Mode F 提前分支：复用 Mode E 的地图初始化，叠加 Mode F 状态机
            if (entryMode == BossRushEntryMode.ModeF)
            {
                DevLog("[BossRush] 检测到船票+血猎收发器+裸装入场，将启动 Mode F");
                ScheduleModeEStartupWarmup("DemoChallenge_ModeF");
                PreCacheMapSpawnerPositions();

                DisableAllSpawners();
                DevLog("[BossRush] Mode F: 已禁用竞技场范围内的敌怪生成器");
                ClearEnemiesForBossRush();

                yield return new WaitForSeconds(0.5f);
                bool startedModeF = TryStartModeF();
                if (startedModeF)
                {
                    SpawnCommonNPCs("DEMO场景 Mode F 初始化完成");
                    ScheduleRestoreFollowingSpouse(SceneManager.GetActiveScene().name, "DEMO场景 Mode F 初始化完成");
                }
                else
                {
                    DevLog("[BossRush] [WARNING] Mode F 启动失败，已跳过 Mode F 额外 NPC 生成");
                }
                BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                yield break;
            }

            // Mode G 独立分支：成功或失败都不得落入普通 BossRush 初始化。
            if (entryMode == BossRushEntryMode.ModeG)
            {
                DevLog("[BossRush] 检测到船票+宿命回响信物，将保留玩家当前装备并尝试启动 Mode G");
                yield return new WaitForSeconds(0.5f);
                bool openedModeG = ModeGInteractable.TryOpenConfirmation(this);
                while (openedModeG && ModeGInteractable.IsConfirmationOpen)
                {
                    yield return null;
                }
                bool startedModeG = modeGActive;
                if (startedModeG)
                {
                    SpawnCommonNPCs("DEMO场景 Mode G 初始化完成");
                    ScheduleRestoreFollowingSpouse(SceneManager.GetActiveScene().name, "DEMO场景 Mode G 初始化完成");
                }
                else
                {
                    if (ModeGInteractable.LastConfirmationAttemptedStart)
                    {
                        DevLog("[BossRush] [WARNING] Mode G 启动失败，已中止本次竞技场初始化");
                        ShowMessage(L10n.T(
                            "宿命回响启动失败，入场物品已恢复。",
                            "Fate Echo failed to start. Entry items were restored."));
                    }
                    else
                    {
                        DevLog("[BossRush] Mode G 确认页已取消，竞技场环境保持不变");
                    }
                }
                if (!openedModeG) TryRefundModeGPendingPrepaidTicket();
                yield break;
            }

            // [修复] spawner 已在 OnSceneLoaded 中立即禁用，这里再次调用确保万无一失
            // 由于 spawnersDisabled 标志，重复调用会直接返回
            DisableAllSpawners();
            DevLog("[BossRush] 已禁用竞技场范围内的敌怪生成器");

            // 清理场景中现有的敌人（确保清理干净）
            ClearEnemiesForBossRush();

            // 传送玩家到指定位置（从配置系统获取当前地图的默认位置）
            Vector3 targetPosition = GetCurrentSceneDefaultPosition();
            try
            {
                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch
                {
                }

                if (main == null && playerCharacter != null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch
                    {
                    }
                }

                if (main == null)
                {
                    try
                    {
                        var candidate = FindObjectOfType<CharacterMainControl>();
                        if (candidate != null)
                        {
                            main = candidate;
                        }
                    }
                    catch
                    {
                    }
                }

                if (main != null)
                {
                    try
                    {
                        if (main.gameObject.scene != scene)
                        {
                            SceneManager.MoveGameObjectToScene(main.gameObject, scene);
                            DevLog("[BossRush] 已将玩家移动到场景: " + scene.name);
                        }
                    }
                    catch
                    {
                    }

                    Vector3 currentPos = main.transform.position;
                    if ((currentPos - targetPosition).sqrMagnitude > 0.25f)
                    {
                        try
                        {
                            main.SetPosition(targetPosition);
                        }
                        catch
                        {
                            main.transform.position = targetPosition;
                        }
                        demoChallengeStartPosition = targetPosition;
                        DevLog("[BossRush] 已将玩家传送到指定位置: " + targetPosition);
                    }
                    else
                    {
                        demoChallengeStartPosition = targetPosition;
                    }
                }
                else
                {
                    DevLog("[BossRush] SetupBossRushInDemoChallenge: 未找到玩家角色，无法传送到指定位置");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 传送玩家到 BossRush 难度入口附近失败: " + e.Message);
            }

            try
            {
                CreateRescueTeleportBubble();
            }
            catch
            {
            }

            TryCreateArenaDifficultyEntryPoint();
            StartCoroutine(EnsureArenaEntryPointCreated());

            if (entryMode == BossRushEntryMode.ModeD)
            {
                DevLog("[BossRush] 检测到裸体入场（无营旗无收发器），将启动 Mode D");
                yield return new WaitForSeconds(0.5f);
                TryStartModeD();
            }

            SpawnCommonNPCs("DEMO场景初始化完成");
            ScheduleRestoreFollowingSpouse(SceneManager.GetActiveScene().name, "DEMO场景初始化完成");
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
        }
    }
}
