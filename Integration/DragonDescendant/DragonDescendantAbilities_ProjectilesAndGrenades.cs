// ============================================================================
// DragonDescendantAbilities_ProjectilesAndGrenades.cs - projectile and grenade attacks
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        private static readonly WaitForSeconds waitRocketTelegraph = new WaitForSeconds(DragonDescendantConfig.RocketTelegraphSeconds);

        /// <summary>
        /// 发射火箭弹：玩家在 Boss 身边（RocketBossDamageRadius）时，锁定玩家此刻的落点，
        /// 地面亮 RocketTelegraphSeconds 的预警圈后在落点爆炸。走开就能躲，翻滚豁免走官方爆炸口径。
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
                Vector3 lockedPos = playerCharacter.transform.position;

                float triggerRadius = DragonDescendantConfig.RocketBossDamageRadius;
                Vector3 toBoss = lockedPos - bossCharacter.transform.position;
                if (toBoss.sqrMagnitude > triggerRadius * triggerRadius) return;

                StartCoroutine(RocketStrikeRoutine(lockedPos));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 发射火箭弹失败: " + e.Message);
            }
        }

        private IEnumerator RocketStrikeRoutine(Vector3 lockedPos)
        {
            LevelManager level = LevelManager.Instance;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (!CanCompleteRocket(level, sceneHandle)) yield break;
            GameObject marker = null;
            bool fired = false;
            try
            {
                try
                {
                    marker = DragonDescendantRocketMarker.Create(lockedPos, DragonDescendantConfig.RocketExplosionRadius,
                        DragonDescendantConfig.RocketTelegraphSeconds, this, level, sceneHandle);
                    PlayDragonCue(DragonKingConfig.Sound_LanceWarning);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 火箭弹预警圈创建失败: " + e.Message);
                }

                yield return waitRocketTelegraph;

                if (!CanCompleteRocket(level, sceneHandle)) yield break;
                if (level.ExplosionManager == null) yield break;

                try
                {
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = DragonDescendantConfig.RocketExplosionDamage
                        * BossSkillDamageRules.ResolveGunDamageScale(bossCharacter, DragonDescendantConfig.DamageMultiplier);
                    dmgInfo.isExplosion = true;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);

                    // 注意：原版ExplosionManager只处理normal和flash类型的特效
                    level.ExplosionManager.CreateExplosion(
                        lockedPos,
                        DragonDescendantConfig.RocketExplosionRadius,
                        dmgInfo,
                        ExplosionFxTypes.normal,
                        1f,
                        true
                    );
                    fired = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 火箭弹爆炸失败: " + e.Message);
                }
            }
            finally
            {
                if (!fired && marker != null) Destroy(marker);
            }
        }

        /// <summary>龙裔技能提示音（火箭预警、二阶段冲刺起手）的唯一宿主音效入口。</summary>
        private static void PlayDragonCue(string soundPath)
        {
            ModBehaviour.Instance?.PlaySoundEffect(soundPath);
        }

        /// <summary>延迟攻击与预警共用有效性门；死亡、停用、换阵营、脱战或切图后不能留下旧攻击。</summary>
        internal bool CanCompleteRocket(LevelManager level, int sceneHandle)
        {
            return isActiveAndEnabled && bossCharacter != null && bossHealth != null && !bossHealth.IsDead &&
                playerCharacter != null && playerCharacter.Health != null && !playerCharacter.Health.IsDead &&
                ReferenceEquals(playerCharacter, CharacterMainControl.Main) && !isResurrecting &&
                !SceneLoader.IsSceneLoading && level != null && ReferenceEquals(level, LevelManager.Instance) &&
                SceneManager.GetActiveScene().handle == sceneHandle && !IsPlayerAlly() && !IsPlayerOutOfLeashRange();
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

                // 只使用物品表确认过的官方燃烧弹技能。
                Skill_Grenade grenadeSkill = FindIncendiaryGrenadeSkill();
                Grenade grenadePrefab = grenadeSkill != null ? grenadeSkill.grenadePfb : null;

                if (grenadePrefab != null)
                {
                    // 使用预制体创建
                    Grenade grenade = UnityEngine.Object.Instantiate(grenadePrefab, startPos, Quaternion.identity);

                    // 设置伤害信息
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = 30f;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                    grenade.damageInfo = dmgInfo;

                    // 对齐官方 Skill_Grenade.OnRelease：这些参数在技能上，不能只克隆 Grenade。
                    grenade.createExplosion = grenadeSkill.createExplosion;
                    grenade.explosionShakeStrength = grenadeSkill.explosionShakeStrength;
                    grenade.damageRange = grenadeSkill.SkillContext.effectRange;
                    grenade.delayFromCollide = grenadeSkill.delayFromCollide;
                    grenade.delayTime = grenadeSkill.delay;
                    grenade.isLandmine = grenadeSkill.isLandmine;
                    grenade.landmineTriggerRange = grenadeSkill.landmineTriggerRange;

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
        /// 查找官方燃烧弹技能（使用缓存）
        /// </summary>
        private Skill_Grenade FindIncendiaryGrenadeSkill()
        {
            // 直接返回缓存的技能（已在Initialize时预缓存）
            return cachedGrenadeSkill;
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
