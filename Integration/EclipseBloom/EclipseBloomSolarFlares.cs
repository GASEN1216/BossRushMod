using System;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 日珥坠落：花冠对场外放风筝的压制。预警圈落在玩家脚下（后续几道按移动方向预判），倒计时里内圈由中心向外烧满，
    /// 最后一束日焰从天而降，圈内玩家受一次伤害。只属于单场战斗 owner，停战或换场景即销毁；不进对象池以外的任何静态缓存。
    /// </summary>
    internal sealed class EclipseBloomSolarFlares : IDisposable
    {
        private const int Capacity = 6;
        private const int RimPoints = 64;
        private const float StreakLead = 0.3f;

        private sealed class Flare
        {
            internal GameObject Root;
            internal MeshRenderer Fill, Core;
            internal LineRenderer Rim;
            internal Vector3 Position;
            internal float Left, Duration, Radius, Damage;
            internal bool Active, Streaked;
        }

        private readonly Flare[] flares = new Flare[Capacity];
        private readonly CharacterMainControl boss;
        private readonly Scene scene;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Mesh disc;
        private float clock, nextHitAt;
        private bool disposed;

        private EclipseBloomSolarFlares(CharacterMainControl owner)
        {
            boss = owner;
            scene = owner.gameObject.scene;
        }

        internal static EclipseBloomSolarFlares Create(CharacterMainControl owner)
        {
            return owner == null ? null : new EclipseBloomSolarFlares(owner);
        }

        internal int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < flares.Length; i++) if (flares[i] != null && flares[i].Active) count++;
                return count;
            }
        }

        /// <summary>在 target 附近的实体地面放一道预警；没有地面或池满时不放。</summary>
        internal bool Launch(Vector3 target, float radius, float warning, float damage)
        {
            if (disposed) return false;
            try
            {
                Vector3 ground;
                if (!TryGround(target, out ground)) return false;
                for (int i = 0; i < flares.Length; i++)
                {
                    // 两道预警重叠时只留一道，避免同一处叠出看不清的双圈与双倍伤害。
                    Flare other = flares[i];
                    if (other != null && other.Active && (other.Position - ground).sqrMagnitude < radius * radius * 0.5f) return false;
                }
                Flare flare = Acquire();
                if (flare == null) return false;
                flare.Position = ground;
                flare.Radius = radius;
                flare.Duration = flare.Left = Mathf.Max(0.6f, warning);
                flare.Damage = damage;
                flare.Active = true;
                flare.Streaked = false;
                flare.Root.transform.position = ground + Vector3.up * 0.05f;
                flare.Fill.transform.localScale = Vector3.one * radius;
                flare.Rim.widthMultiplier = 0.11f;
                for (int i = 0; i < RimPoints; i++)
                {
                    float angle = i * Mathf.PI * 2f / RimPoints;
                    flare.Rim.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, 0.02f, Mathf.Cos(angle) * radius));
                }
                Paint(flare, 0f);
                flare.Root.SetActive(true);
                SkyIslandBossSfx.Play(null, SkyIslandBossCue.Telegraph, ground);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EclipseBloom] 日珥预警创建失败: " + e.Message);
                return false;
            }
        }

        internal void Tick(float deltaTime, CharacterMainControl player)
        {
            if (disposed || deltaTime <= 0f) return;
            clock += deltaTime;
            for (int i = 0; i < flares.Length; i++)
            {
                Flare flare = flares[i];
                if (flare == null || !flare.Active) continue;
                if (flare.Root == null) { flare.Active = false; continue; }
                flare.Left -= deltaTime;
                float progress = Mathf.Clamp01(1f - flare.Left / flare.Duration);
                Paint(flare, progress);
                if (!flare.Streaked && flare.Left <= StreakLead)
                {
                    flare.Streaked = true;
                    PlayStreak(flare.Position);
                }
                if (flare.Left <= 0f) Impact(flare, player);
            }
        }

        internal void Clear()
        {
            for (int i = 0; i < flares.Length; i++)
            {
                Flare flare = flares[i];
                if (flare == null) continue;
                flare.Active = false;
                if (flare.Root != null) flare.Root.SetActive(false);
            }
        }

        private void Paint(Flare flare, float progress)
        {
            // 外圈底色随倒计时加深；内圈按先慢后快的曲线烧满，最后 30% 边线明显加快闪烁，读得出「马上落下」。
            float urgency = Mathf.Clamp01((progress - 0.7f) / 0.3f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(clock * (7f + urgency * 22f));
            Tint(flare.Fill, new Color(0.55f, 0.12f, 0.02f, 0.10f + progress * 0.16f));
            float fill = progress * progress * (3f - 2f * progress);
            flare.Core.transform.localScale = Vector3.one * Mathf.Max(0.001f, flare.Radius * fill);
            Tint(flare.Core, new Color(1f, 0.46f, 0.08f, 0.10f + 0.22f * progress + urgency * 0.12f * pulse));
            Color rim = Color.Lerp(new Color(1f, 0.62f, 0.2f, 0.45f), new Color(1f, 0.9f, 0.62f, 1f), urgency * pulse);
            rim.a = Mathf.Clamp01(0.45f + progress * 0.4f + urgency * 0.15f * pulse);
            flare.Rim.startColor = flare.Rim.endColor = rim;
            flare.Rim.widthMultiplier = 0.09f + urgency * 0.05f * pulse;
        }

        private void PlayStreak(Vector3 ground)
        {
            BossRushFxBurst streak = BossRushFxKit.Sparks(EclipseBloomVisuals.PaleGold, 14);
            streak.Cone = 4f;
            streak.Direction = Vector3.down;
            streak.ShapeRadius = 0.25f;
            streak.SpeedMin = 30f;
            streak.SpeedMax = 36f;
            streak.Drag = 0f;
            streak.LifeMin = 0.26f;
            streak.LifeMax = 0.3f;
            streak.SizeMin = 0.09f;
            streak.SizeMax = 0.16f;
            streak.Stretch = 0.07f;
            streak.GrowTo = 0.6f;
            BossRushFxKit.PlayBurst(ground + Vector3.up * 9.5f, streak);
        }

        private void Impact(Flare flare, CharacterMainControl player)
        {
            flare.Active = false;
            Vector3 at = flare.Position;
            try
            {
                flare.Root.SetActive(false);
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.4f, BossRushFxKit.Glint(EclipseBloomVisuals.Sun, flare.Radius * 1.1f, 0.22f));
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.06f,
                    BossRushFxKit.Shockwave(new Color(1f, 0.55f, 0.12f, 0.8f), flare.Radius * 0.7f, 2.8f, 0.5f, true));
                BossRushFxBurst embers = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 26);
                embers.Upward = true;
                embers.ShapeRadius = flare.Radius * 0.45f;
                embers.SpeedMin = 3f; embers.SpeedMax = 7.5f;
                embers.Gravity = 0.7f; embers.Drag = 1.1f;
                embers.LifeMin = 0.5f; embers.LifeMax = 1.1f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.2f, embers);
                BossRushFxBurst ash = BossRushFxKit.Dust(EclipseBloomVisuals.Ash, 12);
                ash.ShapeRadius = flare.Radius * 0.35f;
                ash.SpeedMin = 2.5f; ash.SpeedMax = 4.5f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.1f, ash);
                FlashLight(at + Vector3.up * 0.8f, flare.Radius);
                SandstormChampionAssetManager.ShakeNear(at, 0.22f, 16f);
                SkyIslandBossSfx.Play(null, SkyIslandBossCue.Impact, at);
            }
            catch (Exception e) { ModBehaviour.DevLog("[EclipseBloom] 日珥落地表现失败: " + e.Message); }
            TryDamage(flare, player);
        }

        private void TryDamage(Flare flare, CharacterMainControl player)
        {
            if (boss == null || player == null || player.Health == null || player.Health.IsDead
                || player.mainDamageReceiver == null || !Team.IsEnemy(boss.Team, player.Team) || clock < nextHitAt) return;
            Vector3 offset = player.transform.position - flare.Position;
            if (Mathf.Abs(offset.y) > 2f || offset.x * offset.x + offset.z * offset.z > flare.Radius * flare.Radius) return;
            // 同帧多道日焰重叠最多结算一次。
            nextHitAt = clock + 0.35f;
            DamageInfo info = new DamageInfo(boss);
            info.damageType = DamageTypes.normal;
            info.damageValue = flare.Damage;
            info.damagePoint = player.transform.position;
            info.damageNormal = Vector3.up;
            info.crit = -1;
            player.mainDamageReceiver.Hurt(info);
        }

        private static void FlashLight(Vector3 at, float radius)
        {
            GameObject go = new GameObject("EclipseBloom_FlareFlash");
            go.transform.position = at;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = EclipseBloomVisuals.Sun;
            light.range = radius * 2.6f;
            light.shadows = LightShadows.None;
            light.intensity = 4.5f;
            BossRushFxLightFade.Attach(light, 4.5f, 0f).FadeOut(0.4f, true);
        }

        private Flare Acquire()
        {
            for (int i = 0; i < flares.Length; i++)
            {
                if (flares[i] == null) return flares[i] = Build(i);
                if (!flares[i].Active && flares[i].Root != null) return flares[i];
            }
            return null;
        }

        private Flare Build(int index)
        {
            if (disc == null) disc = EclipseBloomVisuals.CreateDisc("EclipseBloom_FlareDisc", 1f);
            Material fill = BossRushFxKit.GetMaterial(BossRushFxBlend.Alpha, Texture2D.whiteTexture, BossRushFxKit.GainSoft);
            Material glow = BossRushFxKit.GetMaterial(BossRushFxBlend.Additive, BossRushFxMaterials.GetSoftCircleTexture(), BossRushFxKit.GainBright);
            Material rim = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (fill == null || glow == null || rim == null) throw new InvalidOperationException("eclipse_flare_material_missing");
            GameObject root = new GameObject("EclipseBloom_SolarFlare_" + index);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            Flare flare = new Flare { Root = root };
            flare.Fill = Disc(root.transform, "ScorchedWarning", fill);
            flare.Core = Disc(root.transform, "IgnitingCore", glow);
            flare.Core.transform.localPosition = Vector3.up * 0.01f;
            GameObject rimGo = new GameObject("FlareRim");
            rimGo.transform.SetParent(root.transform, false);
            LineRenderer line = rimGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RimPoints;
            line.sharedMaterial = rim;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            flare.Rim = line;
            return flare;
        }

        private MeshRenderer Disc(Transform parent, string name, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = disc;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        private void Tint(Renderer renderer, Color color)
        {
            EclipseBloomVisuals.Tint(renderer, block, color);
        }

        private static bool TryGround(Vector3 target, out Vector3 ground)
        {
            ground = target;
            RaycastHit hit;
            if (!Physics.Raycast(target + Vector3.up * 3f, Vector3.down, out hit, 7f,
                    GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.6f) return false;
            ground = hit.point;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < flares.Length; i++)
            {
                Flare flare = flares[i];
                flares[i] = null;
                if (flare != null && flare.Root != null) { flare.Root.SetActive(false); UnityEngine.Object.Destroy(flare.Root); }
            }
            if (disc != null) UnityEngine.Object.Destroy(disc);
            disc = null;
        }
    }
}
