using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：三处居民灶火看得见的火苗与烟（程序化粒子，不重打包、没有碰撞体、不参与交互竞争）。
    ///
    /// 批次三的营火只有一盏点光，走近了也看不出「这里生着火」；内容批次四里灶火的烟是云蚋的安全区，
    /// 玩家得看得出烟往哪飘、哪儿能躲。落点不压在装置或居民站位上：复用全岛唯一经过真实几何回归的放置算法
    /// （`SkyIslandRewardCrate.TryFindCratePosition`，方位按标记名取稳定散列）退开 <see cref="SpotDistance"/> 米；
    /// 找不到净空就贴着标记下方的地面生火。调用方把灶火的点光、取暖与驱蚋的判定一并挪到这个落点：看见的火就是算数的火。
    /// 材质复用全 Mod 共享的程序化粒子材质（`RingParticleEffect.GetSharedParticleMaterial`）。
    /// </summary>
    internal static class SkyIslandHearthFx
    {
        internal const float SpotDistance = 2.4f;

        /// <summary>火堆的地面落点：标记旁一个交互间距以外、站得住、不贴墙的地方。</summary>
        internal static Vector3 FindSpot(Transform root, Transform marker, int groundMask)
        {
            Vector3 spot;
            if (SkyIslandRewardCrate.TryFindCratePosition(root, marker.position, SkyIslandLootTables.StableHash(marker.name + ":hearth") % 360,
                SpotDistance, groundMask, out spot)) return spot;
            RaycastHit hit;
            if (Physics.Raycast(marker.position + Vector3.up * 2f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return marker.position;
        }

        /// <summary>在世界坐标 <paramref name="spot"/> 生火冒烟，挂在 <paramref name="parent"/> 下（随灶火一起销毁）。</summary>
        internal static void Build(Transform parent, Vector3 spot)
        {
            GameObject fx = new GameObject("HearthFx");
            fx.transform.SetParent(parent, true);
            fx.transform.position = spot;
            Emitter(fx.transform, "Flame", new Vector3(0f, 0.1f, 0f), new Color(1f, 0.55f, 0.18f, 0.9f), 0.55f, 0.9f, 0.35f, 22f, 0.18f, 0.3f, 40);
            Emitter(fx.transform, "Core", new Vector3(0f, 0.15f, 0f), new Color(1f, 0.86f, 0.46f, 0.8f), 0.35f, 0.4f, 0.5f, 10f, 0.05f, 0.6f, 12);
            Emitter(fx.transform, "Smoke", new Vector3(0f, 0.6f, 0f), new Color(0.76f, 0.76f, 0.8f, 0.28f), 3.2f, 0.7f, 0.7f, 5f, 0.25f, 2.2f, 24);
        }

        private static void Emitter(Transform parent, string name, Vector3 local, Color color, float lifetime, float speed, float size,
            float rate, float radius, float endScale, int max)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = local;
            // 锥形发射默认朝 +Z：转到朝上。
            child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = BossRush.Common.Effects.RingParticleEffect.GetSharedParticleMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = radius;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endScale));
            ps.Play();
        }
    }
}
