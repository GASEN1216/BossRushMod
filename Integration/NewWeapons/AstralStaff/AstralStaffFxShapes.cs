// ============================================================================
// AstralStaffFxShapes.cs - 星阙的两种几何表现：弧形光带与多层光线
// ============================================================================
// 2026-10-08 从 AstralStaffFx.cs 原样拆出（LargeFileBudgetGuard 单文件 1200 行上限），行为逐字不变。
//   AstralStaffRibbon —— 横扫 / 回旋 / 冲击波用的弧形光带（程序网格，显出→淡出）。
//   AstralStaffBeam   —— 星陨的巨型光棍、落点光柱、地裂纹、命中斩痕与流星（线条，淡出自毁）。
// 两者都经 AstralStaffFx.CreateTransient 归当前手持的瞬时表现 owner，换手 / 死亡 / 切图立即撤销。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    // ========================================================================
    // 弧形光带（横扫 / 回旋 / 冲击波）
    // ========================================================================

    /// <summary>
    /// 一条贴着地面（或略高于地面）的扇形 / 环形光带。显出阶段沿弧方向推进，之后整体淡出并自毁。
    /// 网格只建一次，动画只改顶点色；自毁时销毁网格。
    /// </summary>
    internal sealed class AstralStaffRibbon : MonoBehaviour
    {
        private const int Segments = 40;

        private Mesh _mesh;
        private Color[] _colors;
        private float[] _segT;
        private Color _color;
        private float _reveal;
        private float _hold;
        private float _fade;
        private float _t;
        private float _innerAlpha;
        private float _growFrom;
        private bool _grow;
        private Transform _follow;

        /// <summary>
        /// center：圆心；forward：扇形中线方向；sweep：总角度（度，负值反向推进）；
        /// inner/outer：内外半径；reveal/hold/fade：三段时长（秒，按未缩放时间走，顿帧时光带照样展开）。
        /// growFrom&gt;0 时半径从 growFrom 倍长到 1 倍（冲击波）。
        /// </summary>
        internal static AstralStaffRibbon Play(Vector3 center, Vector3 forward, float sweep, float inner, float outer,
            Color color, float reveal, float hold, float fade, float innerAlpha, float growFrom, Transform follow)
        {
            GameObject go = null;
            try
            {
                go = AstralStaffFx.CreateTransient("AstralStaff_Ribbon");
                if (go == null) return null;
                go.transform.position = center;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                go.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                AstralStaffRibbon ribbon = go.AddComponent<AstralStaffRibbon>();
                ribbon.Init(sweep, inner, outer, color, reveal, hold, fade, innerAlpha, growFrom, follow);
                return ribbon;
            }
            catch (Exception e)
            {
                if (go != null) Destroy(go);
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 光带失败: " + e.Message);
                return null;
            }
        }

        private void Init(float sweep, float inner, float outer, Color color, float reveal, float hold, float fade,
            float innerAlpha, float growFrom, Transform follow)
        {
            _color = color;
            _reveal = Mathf.Max(0.001f, reveal);
            _hold = Mathf.Max(0f, hold);
            _fade = Mathf.Max(0.01f, fade);
            _innerAlpha = innerAlpha;
            _growFrom = growFrom;
            _grow = growFrom > 0f;
            _follow = follow;

            int vertexCount = (Segments + 1) * 2;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            _colors = new Color[vertexCount];
            _segT = new float[Segments + 1];
            int[] triangles = new int[Segments * 6];
            float start = -sweep * 0.5f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                _segT[i] = t;
                float angle = (start + sweep * t) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2;
                int k = i * 6;
                triangles[k] = v;
                triangles[k + 1] = v + 1;
                triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 1;
                triangles[k + 4] = v + 3;
                triangles[k + 5] = v + 2;
            }

            _mesh = new Mesh();
            _mesh.name = "AstralStaff_RibbonMesh";
            _mesh.MarkDynamic();
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
            Bounds bounds = _mesh.bounds;
            bounds.Expand(outer);
            _mesh.bounds = bounds;

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _t += Time.unscaledDeltaTime;
            if (_follow != null)
            {
                Vector3 p = _follow.position;
                transform.position = new Vector3(p.x, transform.position.y, p.z);
            }
            Apply();
            if (_t >= _reveal + _hold + _fade) Destroy(gameObject);
        }

        private void Apply()
        {
            if (_mesh == null) return;
            float revealT = Mathf.Clamp01(_t / _reveal);
            float headT = 1f - (1f - revealT) * (1f - revealT);
            float fadeK = 1f - Mathf.Clamp01((_t - _reveal - _hold) / _fade);
            if (_grow)
            {
                float g = Mathf.Lerp(_growFrom, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(_t / (_reveal + _hold + _fade)), 3f));
                transform.localScale = new Vector3(g, 1f, g);
            }
            for (int i = 0; i < _segT.Length; i++)
            {
                float t = _segT[i];
                // 刚扫过的位置最亮（刀锋），越往后越暗；头部之前不可见
                float visible = _grow ? 1f : (t <= headT ? 1f : 0f);
                float trail = _grow ? 1f : 0.08f + 0.92f * Mathf.Exp(-Mathf.Max(0f, headT - t) * 6f);
                float edge = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 8f + (_grow ? 1f : 0f));
                float a = _color.a * visible * trail * fadeK * edge;
                Color outer = new Color(_color.r, _color.g, _color.b, a);
                Color inner = new Color(_color.r, _color.g, _color.b, a * _innerAlpha);
                _colors[i * 2] = inner;
                _colors[i * 2 + 1] = outer;
            }
            _mesh.colors = _colors;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }
    }

    // ========================================================================
    // 线条特效（巨型光棍 / 光柱 / 地裂纹）
    // ========================================================================

    /// <summary>多层线条：白芯 + 主色辉光。淡出后自毁。位置可在生命期内由调用方逐帧改写。</summary>
    internal sealed class AstralStaffBeam : MonoBehaviour
    {
        private LineRenderer _core;
        private LineRenderer _glow;
        private float _coreWidth;
        private float _glowWidth;
        private Color _coreColor;
        private Color _glowColor;
        private float _life;
        private float _fade;
        private float _t;
        private bool _useGameTime;

        internal static AstralStaffBeam Create(Vector3[] points, float coreWidth, float glowWidth, Color glowColor,
            float life, float fade, bool flatOnGround, bool useGameTime = false)
        {
            GameObject go = null;
            try
            {
                go = AstralStaffFx.CreateTransient("AstralStaff_Beam");
                if (go == null) return null;
                AstralStaffBeam beam = go.AddComponent<AstralStaffBeam>();
                beam._useGameTime = useGameTime;
                beam.Init(points, coreWidth, glowWidth, glowColor, life, fade, flatOnGround);
                return beam;
            }
            catch (Exception e)
            {
                if (go != null) Destroy(go);
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 光线失败: " + e.Message);
                return null;
            }
        }

        private void Init(Vector3[] points, float coreWidth, float glowWidth, Color glowColor, float life, float fade, bool flat)
        {
            Material strip = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            _coreWidth = coreWidth;
            _glowWidth = glowWidth;
            _glowColor = glowColor;
            _coreColor = Color.Lerp(glowColor, AstralStaffConfig.CoreWhite, 0.6f);
            _coreColor.a = Mathf.Min(0.85f, glowColor.a);
            _life = Mathf.Max(0.01f, life);
            _fade = Mathf.Max(0.01f, fade);
            _glow = AstralStaffFx.CreateLine(transform, "Glow", strip, glowWidth, glowColor, true);
            _core = AstralStaffFx.CreateLine(transform, "Core", strip, coreWidth, _coreColor, true);
            if (flat)
            {
                _glow.alignment = LineAlignment.TransformZ;
                _core.alignment = LineAlignment.TransformZ;
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            SetPoints(points);
            Apply(1f);
        }

        internal void SetPoints(Vector3[] points)
        {
            if (points == null || _core == null) return;
            _core.positionCount = points.Length;
            _glow.positionCount = points.Length;
            _core.SetPositions(points);
            _glow.SetPositions(points);
        }

        internal void SetWidthScale(float scale)
        {
            if (_core == null) return;
            _core.widthMultiplier = _coreWidth * scale;
            _glow.widthMultiplier = _glowWidth * scale;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _t += _useGameTime ? Time.deltaTime : Time.unscaledDeltaTime;
            float k = _t <= _life ? 1f : 1f - Mathf.Clamp01((_t - _life) / _fade);
            Apply(k);
            if (_t >= _life + _fade) Destroy(gameObject);
        }

        private void Apply(float k)
        {
            if (_core == null) return;
            Color c = new Color(_coreColor.r, _coreColor.g, _coreColor.b, _coreColor.a * k);
            Color g = new Color(_glowColor.r, _glowColor.g, _glowColor.b, _glowColor.a * k);
            _core.startColor = c;
            _core.endColor = c;
            _glow.startColor = g;
            _glow.endColor = g;
        }
    }
}
