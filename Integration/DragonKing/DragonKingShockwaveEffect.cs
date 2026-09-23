// ============================================================================
// DragonKingShockwaveEffect.cs - 龙王Boss阶段转换冲击波特效
// ============================================================================
// 模块说明：
//   实现Boss出现时的音浪扩散效果
//   从Boss身体中心向外扩散的圆环
//   圆环碰到玩家时击退玩家
//   三个波间隔0.5秒释放，释放期间Boss不能移动和射击
//
// 2026-09-23 特效审美审查 VB-14（只改表现，扩散速度、击退时机与力度一字不动）：
//   - 环抬离地面 0.12 m、摊平（TransformZ，环物体绕 X 转 90°），不再是面向镜头、一半插进地里的锯齿带；
//   - 软边带材质（共享工厂），不再是无贴图的 Sprites/Default 塑料色带；
//   - 每一波起点官方口径震屏 0.6；
//   - 环前沿在 1.5 / 4 / 8 / 13 m 处各扬一圈尘（放平的 Circle、世界空间、粒子数封顶），读得出「一道冲击扫过地面」。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 龙王阶段转换冲击波特效
    /// </summary>
    public class DragonKingShockwaveEffect : MonoBehaviour
    {
        private const int RingSegmentCount = 60;
        private const float WaveBaseWidth = 0.8f;
        private const float WaveMinWidth = 0.2f;
        private const int InitialEffectPoolCapacity = 4;
        /// <summary>环离地高度（米）：圆心就是地面，旧环与地面同高、一半插进地里。</summary>
        private const float RingLift = 0.12f;
        /// <summary>每一波起点的震屏强度（官方手雷是 1）。</summary>
        private const float WaveShake = 0.6f;
        /// <summary>环前沿扬尘的半径档位与每档颗数：按事件发，不按帧发（与帧率无关）。</summary>
        private static readonly float[] DustRadii = { 1.5f, 4f, 8f, 13f };
        private static readonly int[] DustCounts = { 6, 8, 10, 12 };
        private static readonly Color DustColor = new Color(0.75f, 0.68f, 0.58f, 1f);

        // ========== 配置参数 ==========

        /// <summary>
        /// 冲击波扩散速度（单位/秒）
        /// </summary>
        public float expansionSpeed = 17f;

        // ========== 性能优化：WaitForSeconds缓存 ==========

        private static readonly WaitForSeconds waveIntervalCached = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds waveExpandTime = new WaitForSeconds(3f);
        private static readonly WaitForSeconds knockbackClearTime = new WaitForSeconds(0.3f);

        /// <summary>
        /// 冲击波最大半径
        /// </summary>
        public float maxRadius = 30f;

        /// <summary>
        /// 波纹环数量
        /// </summary>
        public int waveCount = 3;

        /// <summary>
        /// 波纹释放间隔（秒）
        /// </summary>
        public float waveInterval = 0.5f;

        /// <summary>
        /// 波纹间距（初始扩散时的间距）
        /// </summary>
        public float waveSpacing = 5f;

        /// <summary>
        /// 波纹颜色（带透明度）
        /// </summary>
        public Color waveColor = new Color(1f, 0.85f, 0.5f, 0.8f); // 金黄色

        /// <summary>
        /// 击退力度
        /// </summary>
        public float knockbackForce = 15f;

        /// <summary>
        /// 击退垂直力度（向上）
        /// </summary>
        public float knockbackUpwardForce = 8f;

        // ========== 私有变量 ==========

        private readonly List<WaveRing> waveRings = new List<WaveRing>(3);
        private ParticleSystem dustEmitter;
        private bool isActive = false;
        private bool isPooled = false;
        private Vector3 centerPosition;
        private CharacterMainControl playerCharacter;
        private Transform cachedPlayerTransform;
        private Transform cachedTransform;
        private static Vector2[] cachedRingUnitPoints;
        private static Transform sharedPoolRoot;
        private static readonly Stack<DragonKingShockwaveEffect> sharedEffectPool =
            new Stack<DragonKingShockwaveEffect>(InitialEffectPoolCapacity);

        // 协程引用（用于生命周期管理）
        private Coroutine releaseWavesCoroutine;
        private Coroutine knockbackClearCoroutine;

        // 回调
        public Action OnAllWavesComplete;

        private Transform CachedTransform
        {
            get
            {
                if (cachedTransform == null)
                {
                    cachedTransform = transform;
                }

                return cachedTransform;
            }
        }

        // ========== 公开方法 ==========

        /// <summary>
        /// 在指定位置播放冲击波效果
        /// </summary>
        public static DragonKingShockwaveEffect PlayAt(Vector3 position)
        {
            DragonKingShockwaveEffect effect = AcquirePooledEffect();
            effect.CachedTransform.position = position;
            effect.gameObject.SetActive(true);
            effect.StartShockwave(position);
            return effect;
        }

        public static void WarmSharedPool(int desiredCount)
        {
            if (desiredCount <= 0)
            {
                return;
            }

            while (sharedEffectPool.Count < desiredCount)
            {
                GameObject obj = new GameObject("DragonKing_Shockwave");
                DragonKingShockwaveEffect effect = obj.AddComponent<DragonKingShockwaveEffect>();
                effect.isPooled = false;
                effect.EnsureWaveRingPool(effect.waveCount);
                effect.ReturnToPool();
            }
        }

        /// <summary>
        /// 开始冲击波效果
        /// </summary>
        public void StartShockwave(Vector3 center)
        {
            EnsureWaveRingPool(waveCount);
            StopActiveCoroutines();
            DeactivateAllWaveRings();

            centerPosition = center;
            CachedTransform.position = center;
            isActive = true;
            isPooled = false;
            OnAllWavesComplete = null;

            // 获取玩家引用
            FindPlayer();

            // 开始释放波纹（保存协程引用）
            releaseWavesCoroutine = StartCoroutine(ReleaseWaves());

            ModBehaviour.DevLog($"[DragonKing] 冲击波特效开始播放，将释放{waveCount}个波");
        }

        private void Awake()
        {
            EnsureWaveRingPool(waveCount);
            EnsureDustEmitter();
            DeactivateAllWaveRings();
        }

        // ========== 私有方法 ==========

        private static DragonKingShockwaveEffect AcquirePooledEffect()
        {
            while (sharedEffectPool.Count > 0)
            {
                DragonKingShockwaveEffect pooledEffect = sharedEffectPool.Pop();
                if (pooledEffect != null)
                {
                    pooledEffect.CachedTransform.SetParent(null, false);
                    pooledEffect.isPooled = false;
                    return pooledEffect;
                }
            }

            GameObject obj = new GameObject("DragonKing_Shockwave");
            DragonKingShockwaveEffect effect = obj.AddComponent<DragonKingShockwaveEffect>();
            effect.isPooled = false;
            return effect;
        }

        private static Transform GetOrCreatePoolRoot()
        {
            if (sharedPoolRoot == null)
            {
                GameObject poolRootObject = new GameObject("DragonKing_ShockwavePool");
                poolRootObject.hideFlags = HideFlags.HideInHierarchy;
                sharedPoolRoot = poolRootObject.transform;
            }

            return sharedPoolRoot;
        }

        private void FindPlayer()
        {
            playerCharacter = CharacterMainControl.Main;
            cachedPlayerTransform = playerCharacter != null ? playerCharacter.transform : null;

            if (playerCharacter == null)
            {
                ModBehaviour.DevLog("[DragonKing] 无法找到玩家引用");
            }
        }

        private IEnumerator ReleaseWaves()
        {
            for (int i = 0; i < waveCount; i++)
            {
                ActivateWaveRing(i);

                if (i < waveCount - 1)
                {
                    yield return waveIntervalCached;
                }
            }

            yield return waveExpandTime;

            Action onAllWavesComplete = OnAllWavesComplete;
            OnAllWavesComplete = null;
            onAllWavesComplete?.Invoke();
            ModBehaviour.DevLog("[DragonKing] 冲击波特效完成");

            ReturnToPool();
        }

        private void EnsureWaveRingPool(int requiredCount)
        {
            if (requiredCount <= 0)
            {
                requiredCount = 1;
            }

            while (waveRings.Count < requiredCount)
            {
                waveRings.Add(CreateWaveRing(waveRings.Count));
            }
        }

        private WaveRing CreateWaveRing(int index)
        {
            GameObject ringObj = new GameObject($"WaveRing_{index}");
            ringObj.transform.SetParent(CachedTransform, false);
            ringObj.transform.localPosition = Vector3.zero;
            // 世界坐标的环点 + TransformZ：环物体绕 X 转 90° 后 Z 轴朝下，带子摊平在地面上（VB-14）。
            ringObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            LineRenderer lineRenderer = ringObj.AddComponent<LineRenderer>();
            Material material = GetSharedWaveMaterial();
            if (material != null)
            {
                lineRenderer.sharedMaterial = material;
            }
            lineRenderer.startWidth = WaveBaseWidth;
            lineRenderer.endWidth = WaveBaseWidth;
            lineRenderer.positionCount = RingSegmentCount + 1;
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.alignment = LineAlignment.TransformZ;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            WaveRing waveRing = new WaveRing
            {
                index = index,
                ringObject = ringObj,
                lineRenderer = lineRenderer,
                positions = new Vector3[RingSegmentCount + 1],
                currentRadius = 0f,
                hasHitPlayer = false,
                isActive = false
            };

            ringObj.SetActive(false);
            return waveRing;
        }

        private void ActivateWaveRing(int index)
        {
            if (index < 0 || index >= waveRings.Count)
            {
                return;
            }

            WaveRing wave = waveRings[index];
            if (wave == null || wave.ringObject == null || wave.lineRenderer == null)
            {
                return;
            }

            wave.index = index;
            wave.currentRadius = 0f;
            wave.hasHitPlayer = false;
            wave.isActive = true;
            wave.dustStage = 0;
            wave.ringObject.SetActive(true);
            // 每一波起点震一下（官方口径：30 m 内、朝向主角指向震源）；击退仍按原时机发生。
            DragonKingFxShared.Shake(centerPosition, WaveShake);

            Color color = waveColor;
            color.a = 0f;
            wave.lineRenderer.startColor = color;
            wave.lineRenderer.endColor = color;
            wave.lineRenderer.startWidth = WaveBaseWidth;
            wave.lineRenderer.endWidth = WaveBaseWidth;
            UpdateRingPositions(wave, GetCachedRingUnitPoints());
        }

        private void DeactivateWaveRing(WaveRing wave)
        {
            if (wave == null)
            {
                return;
            }

            wave.currentRadius = 0f;
            wave.hasHitPlayer = false;
            wave.isActive = false;

            if (wave.ringObject != null)
            {
                wave.ringObject.SetActive(false);
            }
        }

        private void DeactivateAllWaveRings()
        {
            for (int i = 0; i < waveRings.Count; i++)
            {
                DeactivateWaveRing(waveRings[i]);
            }
        }

        private void Update()
        {
            if (!isActive)
            {
                return;
            }

            if (playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.IsDead)
            {
                FindPlayer();
            }

            Vector3 playerPos = Vector3.zero;
            bool playerValid = false;
            if (playerCharacter != null && playerCharacter.Health != null && !playerCharacter.Health.IsDead)
            {
                playerPos = cachedPlayerTransform.position;
                playerValid = true;
            }

            float maxVisibleRadius = maxRadius + waveSpacing;
            Vector2[] ringUnitPoints = GetCachedRingUnitPoints();
            float playerDistanceToCenterSqr = 0f;
            if (playerValid)
            {
                Vector2 playerOffset = new Vector2(playerPos.x - centerPosition.x, playerPos.z - centerPosition.z);
                playerDistanceToCenterSqr = playerOffset.sqrMagnitude;
            }

            for (int i = 0; i < waveCount && i < waveRings.Count; i++)
            {
                WaveRing wave = waveRings[i];
                if (wave == null || !wave.isActive || wave.lineRenderer == null)
                {
                    continue;
                }

                wave.currentRadius += expansionSpeed * Time.deltaTime;
                UpdateRingPositions(wave, ringUnitPoints);
                EmitFrontDust(wave);

                if (playerValid && !wave.hasHitPlayer)
                {
                    float waveRadiusSqr = wave.currentRadius * wave.currentRadius;

                    if (waveRadiusSqr >= playerDistanceToCenterSqr)
                    {
                        KnockbackPlayer(playerPos, centerPosition);
                        wave.hasHitPlayer = true;
                        ModBehaviour.DevLog($"[DragonKing] 波{wave.index}击退玩家");
                    }
                }

                float alpha = CalculateAlpha(wave.currentRadius);
                Color color = waveColor;
                color.a = alpha;
                wave.lineRenderer.startColor = color;
                wave.lineRenderer.endColor = color;

                float width = WaveBaseWidth * (1f - wave.currentRadius / maxVisibleRadius * 0.6f);
                width = Mathf.Max(WaveMinWidth, width);
                wave.lineRenderer.startWidth = width;
                wave.lineRenderer.endWidth = width;

                if (wave.currentRadius > maxVisibleRadius)
                {
                    DeactivateWaveRing(wave);
                }
            }
        }

        private void UpdateRingPositions(WaveRing wave, Vector2[] unitPoints)
        {
            if (unitPoints == null)
            {
                return;
            }

            for (int i = 0; i < unitPoints.Length; i++)
            {
                Vector2 point = unitPoints[i];
                wave.positions[i] = new Vector3(
                    centerPosition.x + point.x * wave.currentRadius,
                    centerPosition.y + RingLift,
                    centerPosition.z + point.y * wave.currentRadius);
            }

            wave.lineRenderer.SetPositions(wave.positions);
        }

        private float CalculateAlpha(float radius)
        {
            float fadeIn = Mathf.Min(1f, radius / waveSpacing);
            float fadeOut = 1f - (radius - maxRadius) / waveSpacing;
            return Mathf.Clamp01(fadeIn * fadeOut) * waveColor.a;
        }

        private void KnockbackPlayer(Vector3 playerPos, Vector3 waveCenter)
        {
            if (playerCharacter == null)
            {
                return;
            }

            Vector3 knockbackDir = (playerPos - waveCenter).normalized;
            knockbackDir.y = 0;

            Vector3 knockbackVelocity = knockbackDir * knockbackForce + Vector3.up * knockbackUpwardForce;
            playerCharacter.SetForceMoveVelocity(knockbackVelocity);

            if (knockbackClearCoroutine != null)
            {
                StopCoroutine(knockbackClearCoroutine);
            }
            knockbackClearCoroutine = StartCoroutine(ClearKnockbackAfterDelay());

            ModBehaviour.DevLog($"[DragonKing] 玩家被击退，速度: {knockbackVelocity}");
        }

        /// <summary>
        /// 延迟清除击退效果
        /// </summary>
        private IEnumerator ClearKnockbackAfterDelay()
        {
            yield return knockbackClearTime;

            if (playerCharacter != null)
            {
                playerCharacter.SetForceMoveVelocity(Vector3.zero);
            }
        }

        private void ReturnToPool()
        {
            if (isPooled)
            {
                return;
            }

            StopActiveCoroutines();
            isActive = false;
            isPooled = true;
            playerCharacter = null;
            cachedPlayerTransform = null;
            OnAllWavesComplete = null;
            DeactivateAllWaveRings();

            Transform selfTransform = CachedTransform;
            selfTransform.SetParent(GetOrCreatePoolRoot(), false);
            selfTransform.localPosition = Vector3.zero;
            selfTransform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);

            sharedEffectPool.Push(this);
        }

        private void StopActiveCoroutines()
        {
            if (releaseWavesCoroutine != null)
            {
                StopCoroutine(releaseWavesCoroutine);
                releaseWavesCoroutine = null;
            }
            if (knockbackClearCoroutine != null)
            {
                StopCoroutine(knockbackClearCoroutine);
                knockbackClearCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            StopActiveCoroutines();
            OnAllWavesComplete = null;
            waveRings.Clear();
        }

        /// <summary>冲击波环的材质：软边带 + 普通半透明（共享工厂持有，这里不建、不销毁）。</summary>
        private static Material GetSharedWaveMaterial()
        {
            return DragonKingFxShared.Band(BossRushFxBlend.Alpha);
        }

        /// <summary>
        /// 环前沿扬尘：环越过 <see cref="DustRadii"/> 的某一档时，在那一圈上发一次。
        /// 发射器挂在本特效上（随对象池进出），世界空间：已扬起的尘不跟着下一波移动。
        /// </summary>
        private void EmitFrontDust(WaveRing wave)
        {
            if (dustEmitter == null || wave.dustStage >= DustRadii.Length) return;
            if (wave.currentRadius < DustRadii[wave.dustStage]) return;
            ParticleSystem.ShapeModule shape = dustEmitter.shape;
            shape.radius = DustRadii[wave.dustStage];
            dustEmitter.transform.position = centerPosition + Vector3.up * RingLift;
            // 特效从对象池取出后发射器停在非播放态：补一次 Play（发射模块关着，只是让 Emit 的粒子被模拟）。
            if (!dustEmitter.isPlaying) dustEmitter.Play();
            dustEmitter.Emit(DustCounts[wave.dustStage]);
            wave.dustStage++;
        }

        private void EnsureDustEmitter()
        {
            if (dustEmitter != null) return;
            Material material = DragonKingFxShared.Soft(BossRushFxBlend.Alpha);
            if (material == null) return;
            GameObject go = new GameObject("ShockwaveDust");
            go.transform.SetParent(CachedTransform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 128;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = DustColor;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            // Circle 默认竖在局部 XY 平面：转 90° 放平到地面，出生方向随之水平向外。
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radiusThickness = 0f;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.35f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            dustEmitter = ps;
        }

        private static Vector2[] GetCachedRingUnitPoints()
        {
            if (cachedRingUnitPoints == null || cachedRingUnitPoints.Length != RingSegmentCount + 1)
            {
                cachedRingUnitPoints = new Vector2[RingSegmentCount + 1];
                float angleStep = 360f / RingSegmentCount;
                for (int i = 0; i <= RingSegmentCount; i++)
                {
                    float angle = i * angleStep * Mathf.Deg2Rad;
                    cachedRingUnitPoints[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }

            return cachedRingUnitPoints;
        }

        // ========== 内部类 ==========

        private sealed class WaveRing
        {
            public int index;
            public GameObject ringObject;
            public LineRenderer lineRenderer;
            public Vector3[] positions;
            public float currentRadius;
            public bool hasHitPlayer;
            public bool isActive;
            /// <summary>已经扬过尘的半径档位数（见 DustRadii）。</summary>
            public int dustStage;
        }
    }
}
