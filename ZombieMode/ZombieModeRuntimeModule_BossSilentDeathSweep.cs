using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Boss 波的死亡兜底轮询。Boss 波只靠 <c>CurrentWaveBossesRemaining</c> 归零结束，而它只在死亡回调里扣减。
    /// 死亡广播已由 <see cref="DeathBroadcastIsolation"/> 逐订阅者隔离；这里兜住剩下两种情况：
    /// 广播隔离补丁因官方 IL 变化没装上时被别的订阅者截断（玩家反馈 2026-11-06：第 10 波 Boss 不见了、卡关），
    /// 以及 Boss 没经过死亡就被外部销毁。每 0.25 秒只看本波几只 Boss，战斗外与非 Boss 波 O(1) 早返。
    /// </summary>
    internal sealed partial class ZombieModeRuntimeModule
    {
        private float zombieModeBossSilentDeathSweepNextTime;

        private void SweepZombieModeSilentBossDeaths()
        {
            if (runState.CombatPhase != ZombieModeCombatPhase.Combat || runState.CurrentWaveBossInstances.Count <= 0)
            {
                return;
            }

            float now = GetZombieModeRuntimeNow();
            if (now < zombieModeBossSilentDeathSweepNextTime)
            {
                return;
            }

            zombieModeBossSilentDeathSweepNextTime = now + ZombieModeTuning.BossSilentDeathSweepIntervalSeconds;
            int runId = runState.RunId;
            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || !instance.Lifecycle.Alive)
                {
                    continue;
                }

                CharacterMainControl boss = instance.Character;
                if (boss == null)
                {
                    ModBehaviour.DevLog("[ZombieMode] [WARNING] 第 " + runState.CurrentWave + " 波 Boss 对象已销毁且未结案，兜底从本波扣除");
                    SettleZombieModeUnroutedBoss(runId, instance);
                }
                else
                {
                    Health health = null;
                    bool dead = false;
                    try
                    {
                        health = boss.Health;
                        dead = health != null && health.IsDead;
                    }
                    catch (Exception)
                    {
                        dead = false;
                    }

                    if (!dead)
                    {
                        continue;
                    }

                    ModBehaviour.DevLog("[ZombieMode] [WARNING] 第 " + runState.CurrentWave + " 波 Boss 已死但没收到死亡回调，兜底结案");
                    // 与死亡回调同一条路径：marker.DeathSettled 去重，计数、星点、掉落与本波结算都在里面。
                    try { HandleZombieModeHealthDead(runId, health, default(DamageInfo)); }
                    catch (Exception exception) { LogZombieModeWaveFailure("SilentBossDeath", exception); }
                    if (instance.Lifecycle.Alive)
                    {
                        ZombieModeEnemyRuntimeMarker settledMarker = instance.Marker;
                        if (!ReferenceEquals(settledMarker, null) && settledMarker.DeathSettled)
                        {
                            instance.Lifecycle.Alive = false;
                        }
                        else
                        {
                            // 主路由不认这只 Boss（登记已丢）：不能每 0.25 秒重试到天荒地老，按销毁同样结案。
                            SettleZombieModeUnroutedBoss(runId, instance);
                        }
                    }
                }

                // 结算可能已把本波推进到 Settling；后续实例留给下一波清空。
                if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }
            }

            if (runState.CurrentWaveBossesRemaining <= 0)
            {
                CompleteZombieModeWave(runId);
            }
        }

        /// <summary>
        /// 死亡主路由接不住的 Boss（对象已销毁，或已死但登记丢失）：从本波扣掉并注销记录，不发奖励箱
        /// （没有可信的击杀与位置）；Boss 节点的选卡奖励仍由本波结算照常发放。
        /// </summary>
        private void SettleZombieModeUnroutedBoss(int runId, ZombieModeBossInstance instance)
        {
            instance.Lifecycle.Alive = false;
            ZombieModeEnemyRuntimeMarker marker = instance.Marker;
            if (!ReferenceEquals(marker, null))
            {
                if (marker.DeathSettled || marker.RemovedFromRuntime)
                {
                    return;
                }
                marker.DeathSettled = true;
            }

            CharacterMainControl boss = instance.Character;
            if (!ReferenceEquals(boss, null))
            {
                // 已销毁对象的 GetInstanceID 仍可读；== null 判定会挡住常规注销。
                int instanceId = boss.GetInstanceID();
                zombieModeEnemyInstanceIds.Remove(instanceId);
                zombieModeEnemyMarkersByInstanceId.Remove(instanceId);
            }

            try { RemoveZombieModeHunterFrenzyModifiers(instance.SkillState as ZombieModeHunterState); }
            catch (Exception exception) { LogZombieModeWaveFailure("VanishedBossModifiers", exception); }
            runState.LivingZombieCount = Mathf.Max(0, runState.LivingZombieCount - 1);
            runState.CurrentWaveBossesRemaining = Mathf.Max(0, runState.CurrentWaveBossesRemaining - 1);
            PruneZombieModeRunOnlyEnemyRecords(runId);
        }
    }
}
