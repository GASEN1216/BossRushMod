using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    internal static class EclipseBloomVisuals
    {
        internal static readonly Color Sun = new Color(1f, 0.56f, 0.12f, 1f);
        internal static readonly Color PaleGold = new Color(1f, 0.82f, 0.4f, 1f);
        internal static readonly Color Ash = new Color(0.085f, 0.065f, 0.09f, 0.45f);

        internal static LineRenderer Line(string name, Transform parent, Vector3[] points, Color color, float width, bool hot)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sharedMaterial = BossRushFxKit.GetMaterial(hot ? BossRushFxBlend.Additive : BossRushFxBlend.Alpha,
                Texture2D.whiteTexture, hot ? BossRushFxKit.GainBright : BossRushFxKit.GainSoft);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (line.sharedMaterial == null) line.enabled = false;
            return line;
        }

        internal static void Tint(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null) return;
            block.Clear();
            block.SetVector("_BaseColor", new Vector4(color.r, color.g, color.b, color.a));
            block.SetVector("_TintColor", new Vector4(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, color.a * 0.5f));
            renderer.SetPropertyBlock(block);
        }

        internal static void AddPetalInlay(Transform petal)
        {
            Line("GoldCutEdge", petal, EclipseBloomVisualGeometry.GetPetalOutline(), new Color(0.9f, 0.46f, 0.09f), 0.026f, true);
            // 多处分叉的熔金裂纹随真实叶面起伏；正反面都有纹理，翻开后仍能辨认。
            Vector3[] vein = { new Vector3(0f, 0.10f, -1.62f), new Vector3(-0.06f, 0.29f, -1.05f),
                new Vector3(0.08f, 0.40f, -0.42f), new Vector3(-0.04f, 0.46f, 0.26f),
                new Vector3(0.08f, 0.37f, 0.88f), new Vector3(0f, 0.25f, 1.56f) };
            Line("MoltenCentralVein", petal, vein, PaleGold, 0.023f, true);
            Line("VeinBranchA", petal, new Vector3[] { vein[2], new Vector3(-0.30f, 0.29f, -0.28f),
                new Vector3(-0.39f, 0.20f, 0.12f), new Vector3(-0.69f, 0.14f, 0.42f) }, Sun, 0.018f, true);
            Line("VeinBranchB", petal, new Vector3[] { vein[3], new Vector3(0.26f, 0.30f, 0.52f),
                new Vector3(0.24f, 0.26f, 0.81f), new Vector3(0.38f, 0.20f, 1.03f) }, Sun, 0.014f, true);
            Line("UnderLeafFissure", petal, new Vector3[] { new Vector3(0f, -0.20f, -1.5f), new Vector3(0.06f, -0.18f, -0.4f),
                new Vector3(-0.05f, -0.15f, 0.4f), new Vector3(0f, -0.10f, 1.45f) }, Sun, 0.023f, true);
        }

        internal static Mesh CreateDisc(string name, float radius)
        {
            const int steps = 64;
            Vector3[] vertices = new Vector3[steps + 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[steps * 3];
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                uv[i + 1] = new Vector2(Mathf.Sin(angle) * 0.5f + 0.5f, Mathf.Cos(angle) * 0.5f + 0.5f);
                if (i == steps) continue;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2;
            }
            Mesh mesh = new Mesh();
            mesh.name = name; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        internal static void ConfigureEmitter(ParticleSystem ps, Color color, float sizeMin, float sizeMax, float lifetimeMin, float lifetimeMax)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = BossRushFxKit.FadeGradient(new Color(color.r, color.g, color.b, 0f), color,
                new Color(color.r * 0.5f, color.g * 0.4f, color.b * 0.4f, 0f), 0.14f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 0.15f));
        }
    }

    /// <summary>危险区直接使用规则的半径、半角和采样方法；根坐标始终就是判伤原点。</summary>
    internal sealed class EclipseBloomSectorVisual
    {
        private readonly Transform _root;
        private readonly GameObject[] _sectors = new GameObject[EclipseBloomTuning.PetalCount];
        private readonly Renderer[] _fills = new Renderer[EclipseBloomTuning.PetalCount];
        private readonly LineRenderer[] _contours = new LineRenderer[EclipseBloomTuning.PetalCount];
        private readonly LineRenderer[] _fronts = new LineRenderer[EclipseBloomTuning.PetalCount];
        private readonly Vector3[] _frontPoints = new Vector3[37];
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly EclipseBloomGroundProjection _ground = new EclipseBloomGroundProjection();
        private readonly Mesh[] _meshes = new Mesh[4];
        private readonly Vector3[][] _vertices = new Vector3[4][];
        private readonly bool[][] _valid = new bool[4][];
        private readonly int[][] _sourceIndices = new int[4][];
        private readonly System.Collections.Generic.List<int>[] _indices = new System.Collections.Generic.List<int>[4];
        private readonly Vector3[][] _outlinePoints = new Vector3[4][];

        internal EclipseBloomSectorVisual(Transform parent, EclipseBloomAssetManager assets)
        {
            _root = new GameObject("RuleAlignedGroundTelegraph").transform;
            _root.SetParent(parent, false);
            // XZ 仍由规则生成；只投影 Y。判伤同时检查最终绘制三角面，缺失地面不会隐形伤人。
            for (int i = 0; i < _sectors.Length; i++)
            {
                GameObject sector = new GameObject("Sector_" + i);
                sector.transform.SetParent(_root, false);
                Mesh mesh = assets.Own(EclipseBloomVisualGeometry.CreateSector(i, EclipseBloomTuning.InnerRadius,
                    EclipseBloomTuning.OuterRadius, 36));
                _meshes[i] = mesh;
                mesh.MarkDynamic();
                _vertices[i] = mesh.vertices;
                _valid[i] = new bool[_vertices[i].Length];
                _sourceIndices[i] = mesh.triangles;
                _indices[i] = new System.Collections.Generic.List<int>(_sourceIndices[i].Length);
                _outlinePoints[i] = EclipseBloomVisualGeometry.SectorContour(i);
                _fills[i] = EclipseBloomAssetManager.AddMesh("HazardFill", sector.transform, mesh,
                    BossRushFxKit.GetMaterial(BossRushFxBlend.Alpha, Texture2D.whiteTexture, BossRushFxKit.GainSoft));
                _fills[i].shadowCastingMode = ShadowCastingMode.Off;
                _fills[i].receiveShadows = false;
                _contours[i] = EclipseBloomVisuals.Line("ExactDamageBoundary", sector.transform,
                    EclipseBloomVisualGeometry.SectorContour(i), EclipseBloomVisuals.Sun, 0.055f, true);
                _fronts[i] = EclipseBloomVisuals.Line("ChargeFront", sector.transform,
                    new Vector3[37], EclipseBloomVisuals.PaleGold, 0.055f, true);
                _fronts[i].transform.localPosition = Vector3.up * 0.015f;
                _sectors[i] = sector;
                sector.SetActive(false);
            }
        }

        internal void Apply(int intactMask, int attackMask, float rotationDegrees, float progress, bool damaging)
        {
            PrepareGround();
            _root.localRotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            int activeMask = intactMask & attackMask;
            float p = Mathf.Clamp01(progress);
            for (int i = 0; i < _sectors.Length; i++)
            {
                bool active = (activeMask & (1 << i)) != 0 && (p > 0f || damaging);
                if (_sectors[i].activeSelf != active) _sectors[i].SetActive(active);
                if (!active) continue;
                ProjectSector(i);
                Color fill = damaging ? new Color(1f, 0.20f, 0.015f, 0.32f)
                    : new Color(0.95f, 0.43f, 0.045f, 0.045f + p * 0.13f);
                EclipseBloomVisuals.Tint(_fills[i], _block, fill);
                // 预警最后四分之一边线加速明灭，提示「马上出伤」；出伤段转成白热。
                float urgency = damaging ? 0f : Mathf.Clamp01((p - 0.75f) / 0.25f);
                float flicker = 0.5f + 0.5f * Mathf.Sin(Time.time * (10f + urgency * 16f));
                Color edge = damaging ? new Color(1f, 0.82f, 0.42f, 1f)
                    : Color.Lerp(new Color(1f, 0.63f, 0.20f, 0.35f + p * 0.55f), new Color(1f, 0.9f, 0.6f, 1f), urgency * flicker);
                _contours[i].startColor = _contours[i].endColor = edge;
                float radius = Mathf.Lerp(EclipseBloomTuning.InnerRadius, EclipseBloomTuning.OuterRadius, damaging ? 1f : p);
                for (int point = 0; point < _frontPoints.Length; point++)
                {
                    float offset = Mathf.Lerp(-EclipseBloomTuning.HalfAngleDegrees, EclipseBloomTuning.HalfAngleDegrees,
                        (float)point / (_frontPoints.Length - 1));
                    float x, z;
                    EclipseBloomRules.GetSectorPoint(i, 0f, radius, offset, out x, out z);
                    _frontPoints[point] = new Vector3(x, 0f, z);
                }
                _fronts[i].enabled = ProjectLine(_frontPoints);
                _fronts[i].SetPositions(_frontPoints);
                _fronts[i].startColor = _fronts[i].endColor = edge;
            }
        }

        internal void PrepareGround() { _ground.Step(_root.position); }

        private bool ProjectPoint(ref Vector3 point)
        {
            Vector3 world = _root.TransformPoint(new Vector3(point.x, 0f, point.z));
            float height;
            if (!_ground.TryHeight(world, out height)) return false;
            point.y = height - _root.position.y + 0.045f;
            return true;
        }

        private bool ProjectLine(Vector3[] points)
        {
            bool complete = true;
            for (int j = 0; j < points.Length; j++) complete &= ProjectPoint(ref points[j]);
            return complete;
        }

        private void ProjectSector(int sector)
        {
            Vector3[] points = _vertices[sector];
            bool[] valid = _valid[sector];
            for (int j = 0; j < points.Length; j++) valid[j] = ProjectPoint(ref points[j]);
            var indices = _indices[sector];
            indices.Clear();
            int[] original = _sourceIndices[sector];
            for (int j = 0; j < original.Length; j += 3)
                if (valid[original[j]] && valid[original[j + 1]] && valid[original[j + 2]])
                {
                    indices.Add(original[j]); indices.Add(original[j + 1]); indices.Add(original[j + 2]);
                }
            _meshes[sector].vertices = points;
            _meshes[sector].SetTriangles(indices, 0);
            _meshes[sector].RecalculateBounds();
            _contours[sector].enabled = ProjectLine(_outlinePoints[sector]);
            _contours[sector].SetPositions(_outlinePoints[sector]);
        }

        internal bool CoversPoint(Vector3 world)
        {
            Vector3 point = _root.InverseTransformPoint(world);
            float angle = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
            int radial = Mathf.Clamp(Mathf.FloorToInt((new Vector2(point.x, point.z).magnitude - EclipseBloomTuning.InnerRadius)
                / (EclipseBloomTuning.OuterRadius - EclipseBloomTuning.InnerRadius) * 13f), 0, 12);
            for (int sector = 0; sector < 4; sector++)
            {
                if (!_sectors[sector].activeInHierarchy || !_fills[sector].enabled
                    || _fills[sector].forceRenderingOff || _fills[sector].sharedMaterial == null) continue;
                float offset = Mathf.DeltaAngle(sector * 90f, angle);
                if (Mathf.Abs(offset) > EclipseBloomTuning.HalfAngleDegrees) continue;
                int column = Mathf.Clamp(Mathf.FloorToInt((offset + EclipseBloomTuning.HalfAngleDegrees)
                    / (EclipseBloomTuning.HalfAngleDegrees * 2f) * 36f), 0, 35);
                // 扇形弦长略短于圆弧，检查相邻径向格，最终以真实三角面为准。
                for (int ring = Mathf.Max(0, radial - 1); ring <= Mathf.Min(12, radial + 1); ring++)
                {
                    int a = ring * 37 + column, b = a + 37, c = a + 1, d = b + 1;
                    Vector3[] vertices = _vertices[sector];
                    bool[] valid = _valid[sector];
                    if (valid[a] && valid[b] && valid[c] && EclipseBloomGroundProjection.ContainsTriangle(point, vertices[a], vertices[b], vertices[c])) return true;
                    if (valid[b] && valid[d] && valid[c] && EclipseBloomGroundProjection.ContainsTriangle(point, vertices[b], vertices[d], vertices[c])) return true;
                }
            }
            return false;
        }

        internal void Hide()
        {
            for (int i = 0; i < _sectors.Length; i++) _sectors[i].SetActive(false);
        }
    }

}
