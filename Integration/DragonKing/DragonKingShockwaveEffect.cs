// ============================================================================
// DragonKingShockwaveEffect.cs - 龙王Boss阶段转换冲击波特效
// ============================================================================
// 模块说明：
//   实现Boss出现时的音浪扩散效果
//   从Boss身体中心向外扩散的圆环
//   圆环碰到玩家时击退玩家
//   三个波间隔0.5秒释放，释放期间Boss不能移动和射击
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
        private bool isActive = false;
        private bool isPooled = false;
        private Vector3 centerPosition;
        private CharacterMainControl playerCharacter;
        private static Material sharedWaveMaterial;
        private static Vector2[] cachedRingUnitPoints;
        private static Transform sharedPoolRoot;
        private static readonly Stack<DragonKingShockwaveEffect> sharedEffectPool =
            new Stack<DragonKingShockwaveEffect>(InitialEffectPoolCapacity);

        // 协程引用（用于生命周期管理）
        private Coroutine releaseWavesCoroutine;
        private Coroutine knockbackClearCoroutine;

        // 回调
        public Action OnAllWavesComplete;

        // ========== 公开方法 ==========

        /// <summary>
        /// 在指定位置播放冲击波效果
        /// </summary>
        public static DragonKingShockwaveEffect PlayAt(Vector3 position)
        {
            DragonKingShockwaveEffect effect = AcquirePooledEffect();
            effect.transform.position = position;
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
            transform.position = center;
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
                    pooledEffect.transform.SetParent(null, false);
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
            ringObj.transform.SetParent(transform, false);
            ringObj.transform.localPosition = Vector3.zero;

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
            wave.ringObject.SetActive(true);

            Color color = waveColor;
            color.a = 0f;
            wave.lineRenderer.startColor = color;
            wave.lineRenderer.endColor = color;
            wave.lineRenderer.startWidth = WaveBaseWidth;
            wave.lineRenderer.endWidth = WaveBaseWidth;
            UpdateRingPositions(wave);
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
                playerPos = playerCharacter.transform.position;
                playerValid = true;
            }

            float maxVisibleRadius = maxRadius + waveSpacing;
            for (int i = 0; i < waveCount && i < waveRings.Count; i++)
            {
                WaveRing wave = waveRings[i];
                if (wave == null || !wave.isActive || wave.lineRenderer == null)
                {
                    continue;
                }

                wave.currentRadius += expansionSpeed * Time.deltaTime;
                UpdateRingPositions(wave);

                if (playerValid && !wave.hasHitPlayer)
                {
                    Vector2 playerOffset = new Vector2(playerPos.x - centerPosition.x, playerPos.z - centerPosition.z);
                    float distanceToPlayerSqr = playerOffset.sqrMagnitude;
                    float waveRadiusSqr = wave.currentRadius * wave.currentRadius;

                    if (waveRadiusSqr >= distanceToPlayerSqr)
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

        private void UpdateRingPositions(WaveRing wave)
        {
            Vector2[] unitPoints = GetCachedRingUnitPoints();
            for (int i = 0; i < unitPoints.Length; i++)
            {
                Vector2 point = unitPoints[i];
                wave.positions[i] = new Vector3(
                    centerPosition.x + point.x * wave.currentRadius,
                    centerPosition.y,
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
            OnAllWavesComplete = null;
            DeactivateAllWaveRings();

            transform.SetParent(GetOrCreatePoolRoot(), false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
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

        private static Material GetSharedWaveMaterial()
        {
            if (sharedWaveMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("UI/Default");
                }
                if (shader != null)
                {
                    sharedWaveMaterial = new Material(shader);
                }
            }

            return sharedWaveMaterial;
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
        }
    }
}
