// ============================================================================
// PhantomWitchCurseSweatVfx.cs - 诅咒Buff流汗特效（水珠爆裂+自由落体）
// ============================================================================
// 模块说明：
//   当角色被施加幽灵诅咒 Buff 时，挂载到角色身上的轻量 MonoBehaviour。
//   从角色身体中心持续爆出半透明水珠粒子，水珠受重力自由落体至地面后消失。
//   Buff 消失后自动停止发射并在粒子播完后自毁。
//
//   覆盖路径：
//   1. Boss 技能 / 诅咒领域 → 显式 TryAttach（在 AddBuff 旁）
//   2. 玩家普攻 buffChance → Health.OnHurt 全局回调自动检测
// ============================================================================

using System;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 诅咒流汗特效：从身体中心爆出水珠，自由落体到地面消失。
    /// 调用 <see cref="TryAttach"/> 或由全局 OnHurt hook 自动挂载。
    /// </summary>
    internal sealed class PhantomWitchCurseSweatVfx : MonoBehaviour
    {
        // ========== 粒子外观参数 ==========

        private static readonly Color DropletColorStart = new Color(0.45f, 0.70f, 1.00f, 1.00f);
        private static readonly Color DropletColorEnd = new Color(0.30f, 0.55f, 0.95f, 0.00f);

        private const float DropletSizeMin = 0.12f;
        private const float DropletSizeMax = 0.22f;
        private const float DropletLifetime = 0.6f;
        private const float DropletGravity = 1.8f;
        private const float BurstSpeedMin = 1.2f;
        private const float BurstSpeedMax = 2.8f;
        private const float EmissionRate = 18f;
        private const int MaxParticles = 48;

        /// <summary>
        /// 粒子发射源相对角色 pivot 的 Y 轴偏移（大约在身体中心）
        /// </summary>
        private const float EmitHeightOffset = 0.9f;

        /// <summary>
        /// 创建后的宽限期（秒），在此期间不检查 HasBuff，
        /// 避免引擎 buff 应用顺序导致的首帧误判。
        /// </summary>
        private const float BuffCheckGracePeriod = 0.2f;

        /// <summary>
        /// HasBuff 检查频率（秒），避免每帧反射调用。
        /// </summary>
        private const float BuffCheckInterval = 0.25f;

        // ========== 全局 Hook 状态 ==========

        private static bool hookRegistered = false;

        // ========== 运行时状态 ==========

        private ParticleSystem particleSys;
        private float createTime;
        private float lastBuffCheckTime;
        private bool stopping;
        private CharacterMainControl cachedCharacter;

        // ========== 全局 Hook ==========

        /// <summary>
        /// 注册 Health.OnHurt 全局回调。应在噬魂挽歌初始化时调用一次。
        /// 幂等：多次调用不会重复注册。
        /// </summary>
        public static void RegisterGlobalHook()
        {
            if (hookRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt += OnGlobalHurt;
                hookRegistered = true;
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] Health.OnHurt 全局回调已注册");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 注册 Health.OnHurt 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注销全局回调（场景清理时调用）。
        /// </summary>
        public static void UnregisterGlobalHook()
        {
            if (!hookRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt -= OnGlobalHurt;
            }
            catch
            {
            }
            hookRegistered = false;
        }

        private static void OnGlobalHurt(Health hurtHealth, DamageInfo damageInfo)
        {
            if (hurtHealth == null)
            {
                return;
            }

            // 只关心噬魂挽歌造成的伤害
            if (damageInfo.fromWeaponItemID != PhantomWitchScytheIds.WeaponTypeId)
            {
                return;
            }

            // 检查被击角色是否真的有诅咒 Buff
            try
            {
                CharacterMainControl character = hurtHealth.TryGetCharacter();
                if (character == null)
                {
                    return;
                }

                CharacterBuffManager buffMgr = character.GetBuffManager();
                if (buffMgr == null || !buffMgr.HasBuff(PhantomWitchConfig.CurseBuffID))
                {
                    return;
                }

                TryAttach(character.gameObject);
            }
            catch
            {
            }
        }

        // ========== 公开接口 ==========

        /// <summary>
        /// 在受击目标身上挂载/续期流汗特效。已存在则仅续期，不会重复创建。
        /// </summary>
        public static void TryAttach(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            PhantomWitchCurseSweatVfx existing = target.GetComponent<PhantomWitchCurseSweatVfx>();
            if (existing != null)
            {
                existing.Refresh();
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 续期 target=" + target.name);
                return;
            }

            try
            {
                PhantomWitchCurseSweatVfx vfx = target.AddComponent<PhantomWitchCurseSweatVfx>();
                vfx.Refresh();
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 新挂载 target=" + target.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 挂载失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 续期：重置计时，恢复发射。
        /// </summary>
        public void Refresh()
        {
            if (stopping && particleSys != null)
            {
                stopping = false;
                var emission = particleSys.emission;
                emission.rateOverTime = EmissionRate;
                if (!particleSys.isPlaying)
                {
                    particleSys.Play();
                }
            }
        }

        // ========== 生命周期 ==========

        private void Awake()
        {
            createTime = Time.time;
            lastBuffCheckTime = Time.time;

            // 缓存角色引用
            cachedCharacter = GetComponent<CharacterMainControl>();
            if (cachedCharacter == null)
            {
                cachedCharacter = GetComponentInParent<CharacterMainControl>();
            }

            CreateParticleSystem();
        }

        private void Update()
        {
            if (stopping)
            {
                // 等所有粒子播完后自毁
                if (particleSys == null || !particleSys.IsAlive(false))
                {
                    DestroySelf();
                }
                return;
            }

            // 宽限期内不检查，避免引擎 buff 还没来得及挂上
            if (Time.time - createTime < BuffCheckGracePeriod)
            {
                return;
            }

            // 按间隔检测角色是否仍持有诅咒 Buff
            if (Time.time - lastBuffCheckTime >= BuffCheckInterval)
            {
                lastBuffCheckTime = Time.time;

                if (!CharacterStillCursed())
                {
                    BeginStop();
                }
            }
        }

        private void OnDestroy()
        {
            if (particleSys != null)
            {
                try
                {
                    Destroy(particleSys.gameObject);
                }
                catch
                {
                }
                particleSys = null;
            }
        }

        // ========== Buff 状态检测 ==========

        private bool CharacterStillCursed()
        {
            if (cachedCharacter == null)
            {
                return false;
            }

            try
            {
                CharacterBuffManager buffMgr = cachedCharacter.GetBuffManager();
                if (buffMgr == null)
                {
                    return false;
                }

                return buffMgr.HasBuff(PhantomWitchConfig.CurseBuffID);
            }
            catch
            {
                return false;
            }
        }

        // ========== 粒子系统 ==========

        private void CreateParticleSystem()
        {
            try
            {
                GameObject psGo = new GameObject("PW_CurseSweat_PS");
                psGo.transform.SetParent(transform, false);
                psGo.transform.localPosition = new Vector3(0f, EmitHeightOffset, 0f);

                particleSys = psGo.AddComponent<ParticleSystem>();

                // ---------- Main ----------
                var main = particleSys.main;
                main.duration = 30f;
                main.loop = true;
                main.startLifetime = DropletLifetime;
                main.startSpeed = new ParticleSystem.MinMaxCurve(BurstSpeedMin, BurstSpeedMax);
                main.startSize = new ParticleSystem.MinMaxCurve(DropletSizeMin, DropletSizeMax);
                main.startColor = DropletColorStart;
                main.gravityModifier = DropletGravity;
                main.maxParticles = MaxParticles;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                // ---------- Emission ----------
                var emission = particleSys.emission;
                emission.rateOverTime = EmissionRate;

                // ---------- Shape：从一个很小的球体中心爆出 ----------
                var shape = particleSys.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.15f;

                // ---------- Color over Lifetime：从半透明蓝→透明 ----------
                var colorOverLifetime = particleSys.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(DropletColorStart, 0f),
                        new GradientColorKey(new Color(DropletColorEnd.r, DropletColorEnd.g, DropletColorEnd.b), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(DropletColorStart.a, 0f),
                        new GradientAlphaKey(DropletColorStart.a * 0.7f, 0.5f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                // ---------- Size over Lifetime：先膨胀后收缩（水珠拉长感） ----------
                var sizeOverLifetime = particleSys.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                AnimationCurve sizeCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f),
                    new Keyframe(0.2f, 1f),
                    new Keyframe(1f, 0.3f));
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

                // ---------- Renderer（复用 AssetManager 共享粒子材质）----------
                PhantomWitchAssetManager.ConfigureSharedParticleRenderer(particleSys);

                particleSys.Play();
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 水珠特效已创建 target=" + gameObject.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 创建粒子系统失败: " + e.Message);
            }
        }

        private void BeginStop()
        {
            stopping = true;

            if (particleSys != null)
            {
                var emission = particleSys.emission;
                emission.rateOverTime = 0f;
                particleSys.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void DestroySelf()
        {
            try
            {
                Destroy(this);
            }
            catch
            {
            }
        }
    }
}
