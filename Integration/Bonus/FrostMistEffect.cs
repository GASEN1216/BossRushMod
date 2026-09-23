// ============================================================================
// FrostMistEffect.cs - 冰霜套装脚下霜雾
// ============================================================================
// 模块说明：
//   继承 Common/Effects/RingParticleEffect（与飞行图腾的 FlightCloudEffect 同族），
//   作为「寒冰之护」激活期间的常驻 aura。由 FrostSetBonus 在激活时 Create、停用时 StopEffect
//   （停发射、等粒子自然走完再自销毁）。
//
// 2026-09-23 审美修（VA-02）：
//   旧配方是 4 个 Local 发射器、每个 40 颗 0.72–1.17 m 的软球，速度 0.15 m/s × 寿命 0.6 s 几乎不动，
//   再叠共享材质的 ×2，脚下是一块发光的淡蓝实心饼——与 owner 骂过的异色「一大团黄雾」同一个配方。
//   现在两层，整件活粒子不超过 24 颗：
//     - 贴地薄霜：3 个 World 空间发射器，烟缕贴图、水平公告板、0.28–0.45 m、有效 alpha 0.15，
//       缓慢向外化开、由浅冰白变成冰蓝；World 空间，走动时身后留一小段霜迹，不再是跟着人走的硬饼；
//     - 冰晶：一个 World 空间发射器，0.035–0.055 m 的星芒，HDR 高档，上飘并一闪一闪。
//   门控不变：只在穿齐套装时创建，脱下即停（AGENTS 4.12）。
// ============================================================================

using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// 冰霜套装霜雾 - 玩家脚下的贴地薄霜 + 冰晶闪烁
    /// </summary>
    public class FrostMistEffect : RingParticleEffect
    {
        protected override int EmitterCount => 3;
        protected override float EmitterRadius => 0.35f;
        protected override float EmitterRandomOffset => 0.08f;
        protected override float EmitterHeightJitter => 0.015f;
        // 贴地 5 cm：高了像悬浮的圆饼，低了被地面起伏吃掉（天空岛撤离环吃过这个亏）
        protected override Vector3 FollowOffset => new Vector3(0f, 0.05f, 0f);
        protected override bool EnableLocalEmitters => false;
        protected override bool EnableWorldEmitters => true;

        protected override int WorldMaxParticles => 6;
        protected override float WorldLifetime => 2f;
        protected override float WorldSpeed => 0.05f;
        protected override float WorldSize => 0.45f;
        protected override float WorldSizeMin => 0.28f;
        // 共享材质已是 1×：这就是实际不透明度（淡淡一层霜，不盖住地面纹理）
        protected override float WorldAlpha => 0.15f;
        protected override float WorldEmissionRate => 2.5f;
        protected override float WorldShapeRadius => 0.12f;

        protected override Color ParticleTint => new Color(0.85f, 0.95f, 1f);
        // 末端乘子：(0.85,0.95,1) × 这个 ≈ (0.55,0.75,0.92)，由浅冰白沉成冰蓝
        protected override Color LifetimeEndColor => new Color(0.65f, 0.79f, 0.92f);
        protected override AnimationCurve SizeOverLifetimeCurve => AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f);
        protected override ParticleSystemRenderMode ParticleRenderMode => ParticleSystemRenderMode.HorizontalBillboard;
        protected override float RadialSpeed => 0.12f;
        protected override bool RandomStartRotation => true;

        /// <summary>冰晶层的活粒子上限。</summary>
        private const int GlintMaxParticles = 6;

        protected override Material CreateMaterial()
        {
            // 烟缕形状：边缘带噪声，贴地时读成一片霜气而不是一枚圆片
            Material wisp = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            return wisp != null ? wisp : base.CreateMaterial();
        }

        protected override void OnEmittersCreated(Transform root)
        {
            Material star = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Star, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            ParticleSystem glints = BossRushFxKit.CreateEmitter("FrostGlints", root, new Vector3(0f, 0.25f, 0f), star, GlintMaxParticles, true);
            if (glints == null) return;

            ParticleSystem.MainModule main = glints.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.055f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule emission = glints.emission;
            emission.rateOverTime = 3.5f;

            ParticleSystem.ShapeModule shape = glints.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            ParticleSystem.VelocityOverLifetimeModule velocity = glints.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.ColorOverLifetimeModule color = glints.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(
                new Color(1f, 1f, 1f, 1f), new Color(0.75f, 0.9f, 1f, 0.9f), new Color(0.55f, 0.78f, 1f, 0f), 0.2f);

            // 一闪一闪：尺寸亮—暗—亮—灭（同遗种巢冰晶的节奏）
            ParticleSystem.SizeOverLifetimeModule size = glints.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.15f, 1f),
                new Keyframe(0.4f, 0.45f),
                new Keyframe(0.62f, 0.95f),
                new Keyframe(1f, 0f)));

            glints.Play();
        }
    }
}
