using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 残日盾片的表现：翻滚时在正前方绽开一枚四瓣金色日轮，腰间一道 120 度光弧标出真正能挡的范围；
    /// 接住攻击时整面日轮爆亮，余晖充能期间两颗日珠绕身而行。开合都走缓动，护盾结束后自然收拢淡出，不硬切。
    /// 共享材质，不预热；只在护盾或充能存在时由运行时创建，离场后自己销毁。
    /// </summary>
    internal sealed class ResidualSunShieldFx : MonoBehaviour
    {
        private const int PetalPoints = 11;
        private const int ArcPoints = 25;
        private const float OpenSeconds = 0.09f;
        private const float CloseSeconds = 0.18f;
        private static readonly Color Gold = new Color(1f, 0.72f, 0.26f, 0.9f);
        private static readonly Color PaleGold = new Color(1f, 0.88f, 0.58f, 1f);

        private Transform emblem, petalSpin, arc, orbit;
        private LineRenderer[] petals;
        private LineRenderer rim, arcLine;
        private MeshRenderer lens;
        private Mesh lensMesh;
        private SpriteRenderer[] orbs;
        private ParticleSystem orbTrail;
        private Light glow;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Vector3 anchor;
        private bool guardActive, chargeActive, releasing;
        private float guardLevel, chargeLevel, flash, clock;

        internal static ResidualSunShieldFx Create(Transform parent)
        {
            GameObject go = new GameObject("ResidualSunShield_Fx");
            try
            {
                go.transform.SetParent(parent, false);
                ResidualSunShieldFx view = go.AddComponent<ResidualSunShieldFx>();
                view.Build();
                return view;
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 护盾表现创建失败: " + e.Message);
                return null;
            }
        }

        private void Build()
        {
            Material strip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            Material soft = BossRushFxKit.GetMaterial(BossRushFxBlend.Additive, BossRushFxMaterials.GetSoftCircleTexture(), BossRushFxKit.GainSoft);
            if (strip == null || soft == null) throw new InvalidOperationException("shield_fx_material_missing");

            emblem = Child("SunEmblem", transform);
            lensMesh = BuildLens(0.62f);
            GameObject lensGo = new GameObject("GuardLens");
            lensGo.transform.SetParent(emblem, false);
            lensGo.AddComponent<MeshFilter>().sharedMesh = lensMesh;
            lens = lensGo.AddComponent<MeshRenderer>();
            lens.sharedMaterial = soft;
            Quiet(lens);

            petalSpin = Child("PetalSpin", emblem);
            petals = new LineRenderer[4];
            for (int i = 0; i < petals.Length; i++)
            {
                LineRenderer line = Line("GuardPetal_" + i, petalSpin, strip, 0.05f);
                line.positionCount = PetalPoints;
                float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Vector3 axis = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                Vector3 side = new Vector3(axis.y, -axis.x, 0f);
                // 一片叶形：两侧按正弦鼓起、尖端收拢，首尾在中心汇合成闭合轮廓。
                for (int p = 0; p < PetalPoints; p++)
                {
                    float t = p / (float)(PetalPoints - 1);
                    float along = t <= 0.5f ? t * 2f : (1f - t) * 2f;
                    float bulge = Mathf.Sin(along * Mathf.PI) * 0.17f * (t <= 0.5f ? 1f : -1f);
                    line.SetPosition(p, axis * (0.12f + along * 0.5f) + side * bulge);
                }
                line.loop = true;
                petals[i] = line;
            }
            rim = Line("SunRim", emblem, strip, 0.035f);
            rim.loop = true;
            rim.positionCount = 32;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                rim.SetPosition(i, new Vector3(Mathf.Sin(angle) * 0.66f, Mathf.Cos(angle) * 0.66f, 0f));
            }

            arc = Child("FrontalArc", transform);
            arcLine = Line("CoverageArc", arc, strip, 0.07f);
            arcLine.positionCount = ArcPoints;
            for (int i = 0; i < ArcPoints; i++)
            {
                float angle = Mathf.Lerp(-60f, 60f, i / (float)(ArcPoints - 1)) * Mathf.Deg2Rad;
                arcLine.SetPosition(i, new Vector3(Mathf.Sin(angle) * 0.9f, 0f, Mathf.Cos(angle) * 0.9f));
            }
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            arcLine.colorGradient = fade;

            orbit = Child("AfterglowOrbit", transform);
            Sprite dot = BossRushFxKit.GetShapeSprite(BossRushParticleShape.GlowDot);
            Material hot = BossRushFxKit.GetSpriteMaterial(dot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            orbs = new SpriteRenderer[2];
            for (int i = 0; i < orbs.Length; i++)
            {
                GameObject orb = new GameObject("AfterglowOrb_" + i);
                orb.transform.SetParent(orbit, false);
                SpriteRenderer sprite = orb.AddComponent<SpriteRenderer>();
                sprite.sprite = dot;
                if (hot != null) sprite.sharedMaterial = hot;
                sprite.color = PaleGold;
                Quiet(sprite);
                orbs[i] = sprite;
            }
            orbTrail = BossRushFxKit.CreateEmitter("AfterglowTrail", orbs[0].transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright), 48, true);
            if (orbTrail != null)
            {
                var main = orbTrail.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
                var color = orbTrail.colorOverLifetime;
                color.enabled = true;
                color.color = BossRushFxKit.FadeGradient(PaleGold, Gold, new Color(1f, 0.45f, 0.1f, 0f), 0f);
                var size = orbTrail.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
                var emission = orbTrail.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 14f;
                orbTrail.Play();
            }

            GameObject lightGo = new GameObject("ShieldGlow");
            lightGo.transform.SetParent(emblem, false);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Gold;
            glow.range = 3.2f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            Apply();
        }

        internal void SetState(Vector3 position, Vector3 facing, bool guard, bool charged)
        {
            if (releasing) return;
            if (guard && !guardActive) guardLevel = Mathf.Min(guardLevel, 0.15f);
            guardActive = guard;
            chargeActive = charged;
            anchor = position;
            if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            Place();
        }

        /// <summary>运行时撤掉驱动前调用：脱离父节点，收拢淡出后自毁。</summary>
        internal void Release()
        {
            if (releasing) return;
            releasing = true;
            guardActive = false;
            chargeActive = false;
            transform.SetParent(null, true);
            if (orbTrail != null) orbTrail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>接住攻击时由运行时触发（同帧也会调 PlayAbsorb 放爆发）。</summary>
        internal void Flash()
        {
            flash = 1f;
        }

        private void Place()
        {
            transform.position = anchor;
            if (emblem != null) emblem.localPosition = new Vector3(0f, 0.95f, 0.72f);
            if (arc != null) arc.localPosition = new Vector3(0f, 0.55f, 0f);
            if (orbit != null) orbit.localPosition = new Vector3(0f, 1.05f, 0f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt > 0f && !BossRushUI.IsGamePaused())
            {
                clock += dt;
                guardLevel = Mathf.MoveTowards(guardLevel, guardActive ? 1f : 0f, dt / (guardActive ? OpenSeconds : CloseSeconds));
                chargeLevel = Mathf.MoveTowards(chargeLevel, chargeActive ? 1f : 0f, dt / (chargeActive ? 0.2f : 0.3f));
                flash = Mathf.MoveTowards(flash, 0f, dt * 3.5f);
            }
            Apply();
            if (releasing && guardLevel <= 0f && chargeLevel <= 0f && (orbTrail == null || !orbTrail.IsAlive(false)))
                Destroy(gameObject);
        }

        private void Apply()
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            float open = BossRushUI.EaseOut(guardLevel);
            float g = visible ? open : 0f;
            float shimmer = 0.85f + 0.15f * Mathf.Sin(clock * 9f);
            if (emblem != null)
            {
                emblem.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, open) * (1f + flash * 0.18f);
                petalSpin.localRotation = Quaternion.Euler(0f, 0f, clock * 50f + (1f - open) * 70f);
            }
            float alpha = Mathf.Clamp01(g * (0.75f * shimmer + flash * 0.6f));
            Color petal = Color.Lerp(Gold, PaleGold, flash);
            petal.a = alpha;
            for (int i = 0; petals != null && i < petals.Length; i++)
            {
                petals[i].enabled = g > 0.01f;
                petals[i].startColor = petals[i].endColor = petal;
                petals[i].widthMultiplier = 0.05f * (1f + flash * 0.8f);
            }
            if (rim != null)
            {
                rim.enabled = g > 0.01f;
                Color rimColor = PaleGold;
                rimColor.a = alpha * 0.7f;
                rim.startColor = rim.endColor = rimColor;
            }
            if (lens != null)
            {
                lens.enabled = g > 0.01f;
                block.Clear();
                float lensAlpha = g * (0.16f + flash * 0.45f);
                block.SetVector("_BaseColor", new Vector4(1f, 0.7f, 0.3f, lensAlpha));
                block.SetVector("_TintColor", new Vector4(0.5f, 0.35f, 0.15f, lensAlpha * 0.5f));
                lens.SetPropertyBlock(block);
            }
            if (arcLine != null)
            {
                arcLine.enabled = g > 0.01f;
                Color arcColor = Gold;
                arcColor.a = g * (0.55f + flash * 0.45f);
                arcLine.startColor = arcLine.endColor = arcColor;
                arcLine.widthMultiplier = 0.07f * Mathf.Lerp(0.4f, 1f, open);
            }
            if (glow != null) glow.intensity = g * (0.9f + flash * 2.6f);

            float c = visible ? BossRushUI.EaseOut(chargeLevel) : 0f;
            if (orbit != null)
            {
                orbit.localRotation = Quaternion.Euler(0f, clock * 210f, 0f);
                for (int i = 0; orbs != null && i < orbs.Length; i++)
                {
                    float bob = Mathf.Sin(clock * 4f + i * Mathf.PI) * 0.08f;
                    orbs[i].transform.localPosition = new Vector3(i == 0 ? 0.55f : -0.55f, bob, 0f);
                    orbs[i].transform.localScale = Vector3.one * (0.22f + 0.04f * Mathf.Sin(clock * 7f + i)) * c;
                    orbs[i].enabled = c > 0.01f;
                    Camera view = Camera.main;
                    if (view != null) orbs[i].transform.rotation = view.transform.rotation;
                }
            }
        }

        internal static void PlayAbsorb(Vector3 position)
        {
            try
            {
                BossRushFxKit.PlayBurst(position, BossRushFxKit.Glint(PaleGold, 1.5f, 0.2f));
                BossRushFxKit.PlayBurst(position, BossRushFxKit.Shockwave(new Color(1f, 0.74f, 0.3f, 0.85f), 0.6f, 3.4f, 0.32f, false));
                BossRushFxBurst sparks = BossRushFxKit.Sparks(Gold, 22);
                sparks.SpeedMin = 4f; sparks.SpeedMax = 8.5f; sparks.Gravity = 0.5f;
                sparks.LifeMin = 0.25f; sparks.LifeMax = 0.45f;
                BossRushFxKit.PlayBurst(position, sparks);
                NewWeaponFx.PlaySound(NewWeaponSfx.ShieldAbsorb);
            }
            catch (Exception e) { ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 阻挡反馈失败: " + e.Message); }
        }

        internal static void PlayRiposte(Vector3 position)
        {
            try
            {
                BossRushFxKit.PlayBurst(position, BossRushFxKit.Glint(PaleGold, 1.2f, 0.18f));
                BossRushFxKit.PlayBurst(position, BossRushFxKit.Shockwave(new Color(1f, 0.6f, 0.2f, 0.8f), 0.35f, 4f, 0.28f, false));
                BossRushFxBurst sparks = BossRushFxKit.Sparks(new Color(1f, 0.6f, 0.18f), 18);
                sparks.Upward = true;
                sparks.SpeedMin = 3f; sparks.SpeedMax = 7f; sparks.Gravity = 0.9f;
                sparks.LifeMin = 0.3f; sparks.LifeMax = 0.55f;
                BossRushFxKit.PlayBurst(position, sparks);
                NewWeaponFx.PlaySound("astral_heavy_hit.wav");
            }
            catch (Exception e) { ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 反击反馈失败: " + e.Message); }
        }

        private static Transform Child(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static LineRenderer Line(string name, Transform parent, Material material, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.startColor = line.endColor = Gold;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            Quiet(line);
            return line;
        }

        private static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>竖直圆盘（XY 平面、UV 径向），贴软圆贴图就是中心亮、边缘柔的日轮镜面。</summary>
        private static Mesh BuildLens(float radius)
        {
            const int steps = 40;
            Vector3[] vertices = new Vector3[steps + 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[steps * 3];
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, 0f);
                uv[i + 1] = new Vector2(Mathf.Sin(angle) * 0.5f + 0.5f, Mathf.Cos(angle) * 0.5f + 0.5f);
                if (i == steps) continue;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2;
            }
            Mesh mesh = new Mesh();
            mesh.name = "ResidualSunShield_Lens";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (lensMesh != null) Destroy(lensMesh);
            lensMesh = null;
        }
    }
}
