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

                // 清理所有存活的 Mode E 敌人（优先使用游戏API触发正常死亡流程）
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

                            CleanupModeEEnemyRuntimeState(enemy, enemyFaction);

                            // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                            try
                            {
                                if (enemy.characterPreset != null)
                                {
                                    UnityEngine.Object.Destroy(enemy.characterPreset);
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[ModeE] [WARNING] 结束模式时销毁敌人 characterPreset 失败: index=" + i + ", " + e.Message);
                            }

                            // 阻止掉落战利品箱子（模式结束清理，不应产生掉落物）
                            enemy.dropBoxOnDead = false;

                            // 使用 Health.Hurt() 造成致命伤害，触发正常死亡流程（动画等）
                            Health health = enemy.Health;
                            if (health != null && !health.IsDead)
                            {
                                DamageInfo dmgInfo = new DamageInfo();
                                dmgInfo.damageValue = health.MaxHealth * 10f;
                                dmgInfo.ignoreArmor = true;
                                health.Hurt(dmgInfo);
                            }
                            else
                            {
                                // Health 不可用时回退到直接销毁
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
