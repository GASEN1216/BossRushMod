using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b)
        { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
    }
}

namespace BossRush
{
    internal static partial class SpawnPositionHelper
    {
        internal static UnityEngine.Vector3 SnapToGround(UnityEngine.Vector3 point) { return point; }
    }

    internal static class Program
    {
        private static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }

        private static void Main()
        {
            var player = new UnityEngine.Vector3(0, 0, 0);
            var points = new[]
            {
                new UnityEngine.Vector3(0, 0, 1),
                new UnityEngine.Vector3(16, 0, 0),
                new UnityEngine.Vector3(20, 0, 0),
                new UnityEngine.Vector3(-17, 0, 0)
            };
            var first = SpawnPositionHelper.FindMultipleSafeSpawnPoints(2, points, player, 15f);
            Check(first.Count == 2 && first[0].x == 16 && first[1].x == -17,
                "safe points retain nearest-first order");
            var all = SpawnPositionHelper.FindMultipleSafeSpawnPoints(4, points, player, 15f);
            Check(all.Count == 4 && all[0].x == 16 && all[1].x == -17
                && all[2].x == 20 && all[3].z == 1,
                "fallback fills from unused points without duplicates");
            var list = SpawnPositionHelper.FindMultipleSafeSpawnPoints(4,
                new List<UnityEngine.Vector3>(points), player, 15f);
            Check(list.Count == all.Count && list[0].x == all[0].x
                && list[1].x == all[1].x && list[2].x == all[2].x
                && list[3].z == all[3].z, "list and array overloads agree");
            var near = new[] {
                new UnityEngine.Vector3(1, 0, 0),
                new UnityEngine.Vector3(5, 0, 0),
                new UnityEngine.Vector3(10, 0, 0)
            };
            var fallback = SpawnPositionHelper.FindMultipleSafeSpawnPoints(2, near, player, 15f);
            Check(fallback.Count == 2 && fallback[0].x == 10 && fallback[1].x == 5,
                "all-near fallback picks farthest first");
            Check(SpawnPositionHelper.FindMultipleSafeSpawnPoints(3,
                new UnityEngine.Vector3[0], player, 15f).Count == 0,
                "empty candidate set stays empty");
            var vertical = SpawnPositionHelper.FindMultipleSafeSpawnPoints(1,
                new[] { new UnityEngine.Vector3(0, 100, 0) }, player, 15f);
            Check(vertical.Count == 1 && vertical[0].y == 100,
                "multi-point safety keeps original three-dimensional distance");
        }
    }
}
