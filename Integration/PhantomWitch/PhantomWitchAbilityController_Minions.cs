// ============================================================================
// PhantomWitchAbilityController partial - extracted from PhantomWitchAbilityController.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public partial class PhantomWitchAbilityController : MonoBehaviour
    {
        // ---- Mode G 托管辅助契约（规格 §20 第 16 条）----
        // Legacy 路径不绑定（两委托均为 null），行为逐字不变。
        private Func<CharacterMainControl, ManagedBossRole, bool> _modeGAuxCommit;
        private Action<CharacterMainControl, ManagedBossRole> _modeGAuxReleased;
        private readonly HashSet<CharacterMainControl> _modeGCommittedMinions
            = new HashSet<CharacterMainControl>();

        /// <summary>
        /// 绑定 Mode G 托管辅助契约（仅 PhantomWitchBoss_ModeGAdapter 激活路径调用）。
        /// 随从激活前必须经 TryCommitAuxiliaryBeforeActivation 原子提交；
        /// 返回 true 才放行，迟到 ticket 不写 Legacy 单例。
        /// </summary>
        internal void BindModeGAuxiliaryContract(
            Func<CharacterMainControl, ManagedBossRole, bool> tryCommitBeforeActivation,
            Action<CharacterMainControl, ManagedBossRole> onReleased)
        {
            _modeGAuxCommit = tryCommitBeforeActivation;
            _modeGAuxReleased = onReleased;
        }

        /// <summary>
        /// 释放已提交的 Mode G 辅助单位（仅成功提交者恰好一次；幂等）。
        /// </summary>
        internal void ReleaseModeGAuxiliary(CharacterMainControl minion)
        {
            if (minion == null) return;
            try
            {
                if (!_modeGCommittedMinions.Remove(minion)) return;
                if (_modeGAuxReleased == null) return;
                _modeGAuxReleased(minion, ManagedBossRole.Auxiliary);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] Mode G 辅助释放异常: " + e.Message);
            }
        }

        private async UniTask SpawnMinion(int index, int totalCount, PhantomWitchMinionRole role)
        {
            if (bossCharacter == null)
            {
                pendingMinionRoles.Remove(role);
                return;
            }

            try
            {
                Vector3 lateralOffset = role == PhantomWitchMinionRole.Sustain
                    ? -bossCharacter.transform.right
                    : bossCharacter.transform.right;
                if (lateralOffset.sqrMagnitude < 0.001f)
                {
                    lateralOffset = index == 0 ? Vector3.left : Vector3.right;
                }

                Vector3 spawnPos = bossCharacter.transform.position +
                    lateralOffset.normalized * PhantomWitchConfig.MinionSpawnDistance;

                spawnPos = SampleNavMeshOrFallback(spawnPos, bossCharacter.transform.position);
                TrackEffect(PhantomWitchAssetManager.CreateMinionSpawnEffect(spawnPos));

                CharacterRandomPreset minionPreset = GetCachedMinionPreset();
                if (minionPreset == null)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [WARNING] 未找到合适的随从预设");
                    pendingMinionRoles.Remove(role);
                    return;
                }

                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                CharacterMainControl minion = await minionPreset.CreateCharacterAsync(
                    spawnPos,
                    Vector3.forward,
                    relatedScene,
                    null,
                    false);

                if (minion == null)
                {
                    pendingMinionRoles.Remove(role);
                    return;
                }

                if (bossCharacter == null || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    CleanupSpawnedMinion(minion);
                    return;
                }

                // Mode G 托管辅助原子提交（规格 §20 第 16 条）：提交返回 true 才激活；
                // 迟到 ticket（父 owner 失效/run 结束）fail-closed，不写 summonedMinions 等 Legacy 集合。
                if (_modeGAuxCommit != null)
                {
                    bool auxCommitted = false;
                    try
                    {
                        auxCommitted = _modeGAuxCommit(minion, ManagedBossRole.Auxiliary);
                    }
                    catch (Exception commitEx)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] Mode G 辅助提交异常: " + commitEx.Message);
                    }
                    if (!auxCommitted)
                    {
                        CleanupSpawnedMinion(minion);
                        return;
                    }
                    _modeGCommittedMinions.Add(minion);
                }

                FinalizeSpawnedMinion(minion, index);
                AssignMinionRole(minion, role);
                ModBehaviour.DevLog("[PhantomWitch] 随从 " + index + " 已召唤");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] 生成随从失败: " + e.Message);
            }
            finally
            {
                pendingMinionRoles.Remove(role);
            }
        }

        private void ApplyMinionHealth(CharacterMainControl minion)
        {
            if (minion == null || minion.Health == null)
            {
                return;
            }

            Stat healthStat = minion.CharacterItem != null
                ? minion.CharacterItem.GetStat("MaxHealth")
                : null;
            if (healthStat != null)
            {
                healthStat.BaseValue = PhantomWitchConfig.MinionHealth;
            }

            minion.Health.SetHealth(PhantomWitchConfig.MinionHealth);
            minion.Health.showHealthBar = true;
            minion.Health.RequestHealthBar();
        }

        private void ConfigureMinionAI(CharacterMainControl minion)
        {
            AICharacterController aiCtrl = minion != null ? minion.GetComponentInChildren<AICharacterController>() : null;
            if (aiCtrl == null)
            {
                return;
            }

            var inst = ModBehaviour.Instance;
            bool isModeE = inst != null && inst.IsModeEActive;
            aiCtrl.forceTracePlayerDistance = isModeE ? 0f : PhantomWitchConfig.MinionForceTraceDistance;

            CharacterMainControl target;
            if (!isModeE && TryResolveCombatTarget(out target) &&
                target.mainDamageReceiver != null)
            {
                aiCtrl.searchedEnemy = target.mainDamageReceiver;
                aiCtrl.noticed = true;
            }
        }

        private void FinalizeSpawnedMinion(CharacterMainControl minion, int index)
        {
            if (minion == null || bossCharacter == null)
            {
                return;
            }

            minion.SetTeam(bossCharacter.Team);
            minion.dropBoxOnDead = false;
            minion.gameObject.SetActive(true);
            ApplyMinionHealth(minion);
            ConfigureMinionAI(minion);

            minion.gameObject.name = "PhantomWitch_Minion_" + index;
            summonedMinions.Add(minion);
        }

        private void OnAnyEntityDeath(Health deadHealth, DamageInfo info)
        {
            if (CurrentPhase == PhantomWitchPhase.Dead || deadHealth == null)
            {
                return;
            }

            try
            {
                CharacterMainControl deadChar = deadHealth.TryGetCharacter();
                if (deadChar != null && summonedMinions.Contains(deadChar))
                {
                    summonedMinions.Remove(deadChar);
                    ReleaseModeGAuxiliary(deadChar);
                    liveMinions.RemoveAll(delegate(MinionEntry entry)
                    {
                        return entry == null || entry.character == null || entry.character == deadChar;
                    });
                    EmitTelemetry("minion_death", "liveCount=" + CountLiveMinions() + ",name=" + deadChar.name);
                    if (CurrentPhase == PhantomWitchPhase.Phase3 && !minionFirstClearedLogged)
                    {
                        minionFirstClearedLogged = true;
                        EmitTelemetry("minion_first_cleared",
                            "minionCleared=true,playerHPRatio=" + GetPlayerHealthRatio().ToString("0.00"));
                    }
                    ModBehaviour.DevLog("[PhantomWitch] 随从被击杀，剩余: " + summonedMinions.Count);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] OnAnyEntityDeath处理失败: " + e.Message);
            }
        }

    }
}
