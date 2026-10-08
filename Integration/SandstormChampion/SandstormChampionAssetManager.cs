using UnityEngine;

namespace BossRush
{
    /// <summary>冠军全部由共享粒子材质生成；每只实例持有自己的外形，没有 bundle 或静态缓存。</summary>
    internal static class SandstormChampionAssetManager
    {
        internal static void ConfigureDustNoise(ParticleSystem particles, float strength)
        {
            if (particles == null) return;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.45f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        internal static SandstormChampionBody CreateBody(CharacterMainControl boss)
        {
            return SandstormChampionBody.Attach(boss, CampaignTuning.FinalBossScale);
        }

        internal static GameObject CreateNovaWarning(Vector3 position, float radius)
        {
            SandstormGroundField field = SandstormGroundField.Create(null, radius);
            field.transform.position = position + Vector3.up * 0.04f;
            return field.gameObject;
        }

        /// <summary>
        /// 只在主玩家离 source 不远时给一记相机冲量，强度随距离线性衰减；方向从源指向玩家。
        /// 走官方 CameraShaker 的爆炸通道，暂停 / 拍照由官方处理。
        /// </summary>
        internal static void ShakeNear(Vector3 source, float strength, float radius)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || radius <= 0f) return;
                Vector3 delta = player.transform.position - source;
                float falloff = 1f - Mathf.Clamp01(delta.magnitude / radius);
                if (falloff <= 0.01f) return;
                delta.y = 0f;
                Vector3 dir = delta.sqrMagnitude > 0.01f ? delta.normalized : Vector3.forward;
                CameraShaker.Shake((dir + Vector3.up * 0.3f) * (strength * falloff), CameraShaker.CameraShakeTypes.explosion);
            }
            catch { /* 相机未就绪 */ }
        }

        /// <summary>贴地砂爆：一圈先快后慢推开的砂环 + 一圈细冲击环，冲锋起步、沙柱成形、转阶段共用。</summary>
        internal static void PlaySandBurst(Vector3 ground, float radius, int dustCount, Color ringColor)
        {
            try
            {
                BossRushFxBurst dust = BossRushFxKit.Dust(SandstormChampionConfig.Sand, dustCount);
                dust.SpeedMin = radius * 1.4f;
                dust.SpeedMax = radius * 2.4f;
                dust.SizeMin = 0.5f + radius * 0.12f;
                dust.SizeMax = 0.9f + radius * 0.2f;
                dust.ShapeRadius = Mathf.Max(0.3f, radius * 0.2f);
                dust.Drag = 3.2f;
                dust.LifeMin = 0.7f;
                dust.LifeMax = 1.2f;
                dust.FastGrow = true;
                BossRushFxKit.PlayBurst(ground + Vector3.up * 0.1f, dust);
                BossRushFxKit.PlayBurst(ground + Vector3.up * 0.06f,
                    BossRushFxKit.Shockwave(ringColor, Mathf.Max(0.6f, radius * 0.4f), 5f, 0.45f, true));
            }
            catch { /* 表现失败不影响战斗 */ }
        }

    }
}
