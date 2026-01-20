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

        private List<WaveRing> waveRings = new List<WaveRing>();
        private bool isActive = false;
        private Vector3 centerPosition;
        private CharacterMainControl playerCharacter;
        private HashSet<int> hitPlayerWaves = new HashSet<int>(); // 记录已击退玩家的波

        // 协程引用（用于生命周期管理）
        private Coroutine releaseWavesCoroutine;
        private Coroutine knockbackClearCoroutine;

        // 回调
        public System.Action OnAllWavesComplete;

        // ========== 公开方法 ==========

        /// <summary>
        /// 在指定位置播放冲击波效果
        /// </summary>
        public static DragonKingShockwaveEffect PlayAt(Vector3 position)
        {
            GameObject obj = new GameObject("DragonKing_Shockwave");
            obj.transform.position = position;

            var effect = obj.AddComponent<DragonKingShockwaveEffect>();
            effect.StartShockwave(position);

            return effect;
        }

        /// <summary>
        /// 开始冲击波效果
        /// </summary>
        public void StartShockwave(Vector3 center)
        {
            centerPosition = center;
            isActive = true;
            hitPlayerWaves.Clear();

            // 获取玩家引用
            FindPlayer();

            // 开始释放波纹（保存协程引用）
            releaseWavesCoroutine = StartCoroutine(ReleaseWaves());

            ModBehaviour.DevLog($"[DragonKing] 冲击波特效开始播放，将释放{waveCount}个波");
        }

        // ========== 私有方法 ==========

        private void FindPlayer()
        {
            // 使用CharacterMainControl.Main获取玩家
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
                // 创建一个波纹环
                CreateWaveRing(i);

                // 等待间隔（最后一个波不需要等待）
                if (i < waveCount - 1)
                {
                    yield return waveIntervalCached; // waveInterval = 0.5f
                }
            }

            // 等待所有波纹扩散完成
            yield return waveExpandTime; // 预留足够时间让波纹扩散(3f)

            // 所有波纹完成
            OnAllWavesComplete?.Invoke();
            ModBehaviour.DevLog("[DragonKing] 冲击波特效完成");

            isActive = false;
            Destroy(gameObject, 1f);
        }

        private void CreateWaveRing(int index)
        {
            GameObject ringObj = new GameObject($"WaveRing_{index}");
            ringObj.transform.SetParent(transform);
            ringObj.transform.localPosition = Vector3.zero;

            // 添加 LineRenderer
            LineRenderer lineRenderer = ringObj.AddComponent<LineRenderer>();

            // 创建材质
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");

            Material material = new Material(shader);
            material.color = waveColor;

            // 配置 LineRenderer
            lineRenderer.material = material;
            lineRenderer.startWidth = 0.8f;
            lineRenderer.endWidth = 0.8f;
            lineRenderer.positionCount = 61;
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;

            // 创建波纹环数据
            WaveRing wave = new WaveRing
            {
                index = index,
                lineRenderer = lineRenderer,
                currentRadius = 0f,
                hasHitPlayer = false
            };

            waveRings.Add(wave);
        }

        private void Update()
        {
            if (!isActive) return;

            // 检查玩家位置
            Vector3 playerPos = Vector3.zero;
            bool playerValid = false;
            if (playerCharacter != null)
            {
                playerPos = playerCharacter.transform.position;
                playerValid = true;
            }

            // 更新所有波纹环
            for (int i = waveRings.Count - 1; i >= 0; i--)
            {
                WaveRing wave = waveRings[i];
                if (wave == null || wave.lineRenderer == null) continue;

                // 扩散波纹
                wave.currentRadius += expansionSpeed * Time.deltaTime;

                // 更新圆环点位
                UpdateRingPositions(wave);

                // 检查是否击中玩家
                if (playerValid && !wave.hasHitPlayer)
                {
                    float distanceToPlayer = Vector2.Distance(
                        new Vector2(centerPosition.x, centerPosition.z),
                        new Vector2(playerPos.x, playerPos.z)
                    );

                    // 当波纹半径接近玩家距离时，击退玩家
                    if (wave.currentRadius >= distanceToPlayer)
                    {
                        KnockbackPlayer(playerPos, centerPosition);
                        wave.hasHitPlayer = true;
                        ModBehaviour.DevLog($"[DragonKing] 波{wave.index}击退玩家");
                    }
                }

                // 更新透明度（边缘淡出）
                float alpha = CalculateAlpha(wave.currentRadius);
                Color color = waveColor;
                color.a = alpha;
                wave.lineRenderer.startColor = color;
                wave.lineRenderer.endColor = color;

                // 更新线条宽度（随着扩散逐渐变细）
                float width = 0.8f * (1f - wave.currentRadius / (maxRadius + waveSpacing) * 0.6f);
                width = Mathf.Max(0.2f, width);
                wave.lineRenderer.startWidth = width;
                wave.lineRenderer.endWidth = width;

                // 超出最大半径后移除
                if (wave.currentRadius > maxRadius + waveSpacing)
                {
                    if (wave.lineRenderer != null)
                    {
                        Destroy(wave.lineRenderer.gameObject);
                    }
                    waveRings.RemoveAt(i);
                }
            }
        }

        private void UpdateRingPositions(WaveRing wave)
        {
            int segments = 60;
            float angleStep = 360f / segments;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;

                float x = centerPosition.x + Mathf.Cos(angle) * wave.currentRadius;
                float z = centerPosition.z + Mathf.Sin(angle) * wave.currentRadius;
                float y = centerPosition.y;

                wave.lineRenderer.SetPosition(i, new Vector3(x, y, z));
            }
        }

        private float CalculateAlpha(float radius)
        {
            // 开始时淡入
            float fadeIn = Mathf.Min(1f, radius / waveSpacing);
            // 结束时淡出
            float fadeOut = 1f - (radius - maxRadius) / waveSpacing;
            return Mathf.Clamp01(fadeIn * fadeOut) * waveColor.a;
        }

        private void KnockbackPlayer(Vector3 playerPos, Vector3 waveCenter)
        {
            if (playerCharacter == null) return;

            // 计算击退方向（从波中心向外）
            Vector3 knockbackDir = (playerPos - waveCenter).normalized;
            knockbackDir.y = 0; // 保持水平方向

            // 使用游戏原生的SetForceMoveVelocity进行击退
            Vector3 knockbackVelocity = knockbackDir * knockbackForce + Vector3.up * knockbackUpwardForce;
            playerCharacter.SetForceMoveVelocity(knockbackVelocity);

            // 启动协程在短时间后清除强制移动（保存协程引用）
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
            yield return knockbackClearTime; // delay = 0.3f
            
            // 清除强制移动，让玩家恢复正常控制
            if (playerCharacter != null)
            {
                playerCharacter.SetForceMoveVelocity(Vector3.zero);
            }
        }

        private void OnDestroy()
        {
            // 停止所有协程（防止协程泄漏）
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

            // 清理所有波纹（包括Material）
            foreach (var wave in waveRings)
            {
                if (wave != null && wave.lineRenderer != null)
                {
                    // 销毁动态创建的Material
                    if (wave.lineRenderer.material != null)
                    {
                        Destroy(wave.lineRenderer.material);
                    }
                    Destroy(wave.lineRenderer.gameObject);
                }
            }
            waveRings.Clear();
        }

        // ========== 内部类 ==========

        private class WaveRing
        {
            public int index;
            public LineRenderer lineRenderer;
            public float currentRadius;
            public bool hasHitPlayer;
        }
    }
}
