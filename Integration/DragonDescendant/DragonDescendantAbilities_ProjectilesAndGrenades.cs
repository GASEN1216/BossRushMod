// ============================================================================
// DragonDescendantAbilities_ProjectilesAndGrenades.cs - projectile and grenade attacks
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    public partial class DragonDescendantAbilityController : MonoBehaviour
    {
        /// <summary>
        /// Boss射击回调
        /// </summary>
        private void OnBossShoot(int shotsFired = 1)
        {
            bulletCounter += shotsFired;

            // 每10发子弹发射一次火箭弹
            while (bulletCounter >= DragonDescendantConfig.BulletsPerRocket)
            {
                bulletCounter -= DragonDescendantConfig.BulletsPerRocket;
                LaunchRocket();
            }
        }

        /// <summary>
        /// 发射火箭弹 - 简化版：只有玩家距离Boss小于2m时才在玩家位置爆炸
        /// </summary>
        private void LaunchRocket()
        {
            try
            {
                if (bossCharacter == null || playerCharacter == null) return;

                // Mode E 同阵营不攻击玩家
                if (IsPlayerAlly()) return;

                // Mode E 脱战距离检查
                if (IsPlayerOutOfLeashRange()) return;
                Vector3 explosionPos = playerCharacter.transform.position;

                // 检查玩家是否在Boss 2米范围内，只有在范围内才爆炸
                float distToBoss = Vector3.Distance(explosionPos, bossCharacter.transform.position);
                bool shouldExplode = distToBoss <= DragonDescendantConfig.RocketBossDamageRadius;

                ModBehaviour.DevLog("[DragonDescendant] 火箭弹检测: 玩家位置: " + explosionPos +
                    ", Boss距离: " + distToBoss + "m, 是否爆炸: " + shouldExplode);

                // 只有玩家在Boss附近时才创建爆炸
                if (!shouldExplode) return;

                // 创建爆炸
                if (LevelManager.Instance != null && LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = DragonDescendantConfig.RocketExplosionDamage;
                    dmgInfo.isExplosion = true;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);

                    // 注意：原版ExplosionManager只处理normal和flash类型的特效
                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        explosionPos,
                        DragonDescendantConfig.RocketExplosionRadius,
                        dmgInfo,
                        ExplosionFxTypes.normal,
                        1f,
                        true
                    );

                    ModBehaviour.DevLog("[DragonDescendant] 火箭弹爆炸创建成功，范围: " + DragonDescendantConfig.RocketExplosionRadius);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 发射火箭弹失败: " + e.Message);
            }
        }


        // ========== 燃烧弹逻辑 ==========

        /// <summary>
        /// 燃烧弹计时器协程
        /// [性能优化] 使用缓存的WaitForSeconds避免GC
        /// </summary>
        private IEnumerator GrenadeTimerCoroutine()
        {
            while (true)
            {
                // 根据状态选择间隔
                float interval = isEnraged
                    ? DragonDescendantConfig.EnragedGrenadeInterval
                    : DragonDescendantConfig.NormalGrenadeInterval;

                // [性能优化] 使用缓存的WaitForSeconds
                yield return GetCachedWaitForSeconds(interval);

                // 复活期间不投掷
                if (isResurrecting) continue;

                // 投掷燃烧弹
                ThrowIncendiaryGrenade();
            }
        }

        /// <summary>
        /// 获取缓存的WaitForSeconds对象
        /// [性能优化] 避免每次创建新对象产生GC
        /// </summary>
        private WaitForSeconds GetCachedWaitForSeconds(float seconds)
        {
            // 常用时间使用预缓存对象
            if (Mathf.Approximately(seconds, 0.1f)) return wait01s;
            if (Mathf.Approximately(seconds, 0.2f)) return wait02s;
            if (Mathf.Approximately(seconds, 0.5f)) return wait05s;
            if (Mathf.Approximately(seconds, 1f)) return wait1s;
            if (Mathf.Approximately(seconds, 3f)) return wait3s;
            if (Mathf.Approximately(seconds, 10f)) return wait10s;

            // 燃烧弹间隔使用动态缓存
            if (!Mathf.Approximately(cachedGrenadeIntervalValue, seconds))
            {
                cachedGrenadeIntervalValue = seconds;
                cachedGrenadeInterval = new WaitForSeconds(seconds);
            }
            return cachedGrenadeInterval;
        }

        /// <summary>
        /// 投掷燃烧弹 - 始终投向玩家脚下
        /// </summary>
        private void ThrowIncendiaryGrenade()
        {
            try
            {
                if (bossCharacter == null) return;

                // 更新玩家引用
                if (playerCharacter == null)
                {
                    try { playerCharacter = CharacterMainControl.Main; } catch { }
                }

                if (playerCharacter == null) return;

                // Mode E 同阵营不攻击玩家
                if (IsPlayerAlly()) return;

                // Mode E 脱战距离检查
                if (IsPlayerOutOfLeashRange()) return;

                // 始终投向玩家脚下（不再区分血量阶段）
                Vector3 targetPos = playerCharacter.transform.position;

                // 创建燃烧弹
                CreateIncendiaryGrenade(targetPos);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 投掷燃烧弹失败: " + e.Message);
            }
        }

        /// <summary>
        /// 创建燃烧弹
        /// </summary>
        private void CreateIncendiaryGrenade(Vector3 targetPos)
        {
            try
            {
                Vector3 startPos = bossCharacter.transform.position + Vector3.up * 1.5f;

                // 查找燃烧弹预制体
                Grenade grenadePrefab = FindIncendiaryGrenadePrefab();

                if (grenadePrefab != null)
                {
                    // 使用预制体创建
                    Grenade grenade = UnityEngine.Object.Instantiate(grenadePrefab, startPos, Quaternion.identity);

                    // 设置伤害信息
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = 30f;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                    grenade.damageInfo = dmgInfo;

                    // 计算投掷速度
                    Vector3 velocity = CalculateThrowVelocity(startPos, targetPos, 8f);
                    grenade.Launch(startPos, velocity, bossCharacter, false);

                    ModBehaviour.DevLog("[DragonDescendant] 投掷燃烧弹到: " + targetPos);
                }
                else
                {
                    // 没有预制体，直接在目标位置创建火焰爆炸
                    StartCoroutine(DelayedFireExplosion(targetPos, 1f));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 创建燃烧弹失败: " + e.Message);
            }
        }

        /// <summary>
        /// 查找燃烧弹预制体（使用缓存）
        /// </summary>
        private Grenade FindIncendiaryGrenadePrefab()
        {
            // 直接返回缓存的预制体（已在Initialize时预缓存）
            return cachedGrenadePrefab;
        }

        /// <summary>
        /// 计算投掷速度
        /// </summary>
        private Vector3 CalculateThrowVelocity(Vector3 start, Vector3 target, float verticalSpeed)
        {
            float gravity = Physics.gravity.magnitude;
            if (gravity <= 0f) gravity = 9.81f;

            float timeUp = verticalSpeed / gravity;
            float heightDiff = start.y - target.y;
            float timeDown = Mathf.Sqrt(2f * Mathf.Abs(timeUp * verticalSpeed * 0.5f + heightDiff) / gravity);
            float totalTime = timeUp + timeDown;

            if (totalTime <= 0f) totalTime = 0.001f;

            Vector3 horizontalDelta = target - start;
            horizontalDelta.y = 0f;
            float horizontalDistanceSqr = horizontalDelta.sqrMagnitude;
            Vector3 horizontalVelocity = horizontalDistanceSqr > 0.0000000001f ? horizontalDelta / totalTime : Vector3.zero;

            return horizontalVelocity + Vector3.up * verticalSpeed;
        }

        /// <summary>
        /// 延迟火焰爆炸（作为燃烧弹的后备方案）
        /// [性能优化] 使用缓存的WaitForSeconds
        /// </summary>
        private IEnumerator DelayedFireExplosion(Vector3 position, float delay)
        {
            yield return GetCachedWaitForSeconds(delay);

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = 30f;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);

                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        position,
                        2.5f,
                        dmgInfo,
                        ExplosionFxTypes.fire,
                        0.5f,
                        true
                    );
                }
            }
            catch { }
        }
    }
}
