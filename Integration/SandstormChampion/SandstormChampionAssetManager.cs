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

    }
}
