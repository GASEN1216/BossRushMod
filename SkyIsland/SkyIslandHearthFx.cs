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
    /// 烟复用全 Mod 共享的程序化粒子材质（`RingParticleEffect.GetSharedParticleMaterial`，2026-09-23 起是中性 1×、普通半透明）；
    /// 火苗与火芯走共享特效工厂的加色软圆（`BossRushFxMaterials`，VB-27）：火要发光、烟要灰，两者不能共用一种混合。
    /// 灶火点光由 <see cref="SkyIslandFireFlicker"/> 在近处跟着火苗闪（`SkyIslandFieldcraft.AddFire` 挂）。
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
            // 分层：暖橙火苗（加色）+ 亮黄火芯（加色）+ 灰烟（半透明）。颜色按共享材质已是中性 1× 来调：
            // 旧烟 (0.76,0.76,0.8,0.28) 在 2× 的 _TintColor 下是发光的白团，现在压成真正的灰烟。
            Material glow = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            Material haze = BossRush.Common.Effects.RingParticleEffect.GetSharedParticleMaterial();
            Emitter(fx.transform, "Flame", new Vector3(0f, 0.1f, 0f), new Color(1f, 0.62f, 0.25f, 0.8f), 0.55f, 0.9f, 0.35f, 22f, 0.18f, 0.3f, 40, glow);
            Emitter(fx.transform, "Core", new Vector3(0f, 0.15f, 0f), new Color(1f, 0.9f, 0.6f, 0.85f), 0.35f, 0.4f, 0.28f, 10f, 0.05f, 0.6f, 12, glow);
            Emitter(fx.transform, "Smoke", new Vector3(0f, 0.6f, 0f), new Color(0.45f, 0.43f, 0.42f, 0.35f), 3.2f, 0.7f, 0.7f, 5f, 0.25f, 2.2f, 24, haze);
        }

        private static void Emitter(Transform parent, string name, Vector3 local, Color color, float lifetime, float speed, float size,
            float rate, float radius, float endScale, int max, Material material)
        {
            if (material == null) return;
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
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            // 寿命、速度与个头都带随机范围：一模一样的粒子排队往上冒，一眼就是程序化的。
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed * 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f, size * 1.25f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
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

        /// <summary>给灶火的点光挂上闪烁（只挂灶火，风晶灯是稳的光）。</summary>
        internal static void Flicker(Light light)
        {
            if (light == null || light.GetComponent<SkyIslandFireFlicker>() != null) return;
            light.gameObject.AddComponent<SkyIslandFireFlicker>().Bind(light);
        }
    }

    /// <summary>
    /// 灶火点光的闪烁（VB-27）：在当前基准强度上 ±1/6（夜里 2.4 ± 0.4），7 Hz 与 3 Hz 两个正弦叠加，和跳动的火苗对得上。
    /// 基准强度由 `SkyIslandFieldcraft.UpdateFires` 在昼夜切换时写：检测到外部改写就改用新的基准，不和它抢。
    /// 只在主角 30 m 内闪（每 0.5 s 查一次距离）；远处写回基准就停，不进每帧热路径。游戏时间驱动，暂停时不动。
    /// </summary>
    internal sealed class SkyIslandFireFlicker : MonoBehaviour
    {
        private const float NearRadius = 30f;
        private const float Amplitude = 1f / 6f;

        private Light target;
        private float baseIntensity, lastWritten, seed, nextNearCheck;
        private bool near;

        internal void Bind(Light light)
        {
            target = light;
            baseIntensity = light.intensity;
            lastWritten = light.intensity;
            seed = Random.Range(0f, 10f);
        }

        private void Update()
        {
            if (target == null) return;
            float now = Time.time;
            if (Mathf.Abs(target.intensity - lastWritten) > 0.0001f) baseIntensity = target.intensity;
            if (now >= nextNearCheck)
            {
                nextNearCheck = now + 0.5f;
                CharacterMainControl main = CharacterMainControl.Main;
                near = main != null && (main.transform.position - transform.position).sqrMagnitude <= NearRadius * NearRadius;
            }
            float value = baseIntensity;
            if (near)
            {
                float t = now + seed;
                float wave = Mathf.Sin(t * 44f) * 0.6f + Mathf.Sin(t * 19.5f) * 0.4f;
                value = baseIntensity * (1f + Amplitude * wave);
            }
            if (Mathf.Abs(value - lastWritten) <= 0.0001f) return;
            target.intensity = value;
            lastWritten = value;
        }
    }
}
