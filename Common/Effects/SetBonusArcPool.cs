// ============================================================================
// SetBonusArcPool.cs - 套装折线电弧的 LineRenderer 实例池
// ============================================================================
// 模块说明：
//   雷霆套装的连锁闪电、反震电弧、肩部环境电弧共用这一个池。
//   从 ModBehaviour 的 partial 里抽出来是有原因的：这段只跟 LineRenderer、材质和自身协程打交道，
//   不读任何模式状态，留在宿主上只会让 ModBehaviour 的职责继续膨胀
//   （ModBehaviourPartialBudgetGuard 明确反对「把同一个宿主拆成更多文件」）。
//
// 生命周期（AGENTS.md 4.12）：
//   由套装激活时惰性创建（EnsureInstance）、停用时 Destroy；不穿套装时零对象、零每帧工作。
//   池中对象随场景销毁后在 Unity 里 == null 为真，Rent 前先剔空槽。
//   池满时丢弃这一道纯视觉，不扩容。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush.Common.Effects
{
    /// <summary>套装电弧池。由 ModBehaviour 的表现层门面 EnsureSetArcPool / DestroySetArcPool 持有。</summary>
    public class SetBonusArcPool : MonoBehaviour
    {
        private const int PoolMax = 12;
        private const int Segments = 8;
        private const float ResampleInterval = 0.03f;

        private readonly List<LineRenderer> idle = new List<LineRenderer>(PoolMax);
        private readonly List<LineRenderer> active = new List<LineRenderer>(PoolMax);
        private Material sharedMaterial;

        /// <summary>创建一个挂在自己 GameObject 上的池实例（DontDestroyOnLoad 由调用方决定，默认跟随场景）。</summary>
        public static SetBonusArcPool Create()
        {
            GameObject host = new GameObject("SetBonusArcPool");
            return host.AddComponent<SetBonusArcPool>();
        }

        /// <summary>
        /// 从 from 到 to 画一道折线电弧，life 秒内每 0.03 秒重采样抖动并线性淡出，结束后回池。
        /// </summary>
        public void Spawn(Vector3 from, Vector3 to, Color color, float width, float life)
        {
            try
            {
                LineRenderer lr = Rent();
                if (lr == null) return;

                lr.startWidth = width;
                lr.endWidth = width * 0.6f;
                lr.positionCount = Segments + 1;
                Resample(lr, from, to, width * 3f);
                ApplyColor(lr, color, color.a);
                lr.gameObject.SetActive(true);
                StartCoroutine(Animate(lr, from, to, color, width, life));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusArcPool] Spawn 出错: " + e.Message);
            }
        }

        private LineRenderer Rent()
        {
            // 随场景销毁的对象在 Unity 里 == null 为 true，先把空槽剔掉
            for (int i = idle.Count - 1; i >= 0; i--)
            {
                if (idle[i] == null) idle.RemoveAt(i);
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null) active.RemoveAt(i);
            }

            LineRenderer lr;
            if (idle.Count > 0)
            {
                lr = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
            }
            else if (active.Count >= PoolMax)
            {
                return null;
            }
            else
            {
                lr = CreateRenderer();
            }

            if (lr != null)
            {
                active.Add(lr);
            }
            return lr;
        }

        private LineRenderer CreateRenderer()
        {
            GameObject go = new GameObject("SetBonusArc");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 120;

            Material material = GetMaterial();
            if (material != null)
            {
                lr.material = material;
            }
            return lr;
        }

        private Material GetMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            sharedMaterial = new Material(shader);
            sharedMaterial.name = "SetBonusArcMat";
            return sharedMaterial;
        }

        private static void ApplyColor(LineRenderer lr, Color color, float alpha)
        {
            lr.startColor = new Color(color.r, color.g, color.b, alpha);
            lr.endColor = new Color(color.r, color.g, color.b, alpha * 0.6f);
        }

        private static void Resample(LineRenderer lr, Vector3 from, Vector3 to, float jitter)
        {
            Vector3 direction = to - from;
            Vector3 side = Vector3.Cross(direction.normalized, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }

            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                Vector3 point = Vector3.Lerp(from, to, t);
                if (i > 0 && i < Segments)
                {
                    float amplitude = jitter * Mathf.Sin(t * Mathf.PI);   // 两端钉死，中段抖动
                    point += side * UnityEngine.Random.Range(-amplitude, amplitude)
                           + Vector3.up * (UnityEngine.Random.Range(-amplitude, amplitude) * 0.5f);
                }
                lr.SetPosition(i, point);
            }
        }

        private IEnumerator Animate(LineRenderer lr, Vector3 from, Vector3 to, Color color, float width, float life)
        {
            float elapsed = 0f;
            float nextResample = 0f;
            while (elapsed < life && lr != null)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= nextResample)
                {
                    Resample(lr, from, to, width * 3f);
                    nextResample = elapsed + ResampleInterval;
                }
                ApplyColor(lr, color, Mathf.Lerp(color.a, 0f, elapsed / life));
                yield return null;
            }

            Return(lr);
        }

        private void Return(LineRenderer lr)
        {
            if (lr == null) return;   // 已随场景销毁；Rent 会清掉空槽

            active.Remove(lr);
            lr.gameObject.SetActive(false);
            if (idle.Count < PoolMax)
            {
                idle.Add(lr);
            }
            else
            {
                UnityEngine.Object.Destroy(lr.gameObject);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (sharedMaterial != null)
                {
                    UnityEngine.Object.Destroy(sharedMaterial);
                    sharedMaterial = null;
                }
                idle.Clear();
                active.Clear();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusArcPool] OnDestroy 出错: " + e.Message);
            }
        }
    }
}
