// ============================================================================
// DragonKingBossGunSpecialAmmo.cs - 焚天龙铳的两种元素弹：大型能量弹「虚空奇点」与水弹「灵潮水漂」
// ============================================================================
// 2026-09-29 新增，接官方弹药：
//   - 大型能量弹 #918（口径 PWL）→ 空间属性。慢速大光球穿过沿途敌人，停在准星处（或撞墙 / 射程尽头）塌成奇点：
//     持续期内把半径内的敌人往中心拽（Boss 不拽）、周期结算，到时坍缩爆开。
//   - 水球 #1630（口径 WaterBall，玩家叫它水弹）→ 灵能属性。三颗低弧水球落地像打水漂一样弹起，
//     每次触地炸一圈水花并挂官方「浸水」（受电伤更高、受火伤更低），最后留一滩灵泉水洼（地面区在 ProjectileZones）。
// 主文件 DragonKingBossGunProjectileAgent.cs 在 LargeFileBudgetGuard 的冻结名单上，只在里面留了五处一行钩子，
// 逻辑与表现都在这里。颜色一律非白（DragonKingBossGunNoWhiteGlowGuard）：虚空是紫 / 靛，水弹是水蓝带一点灵能淡紫。
// 生命周期（§4.12）：奇点、水花发射器都只在真的打出这两种弹时才建；场景切换经 ClearStaticCaches 清掉。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Buffs;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DragonKingBossGunProjectileAgent
    {
        private const int OfficialWaterGunTypeId = 1628;
        private const string WaterSoakBuffKey = "Buff_Water";
        /// <summary>打水漂每次触地至少弹起的竖直速度（m/s）：擦地而过时也要看得出一跳。</summary>
        private const float MinSkipUpSpeed = 1.6f;

        private static readonly Color VoidViolet = new Color(0.56f, 0.3f, 1f);
        private static readonly Color VoidIndigo = new Color(0.22f, 0.14f, 0.82f);
        private static readonly Color VoidCore = new Color(0.1f, 0.05f, 0.24f);
        private static readonly Color WaterAqua = new Color(0.32f, 0.76f, 1f);
        private static readonly Color WaterDeep = new Color(0.2f, 0.48f, 0.95f);
        private static readonly Color SpiritLavender = new Color(0.62f, 0.48f, 1f);

        private static Buff waterSoakBuff;
        private static bool waterSoakBuffLookupDone;
        private static GameObject waterSplashRoot;
        private static ParticleSystem waterDropletEmitter;
        private static ParticleSystem waterRippleEmitter;
        private static ParticleSystem waterSpiritEmitter;

        private static void ClearSpecialAmmoStaticCaches()
        {
            if (waterSplashRoot != null) UnityEngine.Object.Destroy(waterSplashRoot);
            waterSplashRoot = null;
            waterDropletEmitter = null;
            waterRippleEmitter = null;
            waterSpiritEmitter = null;
            waterSoakBuff = null;
            waterSoakBuffLookupDone = false;
            DragonKingBossGunSingularity.ClearStaticCaches();
        }

        /// <summary>
        /// 官方「浸水」：先按显示名 key 在官方 Buff 总表里找，找不到再取官方水球枪自己挂的命中 Buff。
        /// 两处都没有时返回 null，水弹照常造成伤害，只是不挂浸水。
        /// </summary>
        internal static Buff ResolveWaterSoakBuff()
        {
            if (waterSoakBuff != null || waterSoakBuffLookupDone)
            {
                return waterSoakBuff;
            }

            waterSoakBuffLookupDone = true;
            try
            {
                object buffsData = GameplayDataSettings.Buffs;
                FieldInfo allBuffsField = buffsData != null
                    ? buffsData.GetType().GetField("allBuffs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                List<Buff> allBuffs = allBuffsField != null ? allBuffsField.GetValue(buffsData) as List<Buff> : null;
                if (allBuffs != null)
                {
                    for (int i = 0; i < allBuffs.Count; i++)
                    {
                        Buff candidate = allBuffs[i];
                        if (candidate != null && string.Equals(candidate.DisplayNameKey, WaterSoakBuffKey, StringComparison.Ordinal))
                        {
                            waterSoakBuff = candidate;
                            break;
                        }
                    }
                }

                if (waterSoakBuff == null)
                {
                    Item waterGun = ItemAssetsCollection.GetPrefab(OfficialWaterGunTypeId);
                    ItemSetting_Gun waterGunSetting = waterGun != null ? waterGun.GetComponent<ItemSetting_Gun>() : null;
                    if (waterGunSetting != null)
                    {
                        waterSoakBuff = waterGunSetting.buff;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonGun] [WARNING] 查找官方浸水 Buff 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[DragonGun] 水弹浸水 Buff: " + (waterSoakBuff != null ? waterSoakBuff.name + " / " + waterSoakBuff.DisplayNameKey : "未找到，只结算伤害"));
            return waterSoakBuff;
        }

        private GameObject CreateSpecialAmmoTrail()
        {
            if (profile == null)
            {
                return null;
            }

            if (profile.UseSingularity && !secondaryProjectile)
            {
                return CreateVoidOrbTrail();
            }

            if (profile.UseSkipSplash)
            {
                return CreateWaterBallTrail();
            }

            return null;
        }

        /// <summary>打水漂：Bounce 已经按法线反射过；触地时再压竖直速度、留一点水平衰减，然后炸一圈水花。</summary>
        private void OnSpecialAmmoBounce(GameObject hitObject, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (profile == null || !profile.UseSkipSplash || projectile == null)
            {
                return;
            }

            if (DragonKingBossGunRuntime.IsGroundSurface(hitObject, hitNormal))
            {
                Vector3 velocity = velocityRef(projectile);
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z) * Mathf.Clamp01(profile.SkipHorizontalDamping);
                float up = Mathf.Max(MinSkipUpSpeed, Mathf.Abs(velocity.y) * Mathf.Clamp01(profile.SkipVerticalDamping));
                velocity = horizontal + Vector3.up * up;
                float speed = velocity.magnitude;
                if (speed > 0.001f)
                {
                    Vector3 direction = velocity / speed;
                    directionRef(projectile) = direction;
                    velocityRef(projectile) = velocity;
                    transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }

                // Bounce 会把撞到的物体记进 damagedObjects 防止同一面墙连撞；地面是同一个碰撞体，
                // 不拿掉的话下一跳落地会被忽略、水球直接穿地。同时把球抬到球体完全离地，下一帧的扫掠不会从地里起步。
                if (hitObject != null)
                {
                    projectile.damagedObjects.Remove(hitObject);
                }

                transform.position = hitPoint + hitNormal.normalized * (projectile.radius + 0.06f);
            }

            SpawnWaterSplash(hitPoint, hitNormal);
        }

        private void HandleSpecialAmmoDeath(Vector3 deathPosition)
        {
            if (profile == null || projectile == null)
            {
                return;
            }

            if (profile.UseSingularity && !secondaryProjectile)
            {
                DragonKingBossGunSingularity.Spawn(deathPosition, projectile.context, profile, shotId);
            }

            if (profile.UseSkipSplash)
            {
                SpawnWaterSplash(deathPosition, deathNormal);
            }
        }

        private void SpawnWaterSplash(Vector3 point, Vector3 normal)
        {
            float radius = Mathf.Max(0.3f, profile.SplashRadius);
            float marker = DragonKingBossGunRuntime.EncodeShotMarker(
                shotId,
                profile.Id,
                DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary);
            DragonKingBossGunRuntime.ApplyRadiusDamage(
                point,
                radius,
                projectile.context,
                Mathf.Max(0.05f, profile.SplashDamageFactor),
                false,
                true,
                marker);

            if (profile.ApplyWaterSoak)
            {
                ApplyWaterSoakAround(point, radius);
            }

            SpawnWaterSplashFx(point, normal);
        }

        private void ApplyWaterSoakAround(Vector3 center, float radius)
        {
            Buff soak = ResolveWaterSoakBuff();
            if (soak == null)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(center, radius, DragonKingBossGunRuntime.SharedColliderBuffer, GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Ignore);
            DragonKingBossGunRuntime.SharedReceiverIdSet.Clear();
            for (int i = 0; i < count; i++)
            {
                DamageReceiver receiver = DragonKingBossGunRuntime.SharedColliderBuffer[i] != null ? DragonKingBossGunRuntime.SharedColliderBuffer[i].GetComponent<DamageReceiver>() : null;
                if (receiver == null || !DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiver.GetInstanceID()))
                {
                    continue;
                }

                if (!IsTraceReceiverUsable(receiver, receiver.health != null ? receiver.health.TryGetCharacter() : null, false))
                {
                    continue;
                }

                receiver.AddBuff(soak, projectile.context.fromCharacter);
            }
        }

        // ---------------------------------------------------------------- 表现

        private GameObject CreateVoidOrbTrail()
        {
            GameObject trailObject = new GameObject("DragonGun_VoidOrbTrailFx");
            trailObject.transform.SetParent(transform, false);

            Material bandMaterial = GetOrCreateTrailMaterial();
            if (bandMaterial != null)
            {
                TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
                trail.time = 0.32f;
                trail.startWidth = 0.55f;
                trail.endWidth = 0.05f;
                trail.startColor = WithAlpha(VoidViolet, 0.85f);
                trail.endColor = WithAlpha(VoidIndigo, 0f);
                trail.sharedMaterial = bandMaterial;
                trail.numCornerVertices = 4;
                trail.numCapVertices = 4;
                trail.minVertexDistance = 0.04f;
            }

            // 暗核拖烟：半透明靛黑烟缕留在身后，读作「这团光在吞光」。
            ParticleSystem halo = BossRushFxKit.CreateEmitter("VoidOrbHalo", trailObject.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft), 28, true);
            if (halo != null)
            {
                var main = halo.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.48f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(VoidCore, 0.7f), WithAlpha(VoidIndigo, 0.45f));
                var emission = halo.emission;
                emission.rateOverTime = 32f;
                SetFadeAndShrink(halo, 0.1f, 0.3f);
                halo.Play();
            }

            // 绕核旋进的光点：从 0.5 m 的壳上出生、边转边往里掉。
            ParticleSystem motes = BossRushFxKit.CreateEmitter("VoidOrbMotes", trailObject.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright), 36, false);
            if (motes != null)
            {
                var main = motes.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(VoidViolet, 0.9f), WithAlpha(new Color(0.82f, 0.36f, 1f), 0.85f));
                var emission = motes.emission;
                emission.rateOverTime = 44f;
                var shape = motes.shape;
                shape.radius = 0.5f;
                shape.radiusThickness = 0.1f;
                var velocity = motes.velocityOverLifetime;
                velocity.enabled = true;
                velocity.radial = new ParticleSystem.MinMaxCurve(-1.3f);
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(6f);
                SetFadeAndShrink(motes, 0.15f, 0.35f);
                motes.Play();
            }

            return trailObject;
        }

        private GameObject CreateWaterBallTrail()
        {
            GameObject trailObject = new GameObject("DragonGun_WaterBallTrailFx");
            trailObject.transform.SetParent(transform, false);

            Material bandMaterial = GetOrCreateTrailMaterial();
            if (bandMaterial != null)
            {
                TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
                trail.time = 0.22f;
                trail.startWidth = 0.16f;
                trail.endWidth = 0.02f;
                trail.startColor = WithAlpha(WaterAqua, 0.8f);
                trail.endColor = WithAlpha(SpiritLavender, 0f);
                trail.sharedMaterial = bandMaterial;
                trail.numCornerVertices = 3;
                trail.numCapVertices = 3;
                trail.minVertexDistance = 0.03f;
            }

            ParticleSystem bubbles = BossRushFxKit.CreateEmitter("WaterBallBubbles", trailObject.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Bubble, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft), 24, true);
            if (bubbles != null)
            {
                var main = bubbles.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
                main.gravityModifier = 0.25f;
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(WaterAqua, 0.85f), WithAlpha(WaterDeep, 0.7f));
                var emission = bubbles.emission;
                emission.rateOverTime = 18f;
                var shape = bubbles.shape;
                shape.radius = 0.06f;
                SetFadeAndShrink(bubbles, 0f, 0.6f);
                bubbles.Play();
            }

            ParticleSystem spirit = BossRushFxKit.CreateEmitter("WaterBallSpirit", trailObject.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Additive, BossRushFxKit.GainSoft), 12, true);
            if (spirit != null)
            {
                var main = spirit.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(SpiritLavender, 0.32f), WithAlpha(WaterAqua, 0.24f));
                var emission = spirit.emission;
                emission.rateOverTime = 9f;
                SetFadeAndShrink(spirit, 0.2f, 0.5f);
                spirit.Play();
            }

            return trailObject;
        }

        private static void SetFadeAndShrink(ParticleSystem ps, float fadeIn, float endSize)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            // 白色色键 = 不染色，颜色都在 startColor 里，这里只管 alpha。
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                fadeIn > 0f
                    ? new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) }
                    : new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize));
        }

        /// <summary>水花：水珠往上溅、贴地一圈涟漪、两缕淡紫灵气。三个世界空间共享发射器，挪到触地点 Emit，不逐次建物体。</summary>
        private static void SpawnWaterSplashFx(Vector3 point, Vector3 normal)
        {
            if (waterSplashRoot == null)
            {
                BuildWaterSplashEmitters();
            }

            Vector3 up = normal.sqrMagnitude > 0.001f && Vector3.Dot(normal.normalized, Vector3.up) > 0.3f ? normal : Vector3.up;
            EmitAt(waterDropletEmitter, point, up, 14);
            EmitAt(waterRippleEmitter, point + Vector3.up * 0.05f, Vector3.up, 1);
            EmitAt(waterSpiritEmitter, point, up, 3);
        }

        private static void BuildWaterSplashEmitters()
        {
            waterSplashRoot = new GameObject("DragonGun_WaterSplashFx");

            waterDropletEmitter = BossRushFxKit.CreateEmitter("Droplets", waterSplashRoot.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainSoft), 160, true);
            if (waterDropletEmitter != null)
            {
                var main = waterDropletEmitter.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                main.gravityModifier = 1.5f;
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(WaterAqua, 0.95f), WithAlpha(WaterDeep, 0.85f));
                var emission = waterDropletEmitter.emission;
                emission.enabled = false;
                var shape = waterDropletEmitter.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.2f;
                SetFadeAndShrink(waterDropletEmitter, 0f, 0.4f);
                ParticleSystemRenderer renderer = waterDropletEmitter.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.05f;
                    renderer.lengthScale = 1f;
                }
            }

            waterRippleEmitter = BossRushFxKit.CreateEmitter("Ripple", waterSplashRoot.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Ripple, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft), 24, true);
            if (waterRippleEmitter != null)
            {
                var main = waterRippleEmitter.main;
                main.loop = false;
                main.startLifetime = 0.5f;
                main.startSize = 0.4f;
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(WaterAqua, 0.9f), WithAlpha(SpiritLavender, 0.75f));
                var emission = waterRippleEmitter.emission;
                emission.enabled = false;
                var shape = waterRippleEmitter.shape;
                shape.enabled = false;
                // 0.4 m 长到约 2.6 m，正好是水花的伤害直径。
                SetFadeAndShrink(waterRippleEmitter, 0f, 6.5f);
                ParticleSystemRenderer renderer = waterRippleEmitter.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                }
            }

            waterSpiritEmitter = BossRushFxKit.CreateEmitter("Spirit", waterSplashRoot.transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Additive, BossRushFxKit.GainSoft), 40, true);
            if (waterSpiritEmitter != null)
            {
                var main = waterSpiritEmitter.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(SpiritLavender, 0.38f), WithAlpha(WaterAqua, 0.28f));
                var emission = waterSpiritEmitter.emission;
                emission.enabled = false;
                var shape = waterSpiritEmitter.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.25f;
                SetFadeAndShrink(waterSpiritEmitter, 0.2f, 1.4f);
            }
        }
    }

    /// <summary>
    /// 大型能量弹塌出来的奇点：持续期内每帧把半径内的敌人往中心拽（官方单帧强制位移，Boss 不拽），
    /// 按间隔结算空间伤害，到时（或被第 5 个奇点挤掉时）坍缩爆开。只在打出大型能量弹时存在，O(目标数) 每帧。
    /// </summary>
    internal sealed class DragonKingBossGunSingularity : MonoBehaviour
    {
        private const int MaxActive = 4;
        private const int MaxPullTargets = 12;
        private const float TargetRefreshInterval = 0.12f;
        /// <summary>离中心这么近时不再全速拽，只缓缓收拢，免得来回抖。</summary>
        private const float CoreHoldRadius = 0.6f;
        private const int RingSegments = 36;

        private static readonly List<DragonKingBossGunSingularity> activeSingularities = new List<DragonKingBossGunSingularity>();
        private static readonly Vector3[] RingUnitOffsets = BuildRingUnitOffsets();

        private readonly List<CharacterMainControl> pullTargets = new List<CharacterMainControl>(MaxPullTargets);
        private ProjectileContext sourceContext;
        private DragonKingBossGunShotProfile profile;
        private int shotId;
        private float radius;
        private float duration;
        private float elapsed;
        private float tickTimer;
        private float refreshTimer;
        private bool collapsed;
        private Vector3 groundPoint;
        private LineRenderer ring;

        internal static void ClearStaticCaches()
        {
            activeSingularities.Clear();
        }

        internal static void Spawn(Vector3 position, ProjectileContext context, DragonKingBossGunShotProfile shotProfile, int currentShotId)
        {
            if (shotProfile == null)
            {
                return;
            }

            for (int i = activeSingularities.Count - 1; i >= 0; i--)
            {
                if (activeSingularities[i] == null)
                {
                    activeSingularities.RemoveAt(i);
                }
            }

            while (activeSingularities.Count >= MaxActive)
            {
                DragonKingBossGunSingularity oldest = activeSingularities[0];
                activeSingularities.RemoveAt(0);
                if (oldest != null)
                {
                    oldest.Collapse();
                }
            }

            GameObject singularityObject = new GameObject("DragonKingBossGunSingularity");
            singularityObject.transform.position = position;
            DragonKingBossGunSingularity singularity = singularityObject.AddComponent<DragonKingBossGunSingularity>();
            singularity.Initialize(context, shotProfile, currentShotId);
            activeSingularities.Add(singularity);
        }

        private void Initialize(ProjectileContext context, DragonKingBossGunShotProfile shotProfile, int currentShotId)
        {
            sourceContext = context;
            profile = shotProfile;
            shotId = currentShotId;
            radius = Mathf.Max(0.5f, profile.SingularityRadius);
            duration = Mathf.Max(0.2f, profile.SingularityDuration);
            elapsed = 0f;
            tickTimer = 0f;
            refreshTimer = 0f;
            collapsed = false;
            groundPoint = FenHuangHalberdRuntime.SnapToGround(transform.position, transform.position.y);
            try
            {
                CreateVisuals();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonGun] [WARNING] 奇点特效创建失败: " + e.Message);
            }
        }

        private void Update()
        {
            if (collapsed || profile == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            tickTimer += deltaTime;
            refreshTimer -= deltaTime;

            if (refreshTimer <= 0f)
            {
                refreshTimer = TargetRefreshInterval;
                RefreshPullTargets();
            }

            ApplyPull();

            float tickInterval = Mathf.Max(0.1f, profile.SingularityTickInterval);
            if (tickTimer >= tickInterval)
            {
                tickTimer -= tickInterval;
                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    transform.position,
                    radius,
                    sourceContext,
                    Mathf.Max(0.05f, profile.SingularityTickDamageFactor),
                    false,
                    true,
                    SecondaryMarker());
            }

            UpdateRing(Mathf.Clamp01(elapsed / duration));
            if (elapsed >= duration)
            {
                Collapse();
            }
        }

        private float SecondaryMarker()
        {
            return DragonKingBossGunRuntime.EncodeShotMarker(
                shotId,
                profile.Id,
                DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary);
        }

        private void RefreshPullTargets()
        {
            pullTargets.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, DragonKingBossGunRuntime.SharedColliderBuffer, GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count && pullTargets.Count < MaxPullTargets; i++)
            {
                DamageReceiver receiver = DragonKingBossGunRuntime.SharedColliderBuffer[i] != null ? DragonKingBossGunRuntime.SharedColliderBuffer[i].GetComponent<DamageReceiver>() : null;
                if (receiver == null || receiver.health == null || receiver.health.IsDead)
                {
                    continue;
                }

                if (sourceContext.team == receiver.Team && receiver.Team != Teams.all)
                {
                    continue;
                }

                CharacterMainControl character = receiver.health.TryGetCharacter();
                if (character == null || character == sourceContext.realFromCharacter || pullTargets.Contains(character) || IsBoss(character))
                {
                    continue;
                }

                pullTargets.Add(character);
            }
        }

        private void ApplyPull()
        {
            Vector3 center = transform.position;
            float pullSpeed = Mathf.Max(0f, profile.SingularityPullSpeed);
            for (int i = pullTargets.Count - 1; i >= 0; i--)
            {
                CharacterMainControl character = pullTargets[i];
                if (character == null || character.Health == null || character.Health.IsDead)
                {
                    pullTargets.RemoveAt(i);
                    continue;
                }

                if (character.Dashing)
                {
                    continue;
                }

                Vector3 offset = center - character.transform.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > radius + 0.8f)
                {
                    continue;
                }

                // 官方强制位移只管一帧（Movement 消费后即清），所以要逐帧写；奇点消失后敌人立刻恢复自己的移动。
                Vector3 velocity = distance > CoreHoldRadius
                    ? offset / distance * pullSpeed * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(distance / radius))
                    : offset * 3f;
                character.SetForceMoveVelocity(velocity);
            }
        }

        private static bool IsBoss(CharacterMainControl character)
        {
            try
            {
                CharacterRandomPreset preset = character.characterPreset;
                if (preset == null)
                {
                    return false;
                }

                Sprite icon = preset.GetCharacterIcon();
                return icon != null && icon == GameplayDataSettings.UIStyle.BossCharacterIcon;
            }
            catch
            {
                return false;
            }
        }

        private void Collapse()
        {
            if (collapsed)
            {
                return;
            }

            collapsed = true;
            activeSingularities.Remove(this);
            pullTargets.Clear();

            if (profile != null)
            {
                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    transform.position,
                    Mathf.Max(0.5f, profile.SingularityCollapseRange),
                    sourceContext,
                    Mathf.Max(0.1f, profile.SingularityCollapseDamageFactor),
                    false,
                    true,
                    SecondaryMarker());
                PlayCollapseFx(Mathf.Max(0.5f, profile.SingularityCollapseRange));
            }

            if (ring != null)
            {
                ring.enabled = false;
            }

            BossRushFxKit.Release(gameObject, 0.25f, 1.2f, false);
            enabled = false;
        }

        private void OnDestroy()
        {
            activeSingularities.Remove(this);
        }

        // ---------------------------------------------------------------- 表现

        private void CreateVisuals()
        {
            // 外壳往里旋的光点：从 0.85R 的壳上出生，径向往里掉、同时绕竖轴转。
            ParticleSystem swirl = BossRushFxKit.CreateEmitter("SingularitySwirl", transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright), 110, true);
            if (swirl != null)
            {
                float lifetime = 0.65f;
                var main = swirl.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.85f, lifetime * 1.15f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.56f, 0.3f, 1f, 0.9f), new Color(0.82f, 0.36f, 1f, 0.85f));
                var emission = swirl.emission;
                emission.rateOverTime = 80f;
                var shape = swirl.shape;
                shape.radius = radius * 0.85f;
                shape.radiusThickness = 0.15f;
                var velocity = swirl.velocityOverLifetime;
                velocity.enabled = true;
                velocity.radial = new ParticleSystem.MinMaxCurve(-(radius * 0.85f) / lifetime);
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(3f);
                ApplyFade(swirl, 0.2f, 0.3f);
                ParticleSystemRenderer renderer = swirl.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.06f;
                    renderer.lengthScale = 1f;
                }

                swirl.Play();
            }

            // 暗核：半透明靛黑烟团往里缩，读作「中间是个洞」。
            ParticleSystem core = BossRushFxKit.CreateEmitter("SingularityCore", transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft), 24, false);
            if (core != null)
            {
                var main = core.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.05f, 0.24f, 0.82f), new Color(0.2f, 0.08f, 0.4f, 0.7f));
                var emission = core.emission;
                emission.rateOverTime = 26f;
                var shape = core.shape;
                shape.radius = 0.15f;
                ApplyFade(core, 0.2f, 0.4f);
                core.Play();
            }

            // 紫色边光：一层加色软圆罩在暗核外面，一收一收地往里缩。
            ParticleSystem rim = BossRushFxKit.CreateEmitter("SingularityRim", transform, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainSoft), 12, false);
            if (rim != null)
            {
                var main = rim.main;
                main.startLifetime = 0.35f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 1.8f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.3f, 1f, 0.5f), new Color(0.4f, 0.22f, 0.95f, 0.42f));
                var emission = rim.emission;
                emission.rateOverTime = 14f;
                ApplyFade(rim, 0.25f, 0.55f);
                rim.Play();
            }

            Material ringMaterial = DragonKingFxShared.Band(BossRushFxBlend.Additive);
            if (ringMaterial != null)
            {
                GameObject ringObject = new GameObject("SingularityRing");
                ringObject.transform.SetParent(transform, false);
                ringObject.transform.position = groundPoint + Vector3.up * 0.05f;
                ring = ringObject.AddComponent<LineRenderer>();
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = RingSegments;
                ring.numCapVertices = 2;
                ring.numCornerVertices = 2;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                ring.textureMode = LineTextureMode.Stretch;
                ring.sharedMaterial = ringMaterial;
                ring.startColor = new Color(0.6f, 0.36f, 1f, 0.8f);
                ring.endColor = new Color(0.6f, 0.36f, 1f, 0.8f);
                ring.widthMultiplier = 0.12f;
                UpdateRing(0f);
            }

            Light glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.5f, 0.28f, 1f);
            glow.range = radius + 1.5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(glow, 2.2f, 0.15f, 0.2f, 0.5f);
        }

        /// <summary>地面圈从判定半径收到 35%：一眼看出「还有多久塌」。</summary>
        private void UpdateRing(float progress)
        {
            if (ring == null)
            {
                return;
            }

            float ringRadius = radius * Mathf.Lerp(1f, 0.35f, progress * progress);
            for (int i = 0; i < RingUnitOffsets.Length; i++)
            {
                ring.SetPosition(i, RingUnitOffsets[i] * ringRadius);
            }
        }

        private void PlayCollapseFx(float collapseRange)
        {
            Vector3 center = transform.position;

            BossRushFxBurst sparks = new BossRushFxBurst();
            sparks.Shape = BossRushParticleShape.GlowDot;
            sparks.Blend = BossRushFxBlend.Additive;
            sparks.Gain = BossRushFxKit.GainBright;
            sparks.Count = 40;
            sparks.SpeedMin = 8f;
            sparks.SpeedMax = 12f;
            sparks.SizeMin = 0.06f;
            sparks.SizeMax = 0.12f;
            sparks.LifeMin = 0.3f;
            sparks.LifeMax = 0.5f;
            sparks.Drag = 5f;
            sparks.Stretch = 0.05f;
            sparks.GrowTo = 0.4f;
            sparks.ShapeRadius = 0.3f;
            sparks.Core = new Color(0.72f, 0.52f, 1f, 1f);
            sparks.Main = new Color(0.55f, 0.32f, 1f, 0.9f);
            sparks.End = new Color(0.22f, 0.12f, 0.7f, 0f);
            BossRushFxKit.PlayBurst(center, sparks);

            BossRushFxBurst shock = new BossRushFxBurst();
            shock.Shape = BossRushParticleShape.Ring;
            shock.Blend = BossRushFxBlend.Additive;
            shock.Gain = BossRushFxKit.GainBright;
            shock.Count = 1;
            shock.SizeMin = 0.8f;
            shock.SizeMax = 0.8f;
            shock.LifeMin = 0.4f;
            shock.LifeMax = 0.4f;
            shock.GrowTo = collapseRange * 2f / 0.8f;
            shock.ShapeRadius = 0.01f;
            shock.FlatOnGround = true;
            shock.Core = new Color(0.62f, 0.42f, 1f, 0.95f);
            shock.Main = new Color(0.5f, 0.3f, 1f, 0.7f);
            shock.End = new Color(0.3f, 0.2f, 0.9f, 0f);
            BossRushFxKit.PlayBurst(groundPoint + Vector3.up * 0.06f, shock);

            // 坍缩那一下的星芒是往里收的（GrowTo < 1），和普通爆炸往外炸区分开。
            BossRushFxBurst implode = new BossRushFxBurst();
            implode.Shape = BossRushParticleShape.Star;
            implode.Blend = BossRushFxBlend.Additive;
            implode.Gain = BossRushFxKit.GainBright;
            implode.Count = 1;
            implode.SizeMin = 2.4f;
            implode.SizeMax = 2.4f;
            implode.LifeMin = 0.2f;
            implode.LifeMax = 0.2f;
            implode.GrowTo = 0.25f;
            implode.ShapeRadius = 0.01f;
            implode.Core = new Color(0.7f, 0.5f, 1f, 0.9f);
            implode.Main = new Color(0.5f, 0.3f, 1f, 0.6f);
            implode.End = new Color(0.3f, 0.2f, 0.8f, 0f);
            BossRushFxKit.PlayBurst(center, implode);
        }

        private static void ApplyFade(ParticleSystem ps, float fadeIn, float endSize)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize));
        }

        private static Vector3[] BuildRingUnitOffsets()
        {
            Vector3[] offsets = new Vector3[RingSegments];
            for (int i = 0; i < offsets.Length; i++)
            {
                float angle = 360f * i / RingSegments;
                offsets[i] = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            }

            return offsets;
        }
    }
}
