// ============================================================================
// ModeELifecycle.cs - Mode E faction bubble and cleanup lifecycle
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 在玩家头顶显示阵营气泡（"阵营：xxx"）
        /// </summary>
        private void ShowFactionBubble(Teams faction)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] ShowFactionBubble: 玩家或 transform 为 null");
                    return;
                }

                string factionName = GetFactionDisplayName(faction);
                string bubbleText = L10n.T("阵营：" + factionName, "Faction: " + faction.ToString());

                // 使用游戏原版 DialogueBubblesManager 显示气泡，时长 3 秒
                DialogueBubblesManager.Show(bubbleText, player.transform, 2.5f, false, false, -1f, 3f);
                DevLog("[ModeE] 显示阵营气泡: " + bubbleText);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ShowFactionBubble 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Dev 验收专用：走 Mode E 的完整结束收尾并回报收尾前后的可断言状态。
        ///
        /// 【语义说明，别当成撤离点交互】Mode E 没有自建撤离点（全模块无 CountDownArea 引用），
        /// 玩家侧的"撤离"就是这条结束收尾链：阵营复位、存活敌人清空、Mutator 清理、
        /// 商人/快递员 NPC 销毁。所以本入口驱动 EndModeE(false)，
        /// 报告 metrics 里标 semantics=end_settlement，不冒充撤离点验证。
        /// </summary>
        internal bool DebugSettleModeEExtractionForValidation(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            if (!DevModeEnabled) { reason = "dev_mode_disabled"; return false; }
            if (!modeEActive)
            {
                reason = "mode_e_not_active";
                return false;
            }

            int enemiesBefore = modeEAliveEnemies.Count;
            Teams teamBefore = Teams.player;
            try
            {
                CharacterMainControl playerBefore = CharacterMainControl.Main;
                if (playerBefore != null) teamBefore = playerBefore.Team;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [Validation] 读取收尾前阵营失败: " + e.Message);
            }

            try
            {
                EndModeE(false);
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + ":" + e.Message;
                return false;
            }

            Teams teamAfter = Teams.player;
            bool teamRestored = false;
            try
            {
                CharacterMainControl playerAfter = CharacterMainControl.Main;
                if (playerAfter != null)
                {
                    teamAfter = playerAfter.Team;
                    teamRestored = teamAfter == Teams.player;
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [Validation] 读取收尾后阵营失败: " + e.Message);
            }

            int enemiesAfter = modeEAliveEnemies.Count;
            metrics = "semantics=end_settlement,enemies=" + enemiesBefore + "->" + enemiesAfter
                + ",team=" + teamBefore + "->" + teamAfter
                + ",active=" + modeEActive;
            if (modeEActive) reason = "结束收尾后 modeEActive 仍为 true";
            else if (enemiesAfter != 0) reason = "结束收尾后仍有登记存活敌人: " + enemiesAfter;
            else if (!teamRestored) reason = "结束收尾后玩家阵营未复位为 player: " + teamAfter;
            return reason == null;
        }

        /// <summary>
        /// 结束 Mode E 模式
        /// </summary>
        public void EndModeE(bool showEndMessage = true)
        {
            try
            {
                if (!modeEActive) return;

                DevLog("[ModeE] 结束 Mode E 模式");

                // 先置 modeEActive = false，防止后续 Hurt() 触发的 OnModeEEnemyDeath
                // 回调中再对即将死亡的敌人执行无意义的 ApplyFactionDeathScaling
                modeEActive = false;
                CleanupModeELotteryAndHiringRuntime();
                InvalidateAndResetModeEShellSession("EndModeE");
                InvalidateModeESession();
                ClearEnemyRecoveryMonitorState();
                ClearPendingBossAggroQueue();
                RemoveModeEPlayerScalingModifiers();
                modeEPlayerLastHitKillCount = 0;

                // 清理变异词条（覆盖正常通关 / 玩家死亡 / 手动退出）
                ClearMutatorsForMode("ModeE");

                // 恢复玩家阵营
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        player.SetTeam(Teams.player);
                        DevLog("[ModeE] 玩家阵营已恢复为 player");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeE] [WARNING] 恢复玩家阵营失败: " + e.Message);
                }

                CleanupModeEPlayerNameTag();
                ResetModeEUiCaches();

                // 清理所有存活的 Mode E 敌人（模式已结束，直接撤销运行时注册并销毁）
                // [L4修复] 清理前先阻止所有敌人掉落战利品箱子，防止模式结束时友军Boss掉落一堆箱子
                modeEEndCleanupEnemyScratch.Clear();
                for (int i = 0; i < modeEAliveEnemies.Count; i++)
                {
                    modeEEndCleanupEnemyScratch.Add(modeEAliveEnemies[i]);
                }

                for (int i = modeEEndCleanupEnemyScratch.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        CharacterMainControl enemy = modeEEndCleanupEnemyScratch[i];
                        if (enemy != null && enemy.gameObject != null)
                        {
                            Teams? enemyFaction = null;
                            try
                            {
                                enemyFaction = enemy.Team;
                            }
                            catch (Exception e)
                            {
                                DevLog("[ModeE] [WARNING] 结束模式时读取敌人阵营失败: index=" + i + ", " + e.Message);
                            }

                            // 阻止掉落战利品箱子（模式结束清理，不应产生掉落物）
                            enemy.dropBoxOnDead = false;

                            CleanupModeEEnemyRuntimeState(enemy, enemyFaction);

                            // 模式已经结束且死亡回调已退订，直接停用并销毁最可靠；
                            // 预设由 ModeECharacterPresetLease 在角色销毁后延迟释放。
                            if (enemy.gameObject != null)
                            {
                                enemy.gameObject.SetActive(false);
                                UnityEngine.Object.Destroy(enemy.gameObject);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeE] [WARNING] 结束模式时清理敌人失败: index=" + i + ", " + e.Message);
                    }
                }
                modeEEndCleanupEnemyScratch.Clear();

                // 清理神秘商人 NPC
                CleanupModeEMerchant();

                // 清理快递员阿稳 NPC
                DestroyCourierNPC();

                // 重置所有状态（modeEActive 已在清理前置为 false）
                ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: true, stopWarmupCoroutine: true);

                // 重置刷怪消耗品击杀计数器
                modeERespawnKillCounter = 0;

                // 清理龙息Buff处理器（防止非 BossRush 场景中意外触发龙焰灼烧）
                DragonBreathBuffHandler.Cleanup();

                if (showEndMessage)
                {
                    ShowMessage(L10n.T(
                        "划地为营模式已结束！",
                        "Faction Battle ended!"
                    ));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] EndModeE 失败: " + e.Message);
            }
        }
    }
}
