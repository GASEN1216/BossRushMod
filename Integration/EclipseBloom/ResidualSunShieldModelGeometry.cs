using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>盾片的封闭金属网格；X / Z 为盾面，Y 为厚度，掉落时背面朝地。</summary>
    internal static class ResidualSunShieldModelGeometry
    {
        private static readonly Vector2[] Outline = {
            new Vector2(-0.03f, -0.48f), new Vector2(-0.25f, -0.23f),
            new Vector2(-0.34f, 0.06f), new Vector2(-0.30f, 0.28f),
            new Vector2(-0.19f, 0.37f), new Vector2(-0.10f, 0.43f),
            new Vector2(-0.015f, 0.39f), new Vector2(0.07f, 0.43f),
            new Vector2(0.095f, 0.275f), new Vector2(0.175f, 0.31f),
            new Vector2(0.20f, 0.225f), new Vector2(0.29f, 0.275f),
            new Vector2(0.33f, 0.07f), new Vector2(0.24f, -0.23f),
            new Vector2(0.06f, -0.43f)
        };

        internal static Mesh Shell()
        {
            Builder b = new Builder();
            Vector3 center = new Vector3(-0.015f, 0.063f, 0f);
            for (int i = 0; i < Outline.Length; i++)
            {
                int next = (i + 1) % Outline.Length;
                Vector3 a = Point(Outline[i], 1f, 0.015f);
                Vector3 c = Point(Outline[next], 1f, 0.015f);
                Vector3 topA = Point(Outline[i], 0.88f, 0.06f);
                Vector3 topC = Point(Outline[next], 0.88f, 0.06f);
                Vector3 bottomA = Point(Outline[i], 0.94f, -0.036f);
                Vector3 bottomC = Point(Outline[next], 0.94f, -0.036f);
                b.Triangle(center, topA, topC);
                b.Quad(topA, a, c, topC);
                b.Quad(a, bottomA, bottomC, c);
                b.Triangle(new Vector3(0f, -0.036f, 0f), bottomC, bottomA);
            }
            return b.Finish("ResidualSunShield_ClosedObsidianPlate");
        }

        internal static Mesh Border()
        {
            Builder b = new Builder();
            for (int i = 0; i < Outline.Length; i++)
            {
                int next = (i + 1) % Outline.Length;
                Vector3 outerA = Point(Outline[i], 0.99f, 0.026f);
                Vector3 outerB = Point(Outline[next], 0.99f, 0.026f);
                Vector3 innerA = Point(Outline[i], 0.88f, 0.066f);
                Vector3 innerB = Point(Outline[next], 0.88f, 0.066f);
                b.Quad(innerA, outerA, outerB, innerB);
                // 金边有独立侧壁，低角度观察也有实体轮廓。
                b.Quad(outerA, outerA - Vector3.up * 0.014f,
                    outerB - Vector3.up * 0.014f, outerB);
                b.Quad(innerA, innerB, innerB - Vector3.up * 0.012f,
                    innerA - Vector3.up * 0.012f);
                b.Quad(innerA - Vector3.up * 0.012f, innerB - Vector3.up * 0.012f,
                    outerB - Vector3.up * 0.014f, outerA - Vector3.up * 0.014f);
            }
            return b.Finish("ResidualSunShield_ChippedGoldBevel");
        }

        internal static Mesh SunRing()
        {
            Builder b = new Builder();
            // 同一条封闭管的首末截面加端盖；保留右上方的真实缺口。
            ArcTube(b, new Vector3(-0.045f, 0.086f, 0.095f), 0.163f, 0.163f,
                0.014f, 48, 40f, 340f);
            for (int i = 0; i < 13; i++)
            {
                float angle = (i * 26f + 15f) * Mathf.Deg2Rad;
                if (i == 1 || i == 2) continue;
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 side = new Vector3(outward.z, 0f, -outward.x);
                Vector3 root = new Vector3(-0.045f, 0.080f, 0.095f) + outward * 0.19f;
                float length = i % 3 == 0 ? 0.047f : 0.028f;
                b.Tetra(root - side * 0.008f, root + side * 0.008f,
                    root + outward * length, root + Vector3.up * 0.012f);
            }
            return b.Finish("ResidualSunShield_FracturedSolarCrown");
        }

        internal static Mesh SunCore()
        {
            Builder b = new Builder();
            const int segments = 32;
            Vector3 center = new Vector3(-0.045f, 0.094f, 0.095f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * 0.143f, -0.02f, Mathf.Cos(a) * 0.143f);
                Vector3 q = center + new Vector3(Mathf.Sin(c) * 0.143f, -0.02f, Mathf.Cos(c) * 0.143f);
                b.Triangle(center, p, q);
                b.Quad(p, p - Vector3.up * 0.009f, q - Vector3.up * 0.009f, q);
                b.Triangle(center - Vector3.up * 0.029f, q - Vector3.up * 0.009f, p - Vector3.up * 0.009f);
            }
            return b.Finish("ResidualSunShield_FacetedBlackSun");
        }

        internal static Mesh Fissures()
        {
            Builder b = new Builder();
            Strip(b, new Vector2[] { new Vector2(0.075f, 0.36f), new Vector2(0.03f, 0.23f),
                new Vector2(0.065f, 0.13f), new Vector2(0.018f, -0.01f),
                new Vector2(0.057f, -0.12f), new Vector2(-0.03f, -0.37f) }, 0.007f);
            Strip(b, new Vector2[] { new Vector2(0.018f, -0.01f), new Vector2(-0.09f, -0.085f),
                new Vector2(-0.15f, -0.065f), new Vector2(-0.23f, -0.15f) }, 0.004f);
            Strip(b, new Vector2[] { new Vector2(0.057f, -0.12f), new Vector2(0.13f, -0.09f),
                new Vector2(0.19f, -0.16f) }, 0.003f);
            return b.Finish("ResidualSunShield_MoltenFaultLines");
        }

        internal static Mesh Handle()
        {
            Builder b = new Builder();
            ArcTube(b, new Vector3(-0.015f, -0.076f, -0.015f), 0.10f, 0.142f,
                0.014f, 36, 0f, 360f);
            b.Box(new Vector3(-0.015f, -0.047f, 0.115f), new Vector3(0.055f, 0.035f, 0.042f));
            b.Box(new Vector3(-0.015f, -0.047f, -0.15f), new Vector3(0.055f, 0.035f, 0.042f));
            return b.Finish("ResidualSunShield_BackGripAndRivets");
        }

        private static Vector3 Point(Vector2 point, float scale, float height)
        {
            return new Vector3(point.x * scale, height, point.y * scale);
        }

        private static void Strip(Builder b, Vector2[] points, float width)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 direction = (points[i + 1] - points[i]).normalized;
                Vector2 side = new Vector2(-direction.y, direction.x) * width;
                Vector3 a = Front(points[i] - side);
                Vector3 c = Front(points[i] + side);
                Vector3 d = Front(points[i + 1] + side * 0.65f);
                Vector3 e = Front(points[i + 1] - side * 0.65f);
                b.Quad(a, c, d, e);
                b.Quad(e - Vector3.up * 0.006f, d - Vector3.up * 0.006f,
                    c - Vector3.up * 0.006f, a - Vector3.up * 0.006f);
                b.Quad(a, e, e - Vector3.up * 0.006f, a - Vector3.up * 0.006f);
                b.Quad(d, c, c - Vector3.up * 0.006f, d - Vector3.up * 0.006f);
                b.Quad(c, a, a - Vector3.up * 0.006f, c - Vector3.up * 0.006f);
                b.Quad(e, d, d - Vector3.up * 0.006f, e - Vector3.up * 0.006f);
            }
        }

        private static Vector3 Front(Vector2 point)
        {
            float radius = (point - new Vector2(-0.045f, 0.095f)).magnitude;
            float height = radius < 0.143f ? 0.096f - 0.020f * radius / 0.143f : 0.068f;
            return Point(point, 1f, height);
        }

        private static void ArcTube(Builder b, Vector3 center, float radiusX, float radiusZ,
            float thickness, int segments, float beginDegrees, float endDegrees)
        {
            const int sides = 8;
            for (int segment = 0; segment < segments; segment++)
            {
                float a = Mathf.Lerp(beginDegrees, endDegrees, (float)segment / segments) * Mathf.Deg2Rad;
                float c = Mathf.Lerp(beginDegrees, endDegrees, (float)(segment + 1) / segments) * Mathf.Deg2Rad;
                for (int side = 0; side < sides; side++)
                {
                    float u = side * Mathf.PI * 2f / sides;
                    float v = (side + 1) * Mathf.PI * 2f / sides;
                    b.Quad(TubePoint(center, radiusX, radiusZ, thickness, a, u),
                        TubePoint(center, radiusX, radiusZ, thickness, c, u),
                        TubePoint(center, radiusX, radiusZ, thickness, c, v),
                        TubePoint(center, radiusX, radiusZ, thickness, a, v));
                }
            }
            if (endDegrees - beginDegrees >= 359.9f) return;
            float start = beginDegrees * Mathf.Deg2Rad, end = endDegrees * Mathf.Deg2Rad;
            Vector3 first = center + new Vector3(Mathf.Sin(start) * radiusX, 0f, Mathf.Cos(start) * radiusZ);
            Vector3 last = center + new Vector3(Mathf.Sin(end) * radiusX, 0f, Mathf.Cos(end) * radiusZ);
            for (int side = 0; side < sides; side++)
            {
                float u = side * Mathf.PI * 2f / sides, v = (side + 1) * Mathf.PI * 2f / sides;
                b.Triangle(first, TubePoint(center, radiusX, radiusZ, thickness, start, u),
                    TubePoint(center, radiusX, radiusZ, thickness, start, v));
                b.Triangle(last, TubePoint(center, radiusX, radiusZ, thickness, end, v),
                    TubePoint(center, radiusX, radiusZ, thickness, end, u));
            }
        }

        private static Vector3 TubePoint(Vector3 center, float radiusX, float radiusZ, float thickness, float angle, float around)
        {
            float radial = Mathf.Cos(around) * thickness;
            return center + new Vector3(Mathf.Sin(angle) * (radiusX + radial),
                Mathf.Sin(around) * thickness, Mathf.Cos(angle) * (radiusZ + radial));
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();

            internal void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c); Triangle(a, c, d);
            }

            internal void Tetra(Vector3 a, Vector3 b, Vector3 c, Vector3 top)
            {
                Triangle(a, top, b); Triangle(b, top, c); Triangle(c, top, a); Triangle(a, b, c);
            }

            internal void Box(Vector3 center, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                Vector3 a = center + new Vector3(-h.x, -h.y, -h.z);
                Vector3 b = center + new Vector3(h.x, -h.y, -h.z);
                Vector3 c = center + new Vector3(h.x, h.y, -h.z);
                Vector3 d = center + new Vector3(-h.x, h.y, -h.z);
                Vector3 e = center + new Vector3(-h.x, -h.y, h.z);
                Vector3 f = center + new Vector3(h.x, -h.y, h.z);
                Vector3 g = center + new Vector3(h.x, h.y, h.z);
                Vector3 j = center + new Vector3(-h.x, h.y, h.z);
                Quad(a, d, c, b); Quad(f, g, j, e); Quad(e, j, d, a);
                Quad(b, c, g, f); Quad(d, j, g, c); Quad(e, a, b, f);
            }

            internal Mesh Finish(string name)
            {
                Mesh mesh = new Mesh();
                mesh.name = name; mesh.hideFlags = HideFlags.HideAndDontSave;
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
