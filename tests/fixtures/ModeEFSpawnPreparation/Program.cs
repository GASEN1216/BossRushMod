using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static float SqrMagnitude(Vector3 value) { return value.sqrMagnitude; }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object other) { return other is Vector3 && this == (Vector3)other; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    }
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) { return x == other.x && y == other.y; }
        public override bool Equals(object other) { return other is Vector2Int && Equals((Vector2Int)other); }
        public override int GetHashCode() { return x * 397 ^ y; }
    }
    public static class Mathf
    {
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
    }
    public sealed class Transform : Object { public Vector3 position; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static string Name;
        public static Scene GetActiveScene() { return new Scene { name = Name }; }
    }
}
namespace UnityEngine.AI
{
    public struct NavMeshHit { public UnityEngine.Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas = -1;
        public static bool Succeeds;
        public static bool SamplePosition(UnityEngine.Vector3 point, out NavMeshHit hit, float distance, int mask)
        { hit = new NavMeshHit { position = point + new UnityEngine.Vector3(0, 1, 0) }; return Succeeds; }
    }
}
namespace BossRush
{
    using UnityEngine;
    using UnityEngine.SceneManagement;
    internal enum Teams { player, first, second }
    internal sealed class CharacterMainControl : UnityEngine.Object
    { public static CharacterMainControl Main; public Transform transform; }
    internal sealed class BossRushMapConfig
    { public Vector3[] modeESpawnPoints; public Vector3? modeEPlayerSpawnPos; }
    internal static class ModBehaviour
    {
        internal static bool VerboseStartupDebugLogsEnabled;
        internal static BossRushMapConfig Map;
        internal static BossRushMapConfig GetCurrentMapConfig() { return Map; }
        internal static void DevLog(string text) { }
    }
    internal sealed class Points : UnityEngine.Object
    {
        internal List<Vector3> points;
        internal Vector3 Offset;
        internal Vector3 GetPoint(int index) { return points[index] + Offset; }
    }
    internal sealed class CharacterSpawnerRoot : UnityEngine.Object
    {
        internal Transform transform;
        internal Points Points;
        internal T GetComponentInChildren<T>() where T : class { return Points as T; }
    }
    internal static class ObjectCache
    {
        internal static CharacterSpawnerRoot[] Roots;
        internal static int Reads;
        internal static CharacterSpawnerRoot[] GetCharacterSpawnerRoots() { Reads++; return Roots; }
    }
    internal static class L10n { internal static string T(string zh, string en) { return en; } }
    internal static class Program
    {
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
        private static Vector3 V(float x) { return new Vector3(x, 0, 0); }
        private static CharacterSpawnerRoot Root(float x)
        { return new CharacterSpawnerRoot { transform = new Transform { position = V(x) } }; }
        private static void Main()
        {
            CharacterMainControl.Main = new CharacterMainControl { transform = new Transform() };
            SceneManager.Name = "first";
            Teams faction = Teams.second;
            int fallbacks = 0, messages = 0;
            Vector3 notifiedPosition = Vector3.zero;
            Func<Vector3, Vector3[]> fallback = p => { fallbacks++; return new[] { V(10), V(20), V(30) }; };
            Action<string> notify = text => { messages++; notifiedPosition = CharacterMainControl.Main.transform.position; };
            var service = new ModeEFSpawnPreparation(new[] { Teams.first, Teams.second }, () => faction, fallback, notify);
            var actual = Root(999);
            actual.Points = new Points { Offset = V(6), points = new List<Vector3> { V(4), V(14), V(-6) } };
            var destroyed = Root(888); destroyed.Destroyed = true;
            ObjectCache.Roots = new[] { actual, Root(35), destroyed };
            service.PreCacheMapSpawnerPositions();
            var original = service.CachedSpawnerPositions;
            Check(original.Length == 3 && original[0].x == 10 && original[1].x == 20 && original[2].x == 35,
                "scan uses world Points, excludes zero/destroyed roots, and keeps root fallback");
            ObjectCache.Roots = new[] { Root(100) };
            service.PreCacheMapSpawnerPositions();
            Check(ObjectCache.Reads == 1 && ReferenceEquals(original, service.CachedSpawnerPositions),
                "same scene preserves pre-teardown cache without rescanning");
            SceneManager.Name = "second";
            service.PreCacheMapSpawnerPositions();
            Check(ObjectCache.Reads == 2 && service.CachedSpawnerPositions[0].x == 100 && service.CachedSpawnerSceneName == "second",
                "new scene replaces the prior scene cache");
            ModBehaviour.Map = new BossRushMapConfig { modeESpawnPoints = new[] { V(30), V(0), V(5), V(10), V(20) } };
            service.AllocateSpawnPoints();
            Check(service.SpawnAllocation[Teams.second].Count == 2 && service.SpawnAllocation[Teams.second][0].x == 0
                && service.SpawnAllocation[Teams.second][1].x == 20 && service.SpawnAllocation[Teams.first][0].x == 10,
                "configured points retain 10m boundary and nearest-first player faction priority");
            Check(ObjectCache.Reads == 2 && fallbacks == 0, "configured points avoid scan and fallback");
            faction = Teams.player;
            service.AllocateSpawnPoints();
            Check(service.SpawnAllocation[Teams.first][0].x == 0 && service.SpawnAllocation[Teams.second][0].x == 10,
                "lone player uses original NPC faction order");
            var independent = new ModeEFSpawnPreparation(new[] { Teams.first, Teams.second }, () => faction, fallback, notify);
            independent.AllocateSpawnPoints();
            var independentPoints = independent.GetModeEFlattenedSpawnPoints();
            service.Reset(true, false);
            Check(service.SpawnAllocation == null && service.GetModeEFlattenedSpawnPoints().Length == 0
                && service.CachedSpawnerPositions[0].x == 100 && ReferenceEquals(independentPoints, independent.GetModeEFlattenedSpawnPoints()),
                "allocation reset clears flattened cache while retaining scene cache and other owner");
            service.AllocateSpawnPoints();
            service.TeleportPlayerToSafePosition();
            Check(CharacterMainControl.Main.transform.position.x == 100 && notifiedPosition.x == 100 && messages == 1,
                "fallback teleport chooses point farthest from boss positions before notification");
            ModBehaviour.Map.modeEPlayerSpawnPos = new Vector3(7, 0, 3);
            UnityEngine.AI.NavMesh.Succeeds = true;
            service.TeleportPlayerToSafePosition();
            Check(notifiedPosition == new Vector3(7, 1, 3), "configured teleport accepts NavMesh correction before notification");
            service.Reset(false, true);
            Check(service.SpawnAllocation != null && service.CachedSpawnerPositions == null && service.CachedSpawnerSceneName == null,
                "scene-only reset retains allocations");
            ModBehaviour.Map = null;
            ObjectCache.Roots = new CharacterSpawnerRoot[0];
            service.AllocateSpawnPoints();
            Check(fallbacks == 1 && service.GetModeEFlattenedSpawnPoints().Length == 3, "missing map/spawners request player fallback once");
            CharacterMainControl.Main = null;
            service.TeleportPlayerToSafePosition();
            Check(messages == 2, "missing player does not report a teleport");
        }
    }
}
