// ============================================================================
// SetBonusArcPool.cs - 套装 / 新武器折线电弧的 LineRenderer 实例池
// ============================================================================
// 模块说明：
//   雷霆套装的连锁闪电、反震电弧、肩部环境电弧，以及雷电戒指的释放电弧共用这一个实现。
//   从 ModBehaviour 的 partial 里抽出来是有原因的：这段只跟 LineRenderer、材质和自身协程打交道，
//   不读任何模式状态，留在宿主上只会让 ModBehaviour 的职责继续膨胀
//   （ModBehaviourPartialBudgetGuard 明确反对「把同一个宿主拆成更多文件」）。
//
// 2026-09-23 审美修（VA-05）：
//   旧电弧是 Sprites/Default 上无贴图的纯色扁带：边缘像剪纸、顶点色封顶 1 推不到泛光、
//   两端一样粗、线性淡出，读起来是一根淡蓝塑料丝。现在每道电弧两层：
//     - 外晕：2.2× 宽、主色、低 alpha；
//     - 白热芯：0.4× 宽、主色向白靠 70%、HDR 高档；
//   两层都用共享条带贴图（沿宽度柔边）+ 加色亮度档材质，宽度曲线两头收尖；
//   前 30% 寿命保持、之后二次淡出，每次重采样 alpha 再乘一点随机，做出噼啪的闪烁。
//
// 生命周期（AGENTS.md 4.12）：
//   由套装激活时惰性创建（EnsureInstance）、停用时 Destroy；不穿套装时零对象、零每帧工作。
//   池中对象随场景销毁后在 Unity 里 == null 为真，Rent 前先剔空槽。
//   池满时丢弃这一道纯视觉，不扩容。共享材质归 BossRushFxKit，本池只销毁自己兜底建的那份。
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
        /// <summary>同时存在的电弧道数上限（每道两根 LineRenderer：外晕 + 芯）。</summary>
        private const int PoolMax = 12;
        private const int Segments = 8;
        private const float ResampleInterval = 0.03f;
        private const float GlowWidthScale = 2.2f;
        private const float CoreWidthScale = 0.4f;
        private const float GlowAlpha = 0.35f;
        /// <summary>芯色 = 主色向白色靠 70%（冷色电弧是近白的蓝，雷电戒指的琥珀电弧是近白的暖黄）。</summary>
        private const float CoreWhiten = 0.7f;

        private sealed class Arc
        {
            public GameObject Root;
            public LineRenderer Glow;
            public LineRenderer Core;
        }

        private readonly List<Arc> idle = new List<Arc>(PoolMax);
        private readonly List<Arc> active = new List<Arc>(PoolMax);
        private readonly Vector3[] points = new Vector3[Segments + 1];
        private Material fallbackMaterial;
        private AnimationCurve taper;

        /// <summary>创建一个挂在自己 GameObject 上的池实例（DontDestroyOnLoad 由调用方决定，默认跟随场景）。</summary>
        public static SetBonusArcPool Create()
        {
            GameObject host = new GameObject("SetBonusArcPool");
            return host.AddComponent<SetBonusArcPool>();
        }

        /// <summary>
        /// 从 from 到 to 画一道折线电弧，life 秒内每 0.03 秒重采样抖动并闪烁淡出，结束后回池。
        /// width 是外观基准宽度（外晕 2.2×、芯 0.4×）。
        /// </summary>
        public void Spawn(Vector3 from, Vector3 to, Color color, float width, float life)
        {
            try
            {
                Arc arc = Rent();
                if (arc == null) return;

                SetupLine(arc.Glow, width * GlowWidthScale);
                SetupLine(arc.Core, width * CoreWidthScale);
                Resample(arc, from, to, width * 3f);
                ApplyColor(arc, color, 1f);
                arc.Root.SetActive(true);
                StartCoroutine(Animate(arc, from, to, color, width, Mathf.Max(0.02f, life)));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusArcPool] Spawn 出错: " + e.Message);
            }
        }

        private Arc Rent()
        {
            // 随场景销毁的对象在 Unity 里 == null 为 true，先把空槽剔掉
            for (int i = idle.Count - 1; i >= 0; i--)
            {
                if (idle[i] == null || idle[i].Root == null) idle.RemoveAt(i);
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null || active[i].Root == null) active.RemoveAt(i);
            }

            Arc arc;
            if (idle.Count > 0)
            {
                arc = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
            }
            else if (active.Count >= PoolMax)
            {
                return null;
            }
            else
            {
                arc = CreateArc();
            }

            if (arc != null)
            {
                active.Add(arc);
            }
            return arc;
        }

        private Arc CreateArc()
        {
            GameObject root = new GameObject("SetBonusArc");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            Arc arc = new Arc();
            arc.Root = root;
            arc.Glow = CreateRenderer(root.transform, "Glow", BossRushFxKit.GainBright, 120);
            arc.Core = CreateRenderer(root.transform, "Core", BossRushFxKit.GainHot, 121);
            return arc;
        }

        private LineRenderer CreateRenderer(Transform parent, string name, float gain, int sortingOrder)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = sortingOrder;
            lr.positionCount = Segments + 1;
            lr.widthCurve = GetTaper();

            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, gain);
            if (material == null) material = GetFallbackMaterial();
            if (material != null)
            {
                lr.sharedMaterial = material;
            }
            return lr;
        }

        /// <summary>宽度曲线：两头收尖，中段最宽（线头不再硬断）。</summary>
        private AnimationCurve GetTaper()
        {
            if (taper == null)
            {
                taper = new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.5f, 1f),
                    new Keyframe(1f, 0.35f));
            }
            return taper;
        }

        /// <summary>共享特效材质不可用时的兜底（旧版同款 Sprites/Default），由本池销毁。</summary>
        private Material GetFallbackMaterial()
        {
            if (fallbackMaterial != null) return fallbackMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            fallbackMaterial = new Material(shader);
            fallbackMaterial.name = "SetBonusArcMat";
            return fallbackMaterial;
        }

        private static void SetupLine(LineRenderer lr, float width)
        {
            if (lr == null) return;
            lr.widthMultiplier = Mathf.Max(0.005f, width);
        }

        private static void ApplyColor(Arc arc, Color color, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (arc.Glow != null)
            {
                Color glow = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a) * GlowAlpha * alpha);
                arc.Glow.startColor = glow;
                arc.Glow.endColor = glow;
            }
            if (arc.Core != null)
            {
                Color whitened = Color.Lerp(color, Color.white, CoreWhiten);
                Color core = new Color(whitened.r, whitened.g, whitened.b, alpha);
                arc.Core.startColor = core;
                arc.Core.endColor = core;
            }
        }

        private void Resample(Arc arc, Vector3 from, Vector3 to, float jitter)
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
                points[i] = point;
            }
            if (arc.Glow != null) arc.Glow.SetPositions(points);
            if (arc.Core != null) arc.Core.SetPositions(points);
        }

        private IEnumerator Animate(Arc arc, Vector3 from, Vector3 to, Color color, float width, float life)
        {
            float elapsed = 0f;
            float nextResample = 0f;
            float flicker = 1f;
            while (elapsed < life && arc != null && arc.Root != null)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= nextResample)
                {
                    Resample(arc, from, to, width * 3f);
                    flicker = UnityEngine.Random.Range(0.65f, 1f);
                    nextResample = elapsed + ResampleInterval;
                }
                // 前 30% 保持满亮，之后二次曲线淡出
                float t = Mathf.Clamp01((elapsed / life - 0.3f) / 0.7f);
                float fade = 1f - t * t;
                ApplyColor(arc, color, fade * flicker);
                yield return null;
            }

            Return(arc);
        }

        private void Return(Arc arc)
        {
            if (arc == null || arc.Root == null) return;   // 已随场景销毁；Rent 会清掉空槽

            active.Remove(arc);
            arc.Root.SetActive(false);
            if (idle.Count < PoolMax)
            {
                idle.Add(arc);
            }
            else
            {
                UnityEngine.Object.Destroy(arc.Root);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (fallbackMaterial != null)
                {
                    UnityEngine.Object.Destroy(fallbackMaterial);
                    fallbackMaterial = null;
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
