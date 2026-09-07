// ============================================================================
// FrostMistEffect.cs - 冰霜套装脚下霜雾
// ============================================================================
// 模块说明：
//   继承 Common/Effects/RingParticleEffect（与飞行图腾的 FlightCloudEffect 同族），
//   只保留 4 个 Local 发射器、少量淡蓝粒子，作为「寒冰之护」激活期间的常驻 aura。
//   由 FrostSetBonus 在激活时 Create、停用时 StopEffect（淡出后自销毁）。
// ============================================================================

using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// 冰霜套装霜雾 - 玩家脚下低密度淡蓝粒子
    /// </summary>
    public class FrostMistEffect : RingParticleEffect
    {
        protected override int EmitterCount => 4;
        protected override float EmitterRadius => 0.5f;
        protected override bool EnableWorldEmitters => false;

        protected override int LocalMaxParticles => 40;
        protected override float LocalLifetime => 0.6f;
        protected override float LocalSpeed => 0.15f;
        protected override float LocalSize => 0.9f;
        // 0.45² —— 基类改为单次施加 alpha 后，按旧的实际生效值重写，霜雾浓度不变。
        protected override float LocalAlpha => 0.2025f;
        protected override float LocalEmissionRate => 5f;

        protected override Color ParticleTint => new Color(0.72f, 0.9f, 1f);
    }
}
