using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using UnityEngine;

internal static class GeometryRegression
{
    private sealed class Edge
    {
        internal int Count, Direction;
        internal readonly List<int> Triangles = new List<int>();
    }

    internal static void Run(Action<bool, string> check)
    {
        Mesh[] parts = { ResidualSunShieldModelGeometry.Shell(), ResidualSunShieldModelGeometry.Border(),
            ResidualSunShieldModelGeometry.SunRing(), ResidualSunShieldModelGeometry.SunCore(),
            ResidualSunShieldModelGeometry.Fissures(), ResidualSunShieldModelGeometry.Handle() };
        check(Math.Abs(parts.Min(mesh => mesh.vertices.Min(point => point.y)) + 0.09f) < 0.00001f,
            "production shield model back grip rests on its registered minus point zero nine pickup ground anchor");
        check(parts.All(mesh => mesh.vertices.All(point => point.y + 0.09f >= -0.00001f)),
            "ground anchored production shield model has no vertex below the pickup floor");
        foreach (Mesh mesh in parts) Validate(mesh, 1f, "shield", check);
        Validate(EclipseBloomVisualGeometry.CreatePetal(), 2f, "flower", check);
        Validate(EclipseBloomVisualGeometry.CreateFragment(), 1f, "flower", check);
        Validate(EclipseBloomVisualGeometry.CreateFacetedCore(24, 12), 1.1f, "flower", check);
    }

    private static void Validate(Mesh mesh, float envelope, string asset, Action<bool, string> check)
    {
        {
            check(mesh != null && mesh.vertices != null && mesh.triangles != null
                && mesh.vertices.Length > 0 && mesh.triangles.Length > 0 && mesh.triangles.Length % 3 == 0,
                "production " + asset + " model creates a nonempty indexed triangle mesh: " + mesh.name);
            Dictionary<string, int> weld = new Dictionary<string, int>();
            int[] ids = new int[mesh.vertices.Length];
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                Vector3 p = mesh.vertices[i];
                check(Finite(p.x) && Finite(p.y) && Finite(p.z), "production " + asset + " model vertices remain finite: " + mesh.name);
                check(Math.Abs(p.x) < envelope && Math.Abs(p.y) < envelope && Math.Abs(p.z) < envelope,
                    "production " + asset + " model stays within its declared physical envelope: " + mesh.name);
                string key = WeldKey(p);
                int id;
                if (!weld.TryGetValue(key, out id)) { id = weld.Count; weld.Add(key, id); }
                ids[i] = id;
            }
            Dictionary<string, Edge> edges = new Dictionary<string, Edge>();
            double[] triangleVolumes = new double[mesh.triangles.Length / 3];
            for (int i = 0; i < mesh.triangles.Length; i += 3)
            {
                int a = mesh.triangles[i], b = mesh.triangles[i + 1], c = mesh.triangles[i + 2];
                check(a >= 0 && b >= 0 && c >= 0 && a < ids.Length && b < ids.Length && c < ids.Length,
                    "production " + asset + " model triangle indices remain inside its vertex buffer: " + mesh.name);
                Vector3 pa = mesh.vertices[a], pb = mesh.vertices[b], pc = mesh.vertices[c];
                Vector3 normal = Cross(pb - pa, pc - pa);
                check(normal.sqrMagnitude > 1e-16f, "production " + asset + " model has no collapsed visible triangle: " + mesh.name);
                AddEdge(edges, ids[a], ids[b], i / 3); AddEdge(edges, ids[b], ids[c], i / 3); AddEdge(edges, ids[c], ids[a], i / 3);
                triangleVolumes[i / 3] = Dot(pa, Cross(pb, pc)) / 6d;
            }
            check(edges.Values.All(e => e.Count == 2 && e.Direction == 0),
                "production " + asset + " model is closed with paired opposite edge winding at its seams: " + mesh.name);
            List<int>[] neighbors = new List<int>[triangleVolumes.Length];
            for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new List<int>();
            foreach (Edge edge in edges.Values)
            { int a = edge.Triangles[0], b = edge.Triangles[1]; neighbors[a].Add(b); neighbors[b].Add(a); }
            bool[] visited = new bool[neighbors.Length];
            for (int i = 0; i < visited.Length; i++)
            {
                if (visited[i]) continue;
                double volume = 0d;
                Queue<int> pending = new Queue<int>(); pending.Enqueue(i); visited[i] = true;
                while (pending.Count > 0)
                {
                    int triangle = pending.Dequeue(); volume += triangleVolumes[triangle];
                    foreach (int neighbor in neighbors[triangle])
                        if (!visited[neighbor]) { visited[neighbor] = true; pending.Enqueue(neighbor); }
                }
                check(volume > 1e-10d,
                    "production " + asset + " model front faces enclose positive physical volume in every component: " + mesh.name);
            }
            UnityEngine.Object.Destroy(mesh);
        }
    }

    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static string WeldKey(Vector3 point)
    {
        return Math.Round(point.x * 100000f) + ":" + Math.Round(point.y * 100000f) + ":" + Math.Round(point.z * 100000f);
    }
    private static void AddEdge(Dictionary<string, Edge> edges, int a, int b, int triangle)
    {
        string key = Math.Min(a, b) + ":" + Math.Max(a, b);
        Edge edge;
        if (!edges.TryGetValue(key, out edge)) { edge = new Edge(); edges.Add(key, edge); }
        edge.Count++; edge.Direction += a < b ? 1 : -1;
        edge.Triangles.Add(triangle);
    }
    private static Vector3 Cross(Vector3 a, Vector3 b)
    { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
    private static double Dot(Vector3 a, Vector3 b) { return (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z; }
}
