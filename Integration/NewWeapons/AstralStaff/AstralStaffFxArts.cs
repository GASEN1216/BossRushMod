// ============================================================================
// AstralStaffFxArts.cs - 星阙三档重击的分层表现与瞬时 owner 解析
// ============================================================================
// owner 2026-10-08：「三个阶段的特效做得有层次感一点」。三档各自按先后分层，层与层错开几十毫秒：
//   一豆 · 流星横扫：前摇符轮 → 金色主刃 + 白热刃口 → 0.05 s 后放大变淡的残影刃 → 弧上甩出的流星光痕
//                    → 贴地新月冲击环与扬尘。金色单色调，利落。
//   二豆 · 双龙回旋：每段一条主色光环（首段金、次段青）→ 一条绕身上旋一圈半的星屑「龙身」→ 贴地主色环，
//                    次段另有向内收拢的青环（卷近）。双色交替，读得出两次节拍。
//   三豆 · 星陨天崩：前摇落点收拢环 + 流星坠落（控制器）→ 落地星芒亮芯 → 细白芯光柱一闪、金柱接上
//                    → 光带环、符轮、地裂纹、贴地四射星屑、碎片与尘环 → 0.14 s 后冷青余震环与第二层扬尘
//                    → 上浮余烬。金白冷青三色，层数最多。
// 瞬时 owner：默认是玩家手持的 AstralStaffController 根；沙暴棍卫用 WithRoot 指定自己的根，
// 延迟层（After）回调时也回到创建它的那个根下，不会落到别人的 owner 上。单根最多 64 个对象。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal static partial class AstralStaffFx
    {
        private static Transform scopedRoot;

        /// <summary>当前生效的瞬时根：WithRoot 指定的优先，其次是玩家手持 owner；满 64 返回 null。</summary>
        internal static Transform ResolveRoot()
        {
            if (scopedRoot != null) return scopedRoot.childCount < 64 ? scopedRoot : null;
            AstralStaffController owner = AstralStaffController.Instance;
            return owner != null ? owner.GetTransientFxRoot() : null;
        }

        /// <summary>在指定根下执行一段表现（沙暴棍卫的重击、延迟层回调）。表现异常不外抛。</summary>
        internal static void WithRoot(Transform root, Action body)
        {
            if (root == null || body == null) return;
            Transform previous = scopedRoot;
            scopedRoot = root;
            try { body(); }
            catch (Exception e) { ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " 表现层异常: " + e.Message); }
            finally { scopedRoot = previous; }
        }

        /// <summary>seconds 秒（未缩放时间，暂停时停）后在同一个 owner 根下补一层表现。</summary>
        internal static void After(float seconds, Action action)
        {
            GameObject go = CreateTransient("AstralStaff_Layer");
            if (go == null) return;
            go.AddComponent<AstralStaffFxDelay>().Init(seconds, action);
        }

        internal static void ResetScopedRoot()
        {
            scopedRoot = null;
        }

        // ---------------- 一豆 · 流星横扫 ----------------

        /// <summary>横扫出手的全部层（前摇符轮由调用方放）。center 为腰高圆心。</summary>
        internal static void PlaySweepArt(Vector3 feet, Vector3 dir, float radius, float halfAngle, Color accent)
        {
            try
            {
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                dir.Normalize();
                Vector3 center = feet + Vector3.up * 0.9f;
                float sweep = halfAngle * 2f;
                PlayStroke(center, dir, sweep, radius, accent, 0.085f, 0.24f);
                After(0.05f, () =>
                {
                    Color echo = Color.Lerp(accent, AstralStaffConfig.CoreWhite, 0.4f);
                    echo.a = 0.24f;
                    AstralStaffRibbon.Play(center + Vector3.up * 0.06f, dir, sweep * 0.96f, radius * 0.9f, radius * 1.14f,
                        echo, 0.08f, 0.02f, 0.32f, 0.05f, 0f, null);
                });
                PlayMeteorStreaks(center, dir, sweep, radius, accent, 5);
                Color ring = accent;
                ring.a = 0.45f;
                PlayBurst(feet + Vector3.up * 0.05f, BossRushFxKit.Shockwave(ring, 1.2f, radius * 1.3f, 0.3f, true));
                PlayDust(feet, 6, 1.2f);
            }
            catch { /* 表现失败不影响伤害 */ }
        }

        /// <summary>流星光痕：沿弧均布几处，按切线方向甩出带拖尾的细光，先快后慢。</summary>
        internal static void PlayMeteorStreaks(Vector3 center, Vector3 dir, float degrees, float radius, Color accent, int count)
        {
            float sign = degrees < 0f ? -1f : 1f;
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                float angle = -degrees * 0.5f + degrees * t;
                Vector3 radial = Quaternion.Euler(0f, angle, 0f) * dir;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial) * sign;
                BossRushFxBurst streak = BossRushFxKit.Sparks(accent, 2);
                streak.Cone = 10f;
                streak.Direction = tangent + radial * 0.35f + Vector3.up * 0.1f;
                streak.SpeedMin = 9f;
                streak.SpeedMax = 14f;
                streak.SizeMin = 0.05f;
                streak.SizeMax = 0.08f;
                streak.LifeMin = 0.18f;
                streak.LifeMax = 0.28f;
                streak.Drag = 4f;
                streak.Stretch = 0.09f;
                streak.Trail = 0.55f;
                PlayBurst(center + radial * (radius * 0.96f), streak);
            }
        }

        // ---------------- 二豆 · 双龙回旋 ----------------

        /// <summary>回旋一段的全部层。second=true 为第二段（青色、反向、外扩一点并带收拢环）。</summary>
        internal static void PlaySpinArt(Vector3 feet, Vector3 dir, float radius, bool second, Transform follow)
        {
            try
            {
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                dir.Normalize();
                Color main = second ? AstralStaffConfig.Cyan : AstralStaffConfig.Gold;
                Vector3 center = feet + Vector3.up * (second ? 1.07f : 0.95f);
                PlayStroke(center, second ? -dir : dir, second ? -360f : 360f, second ? radius + 0.2f : radius,
                    main, second ? 0.075f : 0.095f, second ? 0.32f : 0.22f, follow);
                PlayHelix(feet, radius * 0.55f, main, second ? -1f : 1f, 22);
                Color ring = main;
                ring.a = 0.5f;
                PlayBurst(feet + Vector3.up * 0.05f, BossRushFxKit.Shockwave(ring, 1f, radius * 2.1f, 0.34f, true));
                if (second)
                {
                    BossRushFxBurst gather = BossRushFxKit.Shockwave(new Color(0.42f, 0.7f, 0.77f, 0.55f),
                        (radius + 0.6f) * 2f, 0.2f, 0.32f, true);
                    PlayBurst(feet + Vector3.up * 0.07f, gather);
                    AstralStaffRibbon.Play(feet + Vector3.up * 0.08f, dir, 360f, radius - 0.3f, radius - 0.23f,
                        new Color(0.95f, 0.73f, 0.36f, 0.38f), 0.04f, 0.02f, 0.36f, 0.1f, 0.72f, null);
                }
                PlayDust(feet, 7, 1.3f);
            }
            catch { /* 同上 */ }
        }

        /// <summary>龙身：一串沿螺旋上升的星屑，从脚下绕到肩高，一圈半；spin 为 ±1 决定旋向。</summary>
        internal static void PlayHelix(Vector3 feet, float radius, Color accent, float spin, int count)
        {
            GameObject go = null;
            try
            {
                go = CreateTransient("AstralStaff_DragonHelix");
                if (go == null) return;
                ParticleSystem ps = BossRushFxKit.CreateEmitter("Motes", go.transform, Vector3.zero,
                    Additive(BossRushParticleShape.GlowDot, BossRushFxKit.GainHot), count, true);
                if (ps == null) { UnityEngine.Object.Destroy(go); return; }
                ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
                color.enabled = true;
                Color fade = accent;
                fade.a = 0f;
                color.color = BossRushFxKit.FadeGradient(AstralStaffConfig.CoreWhite, accent, fade, 0.05f);
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.05f;
                    renderer.lengthScale = 1f;
                }
                ps.Play();
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)Mathf.Max(1, count - 1);
                    float angle = spin * t * Mathf.PI * 3f;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * spin;
                    emit.position = feet + radial * (radius * (1f - 0.25f * t)) + Vector3.up * (0.2f + 1.6f * t);
                    emit.velocity = tangent * 3.2f + Vector3.up * 1.4f;
                    emit.startSize = 0.07f - 0.03f * t;
                    emit.startLifetime = 0.3f + 0.25f * t;
                    emit.startColor = Color.white;
                    ps.Emit(emit, 1);
                }
                BossRushFxKit.Release(go, 0f, 1f, false);
            }
            catch { if (go != null) UnityEngine.Object.Destroy(go); }
        }

        // ---------------- 三豆 · 星陨天崩 ----------------

        /// <summary>星陨落地的整套表现：光带冲击环、符轮、光柱、地裂纹、砸痕、亮芯、冲击波、火花、星屑、余烬、尘环与闪光。</summary>
        internal static void PlayStarfallImpact(Vector3 origin, Vector3 blast, Vector3 dir, bool contact, float blastRadius)
        {
            try
            {
                Vector3 ground = blast + Vector3.up * 0.06f;
                float scale = blastRadius / AstralStaffConfig.StarfallBlastRadius;
                // 冲击以外缘传播，落点仍看得见；大面积加色只会抹掉敌人的受击动作。
                AstralStaffRibbon.Play(ground, dir, 360f, blastRadius * 1.3f - 0.07f,
                    blastRadius * 1.3f, new Color(1f, 0.94f, 0.78f, 0.72f),
                    0.025f, 0.015f, 0.4f, 0.15f, 0.16f, null);
                AstralStaffRibbon.Play(ground + Vector3.up * 0.02f, dir, -360f, blastRadius - 0.06f,
                    blastRadius, new Color(0.42f, 0.7f, 0.77f, 0.38f),
                    0.025f, 0.02f, 0.6f, 0.2f, 0.2f, null);
                AstralStaffSigil.Play(ground + Vector3.up * 0.015f, dir, 1.75f * scale, 3, 0.015f, 0.42f);
                // 光柱分两拍：先一根细白芯一闪，紧接一根更粗更高的金柱慢慢熄，避免一根宽白柱长时间遮住中心。
                AstralStaffBeam.Create(new[] { ground, ground + Vector3.up * (5.5f * scale) }, 0.05f, 0.14f,
                    new Color(1f, 0.96f, 0.86f, 0.9f), 0.03f, 0.16f, false);
                After(0.035f, () => AstralStaffBeam.Create(new[] { ground, ground + Vector3.up * (7.5f * scale) },
                    0.08f, 0.42f, new Color(0.95f, 0.73f, 0.36f, 0.6f), 0.05f, 0.45f, false));

                // 地裂纹：从落点放射出去的折线，金色发光，慢慢熄
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f + UnityEngine.Random.Range(-10f, 10f);
                    Vector3 crackDir = Quaternion.Euler(0f, angle, 0f) * dir;
                    Vector3[] pts = new Vector3[5];
                    float length = UnityEngine.Random.Range(2.2f, 3.6f) * scale;
                    Vector3 side = Vector3.Cross(Vector3.up, crackDir);
                    for (int p = 0; p < pts.Length; p++)
                    {
                        float f = p / (float)(pts.Length - 1);
                        float jitter = p == 0 ? 0f : UnityEngine.Random.Range(-0.3f, 0.3f);
                        pts[p] = ground + crackDir * (length * f) + side * jitter;
                    }
                    AstralStaffBeam.Create(pts, 0.018f, 0.065f, new Color(0.8f, 0.52f, 0.22f, 0.62f), 0.16f, 0.65f, true);
                }

                // 一条砸痕：从人到落点
                AstralStaffBeam.Create(new[] { origin + Vector3.up * 0.05f, ground }, 0.035f, 0.14f,
                    new Color(0.95f, 0.73f, 0.36f, 0.64f), 0.07f, 0.5f, true);

                // 落地那一帧：一颗压过画面的星芒亮芯，再一圈先快后慢的贴地冲击波推到伤害圈外缘。
                PlayBurst(blast + Vector3.up * 0.7f,
                    BossRushFxKit.Glint(AstralStaffConfig.CoreWhite, contact ? 3.4f : 2.6f, 0.13f));
                PlayBurst(ground + Vector3.up * 0.02f, BossRushFxKit.Shockwave(
                    new Color(1f, 0.86f, 0.55f, 0.85f), 1.2f, blastRadius * 2.7f / 1.2f, 0.42f, true));

                BossRushFxBurst sparks = BossRushFxKit.Sparks(AstralStaffConfig.Gold, contact ? 32 : 20);
                sparks.SpeedMin = 6f;
                sparks.SpeedMax = 13f;
                sparks.Stretch = 0.06f;
                sparks.SizeMax = 0.07f;
                sparks.LifeMax = 0.42f;
                sparks.Upward = true;
                sparks.Gravity = 1.4f;
                PlayBurst(blast + Vector3.up * 0.3f, sparks);

                // 被砸起的星屑碎片：带重力翻滚落回地面，金色加色，不做灰色碎石。
                BossRushFxBurst shards = BossRushFxKit.Sparks(AstralStaffConfig.Gold, contact ? 16 : 10);
                shards.Shape = BossRushParticleShape.Shard;
                shards.Gain = BossRushFxKit.GainBright;
                shards.Stretch = 0f;
                shards.Upward = true;
                shards.ShapeRadius = 0.6f;
                shards.SizeMin = 0.09f;
                shards.SizeMax = 0.2f;
                shards.SpeedMin = 4f;
                shards.SpeedMax = 8.5f;
                shards.LifeMin = 0.6f;
                shards.LifeMax = 1f;
                shards.Drag = 1.2f;
                shards.Gravity = 2.4f;
                shards.GrowTo = 0.6f;
                shards.Spin = 540f;
                shards.End = AstralStaffConfig.GoldFade;
                PlayBurst(blast + Vector3.up * 0.2f, shards);

                // 余烬：落点上方缓缓升起、慢慢熄灭的星点，给冲击之后留一口气。
                BossRushFxBurst embers = BossRushFxKit.Sparks(AstralStaffConfig.Gold, 18);
                embers.Stretch = 0f;
                embers.Upward = true;
                embers.ShapeRadius = blastRadius * 0.7f;
                embers.SizeMin = 0.03f;
                embers.SizeMax = 0.06f;
                embers.SpeedMin = 0.5f;
                embers.SpeedMax = 1.6f;
                embers.LifeMin = 0.9f;
                embers.LifeMax = 1.6f;
                embers.Drag = 1f;
                embers.Gravity = -0.08f;
                embers.GrowTo = 0.3f;
                embers.FadeIn = 0.1f;
                embers.End = AstralStaffConfig.CyanFade;
                PlayBurst(ground, embers);

                BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.62f, 0.52f, 0.36f, 0.34f), contact ? 18 : 14);
                dust.SpeedMin = 6f;
                dust.SpeedMax = 9.5f;
                dust.SizeMin = 0.6f;
                dust.SizeMax = 1.1f;
                dust.ShapeRadius = 0.8f;
                dust.LifeMin = 0.6f;
                dust.LifeMax = 1f;
                dust.Drag = 4.5f;
                dust.FastGrow = true;
                PlayBurst(blast, dust);
                PlayFlash(blast + Vector3.up * 0.8f, AstralStaffConfig.Gold, contact ? 3.2f : 1.6f, 6.5f, 0.3f);

                // 星屑贴地四射：一圈带拖尾的细光沿地面飞出，读得出「天崩」的外扩力。
                BossRushFxBurst shardsOut = BossRushFxKit.Sparks(AstralStaffConfig.CoreWhite, contact ? 16 : 12);
                shardsOut.Radial = true;
                shardsOut.ShapeRadius = 0.4f;
                shardsOut.SpeedMin = 11f * scale;
                shardsOut.SpeedMax = 17f * scale;
                shardsOut.LifeMin = 0.22f;
                shardsOut.LifeMax = 0.34f;
                shardsOut.Drag = 3f;
                shardsOut.Stretch = 0.07f;
                shardsOut.Trail = 0.45f;
                shardsOut.Main = new Color(1f, 0.86f, 0.55f, 0.9f);
                PlayBurst(ground + Vector3.up * 0.25f, shardsOut);

                // 余震：0.14 秒后一圈冷青环从伤害圈内侧再推一次，冲击有了「两拍」。
                After(0.14f, () =>
                {
                    PlayBurst(ground + Vector3.up * 0.03f, BossRushFxKit.Shockwave(
                        new Color(0.42f, 0.7f, 0.77f, 0.6f), blastRadius * 1.2f, 2.3f, 0.55f, true));
                    BossRushFxBurst haze = BossRushFxKit.Dust(new Color(0.7f, 0.62f, 0.46f, 0.22f), 10);
                    haze.SpeedMin = 3f;
                    haze.SpeedMax = 5f;
                    haze.SizeMin = 0.9f;
                    haze.SizeMax = 1.5f;
                    haze.ShapeRadius = blastRadius * 0.5f;
                    haze.LifeMin = 0.9f;
                    haze.LifeMax = 1.3f;
                    PlayBurst(ground, haze);
                });
            }
            catch { /* 表现失败不影响伤害 */ }
        }

    }

    /// <summary>延迟一层表现：到点后在自己所属的 owner 根下执行，然后自毁。暂停时停表。</summary>
    internal sealed class AstralStaffFxDelay : MonoBehaviour
    {
        private float _remaining;
        private Action _action;

        internal void Init(float seconds, Action action)
        {
            _remaining = Mathf.Max(0f, seconds);
            _action = action;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining > 0f) return;
            Action action = _action;
            _action = null;
            Transform root = transform.parent;
            Destroy(gameObject);
            if (action != null && root != null) AstralStaffFx.WithRoot(root, action);
        }
    }
}
