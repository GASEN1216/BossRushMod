// ============================================================================
// PetNestAuraEffect.cs - 崽身上的炫彩 / 异色粒子光环（owner 需求 14）
// ============================================================================
// owner 口径：「崽们也要带上自己的炫彩和异色的特效，在他们自己身上……
//              不同炫彩弄不同的粒子特效，异色则是最豪华的最好看的。」
//
// 做法：复用龙息 / 霜雾同一条管线 Common/Effects/RingParticleEffect
//   （共享白色粒子材质与贴图，着色只走 startColor / colorOverLifetime，
//    不新建材质、不新增 shader，因此不受 URP Deferred 的 GBuffer pass 约束）。
//   炫彩 = 两层光环，一层一色，半径与高度错开，转起来是两色交织；
//   异色 = 一层高密度金色光环 + 一盏跟随点光，最显眼的那一档。
//
// 硬约束（AGENTS 4.12 重运行时工作按实际使用状态门控）：
//   - 只有**真的带了炫彩或异色**的崽才会创建对象；普通崽零对象、零每帧成本；
//   - 由 PetNestCompanionSpawner 在激活随从时创建、回收随从时 StopEffect；
//     随从 GameObject 销毁时光环作为其子节点一并销毁，不会漏；
//   - 基类的 Update 只做一次位置跟随，LateUpdate 只 Emit 固定几颗，不扫描场景。
// ============================================================================

using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>崽的外观光环。一实例一色，炫彩用两个实例叠。</summary>
    public class PetNestAuraEffect : RingParticleEffect
    {
        private Color _tint = Color.white;
        private int _emitterCount = 4;
        private float _emitterRadius = 0.35f;
        private float _alpha = 0.3f;
        private float _size = 0.7f;
        private float _rate = 6f;
        private float _lifetime = 0.7f;
        private Vector3 _followOffset = new Vector3(0f, 0.1f, 0f);

        protected override int EmitterCount { get { return _emitterCount; } }
        protected override float EmitterRadius { get { return _emitterRadius; } }
        protected override bool EnableWorldEmitters { get { return false; } }
        protected override int LocalMaxParticles { get { return 48; } }
        protected override float LocalLifetime { get { return _lifetime; } }
        protected override float LocalSpeed { get { return 0.18f; } }
        protected override float LocalSize { get { return _size; } }
        protected override float LocalAlpha { get { return _alpha; } }
        protected override float LocalEmissionRate { get { return _rate; } }
        protected override Vector3 FollowOffset { get { return _followOffset; } }
        protected override Color ParticleTint { get { return _tint; } }

        /// <summary>
        /// 配置一层光环。必须在 Create 之后、基类 Start 之前调用
        /// （Create 只 AddComponent，Start 要等到下一帧才跑，因此紧接着调是安全的）。
        /// </summary>
        internal void Configure(Color tint, int emitterCount, float radius, float alpha,
            float size, float rate, float lifetime, Vector3 followOffset)
        {
            _tint = tint;
            _emitterCount = emitterCount;
            _emitterRadius = radius;
            _alpha = alpha;
            _size = size;
            _rate = rate;
            _lifetime = lifetime;
            _followOffset = followOffset;
        }
    }
}
