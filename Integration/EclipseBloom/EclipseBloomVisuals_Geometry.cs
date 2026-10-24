using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>封闭金属叶片、切面日核和地面扇区，不依赖外部 bundle。</summary>
    internal static class EclipseBloomVisualGeometry
    {
        private static readonly float[] PetalZ = { -1.75f, -1.25f, -0.5f, 0.3f, 1.08f, 1.65f };
        private static readonly float[] PetalWidth = { 0.12f, 0.57f, 0.93f, 0.82f, 0.43f, 0.015f };
        private static readonly float[] PetalRidge = { 0.02f, 0.17f, 0.31f, 0.33f, 0.23f, 0.11f };

        internal static Mesh CreatePetal()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            // 四条独立纵棱：上面的脊线 / 左右刃口 / 下方脊线组成有厚度的封闭梭形。
            for (int i = 0; i < PetalZ.Length; i++)
            {
                float z = PetalZ[i];
                float width = PetalWidth[i];
                float edge = 0.03f + (z + 1.75f) * 0.035f;
                vertices.Add(new Vector3(0f, PetalRidge[i] + edge, z));
                vertices.Add(new Vector3(width, edge, z));
                vertices.Add(new Vector3(0f, edge - 0.22f, z));
                vertices.Add(new Vector3(-width, edge, z));
            }
            for (int i = 0; i < PetalZ.Length - 1; i++)
                for (int side = 0; side < 4; side++)
                {
                    int a = i * 4 + side;
                    int b = i * 4 + (side + 1) % 4;
                    int c = a + 4;
                    int d = b + 4;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            triangles.Add(0); triangles.Add(1); triangles.Add(2);
            triangles.Add(0); triangles.Add(2); triangles.Add(3);
            int end = (PetalZ.Length - 1) * 4;
            triangles.Add(end); triangles.Add(end + 2); triangles.Add(end + 1);
            triangles.Add(end); triangles.Add(end + 3); triangles.Add(end + 2);
            return Facet("EclipseBloom_ClosedMetalPetal", vertices, triangles);
        }

        internal static Vector3[] GetPetalOutline()
        {
            Vector3[] outline = new Vector3[PetalZ.Length * 2 + 1];
            for (int i = 0; i < PetalZ.Length; i++)
            {
                float y = 0.045f + (PetalZ[i] + 1.75f) * 0.035f;
                outline[i] = new Vector3(PetalWidth[i], y, PetalZ[i]);
                int back = PetalZ.Length * 2 - 1 - i;
                outline[back] = new Vector3(-PetalWidth[i], y, PetalZ[i]);
            }
            outline[outline.Length - 1] = outline[0];
            return outline;
        }

        internal static Mesh CreateFacetedCore(int segments, int rings)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            segments = Mathf.Max(3, segments);
            rings = Mathf.Max(2, rings);
            vertices.Add(Vector3.up);
            for (int y = 1; y < rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x < segments; x++)
                {
                    float angle = Mathf.PI * 2f * x / segments + (y % 2) * Mathf.PI / segments;
                    float ripple = 1f + 0.045f * Mathf.Sin(x * 2.7f + y * 4.1f);
                    vertices.Add(new Vector3(Mathf.Sin(phi) * Mathf.Sin(angle), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Cos(angle)) * ripple);
                }
            }
            int south = vertices.Count;
            vertices.Add(-Vector3.up);
            for (int x = 0; x < segments; x++)
            {
                int next = (x + 1) % segments;
                triangles.Add(0); triangles.Add(1 + x); triangles.Add(1 + next);
                int last = 1 + (rings - 2) * segments;
                triangles.Add(last + x); triangles.Add(south); triangles.Add(last + next);
            }
            for (int y = 0; y < rings - 2; y++)
                for (int x = 0; x < segments; x++)
                {
                    int a = 1 + y * segments + x;
                    int next = 1 + y * segments + (x + 1) % segments;
                    int b = a + segments;
                    triangles.Add(a); triangles.Add(b); triangles.Add(next);
                    triangles.Add(next); triangles.Add(b); triangles.Add(next + segments);
                }
            return Facet("EclipseBloom_FacetedBlackSun", vertices, triangles);
        }

        internal static Mesh CreateFragment()
        {
            Vector3[] points = { new Vector3(-0.22f, 0f, -0.34f), new Vector3(0.31f, 0.03f, -0.17f),
                new Vector3(0.04f, 0.07f, 0.52f), new Vector3(-0.04f, 0.22f, 0f), new Vector3(0f, -0.1f, 0f) };
            int[] indices = { 0, 3, 1, 1, 3, 2, 2, 3, 0, 0, 1, 4, 1, 2, 4, 2, 0, 4 };
            return Facet("EclipseBloom_ObsidianSplinter", new List<Vector3>(points), new List<int>(indices));
        }

        private static Mesh Facet(string name, List<Vector3> positions, List<int> indices)
        {
            Vector3[] vertices = new Vector3[indices.Count];
            Vector2[] uvs = new Vector2[indices.Count];
            int[] triangles = new int[indices.Count];
            for (int i = 0; i < indices.Count; i++)
            {
                vertices[i] = positions[indices[i]];
                uvs[i] = new Vector2(vertices[i].x * 0.2f + 0.5f, vertices[i].z * 0.2f + 0.5f);
                triangles[i] = i;
            }
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh CreateSector(int sector, float inner, float outer, int segments)
        {
            const int radialSteps = 13;
            Vector3[] vertices = new Vector3[(segments + 1) * (radialSteps + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[segments * radialSteps * 6];
            for (int ring = 0; ring <= radialSteps; ring++)
            for (int i = 0; i <= segments; i++)
            {
                float offset = Mathf.Lerp(-EclipseBloomTuning.HalfAngleDegrees, EclipseBloomTuning.HalfAngleDegrees, (float)i / segments);
                float x, z;
                EclipseBloomRules.GetSectorPoint(sector, 0f, Mathf.Lerp(inner, outer, (float)ring / radialSteps), offset, out x, out z);
                int v = ring * (segments + 1) + i;
                vertices[v] = new Vector3(x, 0f, z);
                uv[v] = new Vector2(0.5f, 0.5f);
                colors[v] = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.65f, (float)ring / radialSteps));
                if (i == segments || ring == radialSteps) continue;
                int t = (ring * segments + i) * 6;
                triangles[t] = v; triangles[t + 1] = v + segments + 1; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + segments + 1; triangles[t + 4] = v + segments + 2; triangles[t + 5] = v + 1;
            }
            Mesh mesh = new Mesh();
            mesh.name = "EclipseBloom_RuleSector_" + sector;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        internal static Vector3[] SectorContour(int sector)
        {
            const int steps = 36;
            Vector3[] points = new Vector3[(steps + 1) * 2 + 1];
            for (int i = 0; i <= steps; i++)
            {
                float offset = Mathf.Lerp(-EclipseBloomTuning.HalfAngleDegrees, EclipseBloomTuning.HalfAngleDegrees, (float)i / steps);
                float x, z;
                EclipseBloomRules.GetSectorPoint(sector, 0f, EclipseBloomTuning.OuterRadius, offset, out x, out z);
                points[i] = new Vector3(x, 0f, z);
                EclipseBloomRules.GetSectorPoint(sector, 0f, EclipseBloomTuning.InnerRadius, offset, out x, out z);
                points[steps * 2 + 1 - i] = new Vector3(x, 0f, z);
            }
            points[points.Length - 1] = points[0];
            return points;
        }

        internal static Vector3[] Circle(float radius, int segments, float height, float irregularity)
        {
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float r = radius * (1f + irregularity * Mathf.Sin(angle * 7f) * Mathf.Sin(angle * 11f + 0.4f));
                points[i] = new Vector3(Mathf.Sin(angle) * r, height, Mathf.Cos(angle) * r);
            }
            return points;
        }
    }
}
