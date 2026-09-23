// ============================================================================
// DragonKingFxShared.cs - 龙王 Boss 与龙王武器共用的程序化特效件（纯表现层）
// ============================================================================
// 2026-09-23 特效审美审查 VB-11 / VB-12 / VB-14 / VB-15 / VB-16 查实：龙王这一族的预警线、预警圈、冲击波、
// 龙皇铳的火花与拖尾全是 `Hidden/Internal-Colored` 或无贴图的 `Sprites/Default`——硬边平色带、白方块，
// 弹体命中时只有一声音效。这里只放它们共用的三样：
//   1. 软边带贴图：跨线宽方向羽化、沿线方向不变。LineRenderer / TrailRenderer 用它（软圆贴图拉在线上会让线头变透明、整条线成一个枣核）。
//   2. 材质：一律走全 Mod 共享工厂 BossRushFxMaterials（URP 粒子 Unlit，Alpha / Additive），本文件不 new Material。
//   3. 命中小爆闪：一个世界空间的共享发射器（火花 + 核心闪），挪到命中点 Emit 一次；只在真的命中时懒建。
// 震屏走官方口径（ExplosionManager.CreateExplosion 的算式：30 m 内、朝向主角指向震源、×0.4）。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class DragonKingFxShared
    {
        private const int HitSparkCap = 128;
        private const int HitCoreCap = 16;

        private static Texture2D bandTexture;
        private static ParticleSystem hitSparks, hitCore;

        /// <summary>软边带材质（线、拖尾、贴地圈）。颜色走顶点色。工厂不可用时返回 null，调用方不画。</summary>
        internal static Material Band(BossRushFxBlend blend)
        {
            return BossRushFxMaterials.Get(blend, BandTexture());
        }

        /// <summary>软圆粒子材质（火花、烟、闪光）。</summary>
        internal static Material Soft(BossRushFxBlend blend)
        {
            return BossRushFxMaterials.Get(blend);
        }

        /// <summary>
        /// 跨带宽羽化的软边带：中间 30% 实心、两侧 SmoothStep 收到 0。2×32，白色。
        /// 进程内只建一份（HideAndDontSave，256 字节）：工厂按贴图实例缓存材质，反复重建会让工厂里堆出一串失效材质。
        /// </summary>
        internal static Texture2D BandTexture()
        {
            if (bandTexture != null) return bandTexture;
            const int width = 2;
            const int height = 32;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.name = "DragonKingFx_SoftBand";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float edge = 1f - Mathf.Abs(v * 2f - 1f);
                float alpha = BossRushUI.SmoothStep(Mathf.Clamp01(edge / 0.7f));
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                for (int x = 0; x < width; x++) pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            bandTexture = texture;
            return bandTexture;
        }

        /// <summary>
        /// 命中小爆闪（VB-15）：16 粒拉伸火花（0.05–0.12 m）+ 2 粒 0.4 m 的核心闪（0.08 s），颜色取弹体色。
        /// 共享的世界空间发射器挪到命中点 Emit 一次；已发出的粒子留在原地，不跟着发射器走。
        /// </summary>
        internal static void HitBurst(Vector3 at, Color color)
        {
            try
            {
                if (!EnsureHitEmitters()) return;
                Color hot = Color.Lerp(color, Color.white, 0.55f);
                ParticleSystem.MainModule sparkMain = hitSparks.main;
                sparkMain.startColor = new ParticleSystem.MinMaxGradient(hot, color);
                hitSparks.transform.position = at;
                if (!hitSparks.isPlaying) hitSparks.Play();
                hitSparks.Emit(16);
                ParticleSystem.MainModule coreMain = hitCore.main;
                coreMain.startColor = new Color(hot.r, hot.g, hot.b, 0.9f);
                hitCore.transform.position = at;
                if (!hitCore.isPlaying) hitCore.Play();
                hitCore.Emit(2);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 命中爆闪失败: " + e.Message);
            }
        }

        /// <summary>
        /// 把粒子的起始尺寸整体乘 <paramref name="factor"/>（VB-03 第 4 点）。
        /// `startSizeMultiplier *= k` 在「两常数随机」模式下只改上限常数：0.1–0.2 m 的火苗会被拉成 0.1–0.36 m 的大小不一，
        /// 这里按模式把两端一起乘；曲线模式乘曲线倍率。
        /// </summary>
        internal static void ScaleStartSize(ParticleSystem.MainModule main, float factor)
        {
            ParticleSystem.MinMaxCurve size = main.startSize;
            switch (size.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    main.startSize = size.constant * factor;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    main.startSize = new ParticleSystem.MinMaxCurve(size.constantMin * factor, size.constantMax * factor);
                    break;
                default:
                    main.startSizeMultiplier = size.curveMultiplier * factor;
                    break;
            }
        }

        /// <summary>官方口径的震屏（30 m 内生效，强度 1 = 官方手雷）。</summary>
        internal static void Shake(Vector3 at, float strength)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || strength <= 0f) return;
            Vector3 offset = at - main.transform.position;
            if (offset.sqrMagnitude > 30f * 30f) return;
            Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.forward;
            CameraShaker.Shake(direction * 0.4f * strength, CameraShaker.CameraShakeTypes.explosion);
        }

        private static bool EnsureHitEmitters()
        {
            if (hitSparks != null && hitCore != null) return true;
            Material glow = Soft(BossRushFxBlend.Additive);
            if (glow == null) return false;
            if (hitSparks == null)
            {
                hitSparks = NewEmitter("DragonKing_HitSparks", glow, HitSparkCap);
                ParticleSystem.MainModule main = hitSparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.gravityModifier = 0.6f;
                ParticleSystem.ShapeModule shape = hitSparks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f;
                ParticleSystemRenderer renderer = hitSparks.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1.4f;
                renderer.velocityScale = 0.05f;
                SetFade(hitSparks, 1f, 0.02f);
                SetGrow(hitSparks, 1f, 0.25f);
            }
            if (hitCore == null)
            {
                hitCore = NewEmitter("DragonKing_HitCore", glow, HitCoreCap);
                ParticleSystem.MainModule main = hitCore.main;
                main.startLifetime = 0.08f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.34f, 0.46f);
                ParticleSystem.ShapeModule shape = hitCore.shape;
                shape.enabled = false;
                SetFade(hitCore, 1f, 0.2f);
                SetGrow(hitCore, 0.7f, 1.3f);
            }
            return true;
        }

        private static ParticleSystem NewEmitter(string name, Material material, int cap)
        {
            GameObject go = new GameObject(name);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            // 循环 + 发射模块关着：保持「在播、但不自己发」，Emit 出来的粒子一定被模拟。
            main.loop = true;
            main.maxParticles = cap;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void SetFade(ParticleSystem ps, float peak, float rampIn)
        {
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, rampIn), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
        }

        private static void SetGrow(ParticleSystem ps, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        /// <summary>
        /// 收掉命中爆闪发射器。模块销毁时经 DragonKingBossGunRuntime.ResetStaticCaches、切场景时经
        /// DragonKingAbilityController.ClearStaticMaterialCache 调用。软边带贴图进程内保留（见 BandTexture）。
        /// </summary>
        internal static void ClearStaticCaches()
        {
            if (hitSparks != null) Object.Destroy(hitSparks.gameObject);
            if (hitCore != null) Object.Destroy(hitCore.gameObject);
            hitSparks = null;
            hitCore = null;
        }
    }
}
