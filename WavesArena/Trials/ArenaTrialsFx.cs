// ============================================================================
// ArenaTrialsFx.cs - 出征令天灾 / 焚城 / 词缀的表现小工具（贴地符环、墨晕填充、余烬、落星、雷光、落点冲击）
// ============================================================================
// 质感口径（owner 2026-11-24 / 11-25：高级、大气、史诗、无塑料感）：
//   - 预警圈一律画在真实判定半径上（鸭科夫固定俯视，地上的圈是唯一能读出「多大」的东西），
//     用带刻度与菱形符文的符环（BossRushParticleShape.RuneOuter），不用平涂色圆片；
//     环内的填充从圆心长到边界，读「还剩多久」（同龙裔火箭落点的口径）；
//   - 粒子少而亮、带辉光，出生有亮芯、消散有渐隐；冲击 = 一圈冲击波 + 一撮火星 + 一缕尘 + 一颗亮芯 + 一盏瞬时灯；
//   - 颜色守在原版暖色调里：余烬金、朱砂、墨、冷银，不用饱和的纯红纯蓝。
// 材质全部取共享工厂（BossRushFxKit / BossRushFxMaterials），本文件不 new 材质、不持有静态缓存。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal static class ArenaTrialsFx
    {
        /// <summary>贴地图形离地高度（压过地表装饰，俯视下读不出抬高）。</summary>
        internal const float GroundLift = 0.09f;
        /// <summary>共享形状精灵是 1 个单位宽；RuneOuter 外细环在 r=0.93 处 → 可见半径 0.465。</summary>
        internal const float RuneRadius = 0.465f;
        /// <summary>RuneInner 的环在 r=0.84 处。</summary>
        internal const float RuneInnerRadius = 0.42f;
        /// <summary>软圆精灵的可见半径。</summary>
        internal const float SoftRadius = 0.5f;

        internal static readonly Color Ember = new Color(1f, 0.62f, 0.28f, 1f);
        internal static readonly Color EmberDeep = new Color(0.86f, 0.30f, 0.12f, 1f);
        internal static readonly Color InkDark = new Color(0.07f, 0.06f, 0.06f, 1f);
        internal static readonly Color Silver = new Color(0.74f, 0.82f, 0.96f, 1f);
        internal static readonly Color Frost = new Color(0.72f, 0.86f, 0.96f, 1f);
        internal static readonly Color Miasma = new Color(0.46f, 0.54f, 0.32f, 1f);
        internal static readonly Color Fog = new Color(0.66f, 0.67f, 0.68f, 1f);
        internal static readonly Color VortexInk = new Color(0.20f, 0.14f, 0.26f, 1f);

        // ------------------------------------------------------------------
        // 贴地精灵
        // ------------------------------------------------------------------

        internal static SpriteRenderer FlatShape(Transform parent, string name, BossRushParticleShape shape, BossRushFxBlend blend, float gain)
        {
            Sprite sprite = BossRushFxKit.GetShapeSprite(shape);
            Material material = BossRushFxKit.GetSpriteMaterial(sprite, blend, gain);
            return CreateFlat(parent, name, sprite, material);
        }

        internal static SpriteRenderer FlatSoft(Transform parent, string name, BossRushFxBlend blend, float gain)
        {
            Sprite sprite = BossRushFxKit.GetSoftCircleSprite();
            Material material = BossRushFxKit.GetSpriteMaterial(sprite, blend, gain);
            return CreateFlat(parent, name, sprite, material);
        }

        private static SpriteRenderer CreateFlat(Transform parent, string name, Sprite sprite, Material material)
        {
            if (parent == null || sprite == null || material == null) return null;
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, GroundLift, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = new Color(1f, 1f, 1f, 0f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>把贴地精灵缩放到真实半径（spriteRadius 是该精灵的可见半径）。</summary>
        internal static void SetRadius(SpriteRenderer renderer, float radius, float spriteRadius)
        {
            if (renderer == null) return;
            float scale = Mathf.Max(0.001f, radius / Mathf.Max(0.01f, spriteRadius));
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// 贴地的线 / 带（熔缝、墨潮浪头、波路金线）：条带贴图沿线恒亮、横向柔边，世界坐标点位。
        /// 法线朝上（TransformZ 对齐 + 物体绕 X 转 −90°），俯视下读成画在地上的一笔，不是朝镜头的飘带。
        /// sortingOrder 决定同一处几条线的叠放（墨底在下、亮芯在上）。
        /// </summary>
        internal static LineRenderer GroundLine(Transform parent, string name, BossRushFxBlend blend, float gain, float width, int sortingOrder)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, blend, gain);
            if (parent == null || material == null) return null;
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.widthMultiplier = Mathf.Max(0.01f, width);
            line.sortingOrder = sortingOrder;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = line.endColor = new Color(1f, 1f, 1f, 0f);
            return line;
        }

        internal static void Tint(LineRenderer line, Color color, float alpha)
        {
            if (line == null) return;
            Color c = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
            line.startColor = c;
            line.endColor = c;
        }

        internal static void Tint(SpriteRenderer renderer, Color color, float alpha)
        {
            if (renderer == null) return;
            renderer.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        internal static void Spin(SpriteRenderer renderer, float degrees)
        {
            if (renderer == null) return;
            renderer.transform.Rotate(0f, 0f, degrees, Space.Self);
        }

        // ------------------------------------------------------------------
        // 常驻发射器（归调用方所有，收尾走 BossRushFxKit.Release）
        // ------------------------------------------------------------------

        /// <summary>贴地缓缓翻涌的烟缕（毒雾、雾泽、墨潮预铺、冰面寒气）。flat=true 平铺在地面，否则立体团雾。</summary>
        internal static ParticleSystem Wisps(Transform parent, string name, Color color, float alpha, float radius,
            float rate, float sizeMin, float sizeMax, float life, bool flat, float rise)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            int max = Mathf.Clamp(Mathf.CeilToInt(rate * life * 1.3f) + 4, 8, 220);
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, parent, new Vector3(0f, flat ? GroundLift + 0.05f : 0.6f, 0f), material, max, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.1f, radius);
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(color.r, color.g, color.b, alpha * 0.6f),
                new Color(color.r, color.g, color.b, alpha), new Color(color.r, color.g, color.b, 0f), 0.3f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 1.25f));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && flat) renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ps.Play();
            return ps;
        }

        /// <summary>少而亮的上窜余烬 / 冰屑 / 气泡（带亮芯、慢慢熄灭）。</summary>
        internal static ParticleSystem Motes(Transform parent, string name, BossRushParticleShape shape, Color color, float radius,
            float rate, float sizeMin, float sizeMax, float life, float rise, BossRushFxBlend blend, float gain)
        {
            Material material = BossRushFxKit.GetShapeMaterial(shape, blend, gain);
            int max = Mathf.Clamp(Mathf.CeilToInt(rate * life * 1.3f) + 4, 6, 160);
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, parent, new Vector3(0f, GroundLift + 0.1f, 0f), material, max, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.ShapeModule shapeModule = ps.shape;
            shapeModule.shapeType = ParticleSystemShapeType.Circle;
            shapeModule.radius = Mathf.Max(0.05f, radius);
            shapeModule.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(1f, 0.96f, 0.88f, 0.9f), new Color(color.r, color.g, color.b, 0.75f),
                new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0f), 0.15f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f));
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ps.Play();
            return ps;
        }

        internal static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        // ------------------------------------------------------------------
        // 一次性演出
        // ------------------------------------------------------------------

        /// <summary>落点冲击：贴地冲击波 + 火星 + 一缕尘 + 亮芯 + 一盏瞬时灯。radius 是真实判定半径。</summary>
        internal static void Impact(Vector3 at, Color color, float radius, int sparks, Color dust)
        {
            try
            {
                Vector3 ground = at + Vector3.up * GroundLift;
                // 细环贴图的环在约 0.45 倍粒子尺寸处：0.85R 起、长到 2.4 倍，正好停在判定边界上
                BossRushFxKit.PlayBurst(ground, BossRushFxKit.Shockwave(new Color(color.r, color.g, color.b, 0.85f),
                    radius * 0.85f, 2.4f, 0.45f, true));
                BossRushFxBurst spark = BossRushFxKit.Sparks(color, sparks);
                spark.Upward = true;
                spark.SpeedMin = 4f;
                spark.SpeedMax = 9f;
                spark.SizeMin = 0.07f;
                spark.SizeMax = 0.12f;
                spark.Gravity = 1.1f;
                spark.LifeMin = 0.35f;
                spark.LifeMax = 0.7f;
                BossRushFxKit.PlayBurst(ground + Vector3.up * 0.2f, spark);
                BossRushFxBurst smoke = BossRushFxKit.Dust(dust, 7);
                smoke.SpeedMin = radius * 0.9f;
                smoke.SpeedMax = radius * 1.4f;
                smoke.SizeMin = radius * 0.35f;
                smoke.SizeMax = radius * 0.6f;
                BossRushFxKit.PlayBurst(ground, smoke);
                BossRushFxKit.PlayBurst(ground + Vector3.up * 0.4f, BossRushFxKit.Glint(color, radius * 1.1f, 0.22f));
                Flash(ground + Vector3.up * 1.2f, color, 3.2f, radius * 2.6f, 0.45f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令天灾冲击表现失败: " + e.Message);
            }
        }

        /// <summary>一盏瞬时点光：当帧亮起、seconds 内淡出后自毁（URP Deferred 下照得到地面）。</summary>
        internal static void Flash(Vector3 at, Color color, float intensity, float range, float seconds)
        {
            GameObject go = new GameObject("BossRush_TrialFlash");
            go.transform.position = at;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = Mathf.Max(1f, range);
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            // 淡入必须是 0：带淡入时 level 从 0 起步，紧接着 FadeOut 会从 0 淡到 0，当帧就自毁、一下都不亮
            BossRushFxLightFade fade = BossRushFxLightFade.Attach(light, intensity, 0f);
            if (fade != null) fade.FadeOut(Mathf.Max(0.1f, seconds), true);
            else UnityEngine.Object.Destroy(go, seconds);
        }

        /// <summary>天上落下的一道火流星：一颗拉长的亮粒带拖尾，flightSeconds 后落在 target。</summary>
        internal static void FallingStar(Vector3 target, Color color, float flightSeconds)
        {
            Vector3 from = target + new Vector3(UnityEngine.Random.Range(-5f, 5f), 15f, UnityEngine.Random.Range(-5f, 5f));
            Vector3 path = target - from;
            float distance = path.magnitude;
            BossRushFxBurst star = new BossRushFxBurst();
            star.Shape = BossRushParticleShape.GlowDot;
            star.Blend = BossRushFxBlend.Additive;
            star.Gain = BossRushFxKit.GainHot;
            star.Count = 1;
            star.SpeedMin = star.SpeedMax = distance / Mathf.Max(0.05f, flightSeconds);
            star.SizeMin = star.SizeMax = 0.55f;
            star.LifeMin = star.LifeMax = flightSeconds;
            star.Stretch = 0.035f;
            star.Trail = 0.45f;
            star.Cone = 1f;
            star.Direction = path / Mathf.Max(0.01f, distance);
            star.ShapeRadius = 0.01f;
            star.GrowTo = 1f;
            star.Core = new Color(1f, 0.97f, 0.88f, 1f);
            star.Main = new Color(color.r, color.g, color.b, 0.95f);
            star.End = new Color(color.r, color.g, color.b, 0.6f);
            BossRushFxKit.PlayBurst(from, star);
        }

        /// <summary>一道落雷：从天上折下来的冷银电光（两条：主干 + 一条分叉），亮两下后熄灭自毁。</summary>
        internal static void LightningBolt(Vector3 target)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            if (material == null) return;
            GameObject go = new GameObject("BossRush_TrialBolt");
            go.transform.position = target;
            go.AddComponent<ArenaTrialsBoltFade>().Build(material, target);
        }

        /// <summary>
        /// 雷暴下落雷劈中敌方身体：身上一圈冷银电弧往外炸开、一颗亮芯、一圈朝镜头的电环、一盏短灯。
        /// 只在雷暴天象里出（平时敌人挨雷不另演），让玩家看得出「把首领引到雷下」劈得更狠。
        /// </summary>
        internal static void Smite(Vector3 at)
        {
            try
            {
                Vector3 body = at + Vector3.up * 0.9f;
                BossRushFxBurst arcs = BossRushFxKit.Sparks(Silver, 14);
                arcs.SpeedMin = 5f;
                arcs.SpeedMax = 10f;
                arcs.SizeMin = 0.05f;
                arcs.SizeMax = 0.09f;
                arcs.LifeMin = 0.12f;
                arcs.LifeMax = 0.26f;
                arcs.Stretch = 0.06f;
                arcs.ShapeRadius = 0.35f;
                BossRushFxKit.PlayBurst(body, arcs);
                BossRushFxKit.PlayBurst(body, BossRushFxKit.Glint(Silver, 1.6f, 0.2f));
                BossRushFxKit.PlayBurst(body, BossRushFxKit.Shockwave(new Color(Silver.r, Silver.g, Silver.b, 0.7f), 0.6f, 3.2f, 0.3f, false));
                Flash(body + Vector3.up * 0.4f, Silver, 4.5f, 6f, 0.3f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令雷击表现失败: " + e.Message);
            }
        }

        /// <summary>血月的暗朱（压低饱和，不是纯红）与余烬色。</summary>
        internal static readonly Color BloodMoon = new Color(0.78f, 0.20f, 0.14f, 1f);
        internal static readonly Color BloodEmber = new Color(0.98f, 0.52f, 0.34f, 1f);

        /// <summary>
        /// 血月下倒下的波次首领：贴地一圈暗朱血环慢慢撑开，一柱余烬从尸身缓缓升起，一盏暗红的灯，一声魂升——
        /// 多掉的那一件就从这里来。慢而沉：环 1.1 秒、余烬 1.6–2.4 秒，不炸不闪。
        /// </summary>
        internal static void BloodOffering(Vector3 at)
        {
            try
            {
                Vector3 ground = at + Vector3.up * GroundLift;
                BossRushFxKit.PlayBurst(ground, BossRushFxKit.Shockwave(new Color(BloodMoon.r, BloodMoon.g, BloodMoon.b, 0.8f),
                    1.4f, 3.6f, 1.1f, true));
                BossRushFxBurst rise = BossRushFxKit.Sparks(BloodEmber, 26);
                rise.Cone = 12f;
                rise.Direction = Vector3.up;
                rise.SpeedMin = 1.6f;
                rise.SpeedMax = 3.4f;
                rise.SizeMin = 0.06f;
                rise.SizeMax = 0.13f;
                rise.LifeMin = 1.6f;
                rise.LifeMax = 2.4f;
                rise.Drag = 0.8f;
                rise.Stretch = 0f;
                rise.GrowTo = 0.3f;
                rise.ShapeRadius = 0.5f;
                rise.FadeIn = 0.12f;
                rise.End = new Color(0.45f, 0.08f, 0.06f, 0f);
                BossRushFxKit.PlayBurst(ground + Vector3.up * 0.2f, rise);
                BossRushFxBurst mist = BossRushFxKit.Dust(new Color(0.36f, 0.06f, 0.05f, 0.5f), 6);
                mist.SpeedMin = 1.2f;
                mist.SpeedMax = 2.2f;
                mist.LifeMin = 1.4f;
                mist.LifeMax = 2f;
                BossRushFxKit.PlayBurst(ground, mist);
                Flash(ground + Vector3.up * 1.6f, BloodMoon, 3.6f, 7f, 1.4f);
                SoundAt(BossRushInkSkin.SfxSoulRise, at);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令血月表现失败: " + e.Message);
            }
        }

        /// <summary>符环与填充的预警淡入 / 收尾包络（两头缓）。</summary>
        internal static float Envelope(float age, float fadeIn, float remaining, float fadeOut)
        {
            float a = fadeIn > 0f ? BossRushUI.SmoothStep(Mathf.Clamp01(age / fadeIn)) : 1f;
            if (fadeOut > 0f && remaining < fadeOut) a *= BossRushUI.SmoothStep(Mathf.Clamp01(remaining / fadeOut));
            return a;
        }

        /// <summary>在世界某处放一声（临时发声体，8 秒后自毁）。</summary>
        internal static void SoundAt(string file, Vector3 at)
        {
            GameObject emitter = new GameObject("BossRush_TrialSfx");
            emitter.transform.position = at;
            BossRushInkSkin.PlaySfxAt(file, emitter);
            UnityEngine.Object.Destroy(emitter, 8f);
        }

        /// <summary>
        /// 让一棵天灾表现自然退场：贴地精灵与线在 seconds 内淡尽、粒子停发射走完寿命、灯淡出，最后销毁根。
        /// 不会「一帧消失」；重复调用无副作用。
        /// </summary>
        internal static void Retire(GameObject root, float seconds)
        {
            if (root == null) return;
            try
            {
                ArenaTrialsRetire retire = root.GetComponent<ArenaTrialsRetire>();
                if (retire == null) retire = root.AddComponent<ArenaTrialsRetire>();
                retire.Begin(seconds);
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(root);
            }
        }
    }

    /// <summary>天灾表现的退场：精灵 / 线按 SmoothStep 淡尽，粒子走完寿命（最多再等 6 秒）后销毁。</summary>
    internal sealed class ArenaTrialsRetire : MonoBehaviour
    {
        private SpriteRenderer[] sprites;
        private Color[] spriteColors;
        private LineRenderer[] lines;
        private Color[] lineStart;
        private Color[] lineEnd;
        private ParticleSystem[] systems;
        private float seconds;
        private float age;
        private bool begun;

        internal void Begin(float fadeSeconds)
        {
            if (begun) return;
            begun = true;
            seconds = Mathf.Max(0.05f, fadeSeconds);
            sprites = GetComponentsInChildren<SpriteRenderer>(false);
            spriteColors = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i] != null ? sprites[i].color : Color.clear;
            lines = GetComponentsInChildren<LineRenderer>(false);
            lineStart = new Color[lines.Length];
            lineEnd = new Color[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                lineStart[i] = lines[i].startColor;
                lineEnd[i] = lines[i].endColor;
            }
            systems = GetComponentsInChildren<ParticleSystem>(false);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null) systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Light[] lights = GetComponentsInChildren<Light>(false);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                BossRushFxLightFade fade = lights[i].GetComponent<BossRushFxLightFade>();
                if (fade == null) fade = BossRushFxLightFade.Attach(lights[i], lights[i].intensity, 0f);
                if (fade != null) fade.FadeOut(seconds, false);
            }
        }

        private void Update()
        {
            if (!begun) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            float k = 1f - BossRushUI.SmoothStep(Mathf.Clamp01(age / seconds));
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color c = spriteColors[i];
                sprites[i].color = new Color(c.r, c.g, c.b, c.a * k);
            }
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                Color a = lineStart[i];
                Color b = lineEnd[i];
                lines[i].startColor = new Color(a.r, a.g, a.b, a.a * k);
                lines[i].endColor = new Color(b.r, b.g, b.b, b.a * k);
            }
            if (age < seconds) return;
            bool alive = false;
            for (int i = 0; i < systems.Length && !alive; i++)
            {
                if (systems[i] != null && systems[i].IsAlive(true)) alive = true;
            }
            if (!alive || age > seconds + 6f) Destroy(gameObject);
        }
    }

    /// <summary>落雷的折线电光：0.06 秒亮、暗一下、再亮、0.3 秒内熄灭后自毁。</summary>
    internal sealed class ArenaTrialsBoltFade : MonoBehaviour
    {
        private LineRenderer trunk;
        private LineRenderer branch;
        private float age;

        internal void Build(Material material, Vector3 target)
        {
            trunk = CreateLine("Trunk", material, 0.22f);
            branch = CreateLine("Branch", material, 0.1f);
            Vector3 top = target + new Vector3(UnityEngine.Random.Range(-2.5f, 2.5f), 16f, UnityEngine.Random.Range(-2.5f, 2.5f));
            Vector3[] points = Zigzag(top, target + Vector3.up * 0.1f, 14, 0.75f);
            trunk.positionCount = points.Length;
            trunk.SetPositions(points);
            int fork = UnityEngine.Random.Range(4, 8);
            Vector3 forkEnd = points[fork] + new Vector3(UnityEngine.Random.Range(-4f, 4f), -UnityEngine.Random.Range(3f, 5f), UnityEngine.Random.Range(-4f, 4f));
            Vector3[] side = Zigzag(points[fork], forkEnd, 6, 0.5f);
            branch.positionCount = side.Length;
            branch.SetPositions(side);
            Apply(1f);
        }

        private LineRenderer CreateLine(string name, Material material, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.6f), new Keyframe(0.8f, width), new Keyframe(1f, width * 1.4f));
            return line;
        }

        private static Vector3[] Zigzag(Vector3 from, Vector3 to, int count, float jitter)
        {
            Vector3[] points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 p = Vector3.Lerp(from, to, t);
                if (i > 0 && i < count - 1)
                {
                    float j = jitter * Mathf.Sin(t * Mathf.PI);
                    p += new Vector3(UnityEngine.Random.Range(-j, j), 0f, UnityEngine.Random.Range(-j, j));
                }
                points[i] = p;
            }
            return points;
        }

        private void Apply(float level)
        {
            Color core = new Color(0.92f, 0.95f, 1f, Mathf.Clamp01(level));
            Color tail = new Color(ArenaTrialsFx.Silver.r, ArenaTrialsFx.Silver.g, ArenaTrialsFx.Silver.b, Mathf.Clamp01(level * 0.8f));
            if (trunk != null) { trunk.startColor = tail; trunk.endColor = core; }
            if (branch != null) { branch.startColor = tail; branch.endColor = new Color(core.r, core.g, core.b, core.a * 0.7f); }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float level;
            if (age < 0.06f) level = 1f;
            else if (age < 0.1f) level = 0.35f;
            else if (age < 0.16f) level = 0.9f;
            else level = Mathf.Clamp01(1f - (age - 0.16f) / 0.3f) * 0.9f;
            Apply(level);
            if (age > 0.5f) Destroy(gameObject);
        }
    }
}
