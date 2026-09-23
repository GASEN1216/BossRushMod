// ============================================================================
// DragonKingBossGunProjectileAgent_Fx.cs - 龙皇铳弹体的尾迹摘离与冰屑共享发射器
// ============================================================================
// 2026-09-23 特效审美审查 VB-16 / VB-18 新写的两块表现代码。主文件在 LargeFileBudgetGuard 的存量白名单上
// （只许缩不许涨），放在同一 partial 的这个文件里；冰碎发射器的建造仍在主文件（DragonKingBossGunIceBladeFxGuard 钉着）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DragonKingBossGunProjectileAgent
    {
        private static readonly List<TrailRenderer> detachTrailBuffer = new List<TrailRenderer>(4);
        private static readonly List<ParticleSystem> detachParticleBuffer = new List<ParticleSystem>(8);

        /// <summary>
        /// VB-18：弹体死亡时把自定义尾迹从弹体上摘下来留在世界里淡完。官方 Projectile.Release 走对象池会停用弹体，
        /// 挂在它下面的 TrailRenderer 与粒子同一帧就看不见了（尾迹像被剪刀剪断）；OnDisable 里的 Destroy(…, 2f) 延时并不起作用，
        /// 而 OnDisable 里又不能改父子关系，所以在死亡处理入口做。拖尾停止延伸、粒子停发射，最长 0.9 s 后销毁；下一发照旧新建。
        /// </summary>
        private void DetachTrailForFade()
        {
            GameObject trail = customTrailInstance;
            if (trail == null)
            {
                return;
            }

            customTrailInstance = null;
            try
            {
                trail.transform.SetParent(null, true);
                float linger = 0.2f;
                trail.GetComponentsInChildren(true, detachTrailBuffer);
                for (int i = 0; i < detachTrailBuffer.Count; i++)
                {
                    TrailRenderer renderer = detachTrailBuffer[i];
                    if (renderer == null) continue;
                    renderer.emitting = false;
                    linger = Mathf.Max(linger, renderer.time);
                }
                trail.GetComponentsInChildren(true, detachParticleBuffer);
                for (int i = 0; i < detachParticleBuffer.Count; i++)
                {
                    if (detachParticleBuffer[i] != null)
                    {
                        detachParticleBuffer[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
                UnityEngine.Object.Destroy(trail, Mathf.Min(0.9f, linger + 0.5f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonGun] [WARNING] 尾迹摘离失败: " + e.Message);
                UnityEngine.Object.Destroy(trail);
            }
            finally
            {
                detachTrailBuffer.Clear();
                detachParticleBuffer.Clear();
            }
        }

        // VB-16：冰穿 / 冰碎原先每次命中 new GameObject + AddComponent<ParticleSystem> + Destroy。改成各一个世界空间的共享发射器，
        // 挪到命中点、转到命中法线再 Emit 一次：已发出的冰屑留在原地，不跟着发射器走；只在第一次命中时懒建（仍按手持门控，§4.12）。
        private static ParticleSystem icePierceEmitter;
        private static ParticleSystem iceShatterEmitter;

        private static void ClearIceEmitters()
        {
            if (icePierceEmitter != null) UnityEngine.Object.Destroy(icePierceEmitter.gameObject);
            if (iceShatterEmitter != null) UnityEngine.Object.Destroy(iceShatterEmitter.gameObject);
            icePierceEmitter = null;
            iceShatterEmitter = null;
        }

        private static void EmitAt(ParticleSystem ps, Vector3 point, Vector3 normal, int count)
        {
            if (ps == null) return;
            ps.transform.SetPositionAndRotation(point, normal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(normal.normalized, Vector3.up)
                : Quaternion.identity);
            // 发射模块关着，Play 只是让系统处在模拟中；已经在播时不重复调用。
            if (!ps.isPlaying) ps.Play();
            ps.Emit(count);
        }

        private void SpawnIcePierceEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (icePierceEmitter == null) icePierceEmitter = BuildIcePierceEmitter();
            EmitAt(icePierceEmitter, hitPoint, hitNormal, 14);
        }

        private static ParticleSystem BuildIcePierceEmitter()
        {
            GameObject iceFx = new GameObject("DragonGun_IcePierceFx");

            ParticleSystem ps = iceFx.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.16f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.095f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.9f, 1f, 1f, 0.88f),
                new Color(0.42f, 0.78f, 1f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 72;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.045f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(new Color(0.9f, 1f, 1f), 0f), new GradientColorKey(new Color(0.45f, 0.75f, 1f), 0.55f), new GradientColorKey(new Color(0.2f, 0.45f, 1f), 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.08f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.35f;
            renderer.velocityScale = 0.22f;
            renderer.sharedMaterial = GetOrCreateIceMaterial();
            return ps;
        }
    }
}
