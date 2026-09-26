using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    public static class UniTask
    {
        public static readonly List<int> Delays = new List<int>();
        public static readonly Queue<TaskCompletionSource<bool>> Pending = new Queue<TaskCompletionSource<bool>>();
        public static Task Delay(int milliseconds)
        {
            Delays.Add(milliseconds);
            var source = new TaskCompletionSource<bool>();
            Pending.Enqueue(source);
            return source.Task;
        }
        public static void Advance() { Pending.Dequeue().SetResult(true); }
        public static void Reset() { if (Pending.Count != 0) throw new Exception("unfinished delay"); Delays.Clear(); }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            go.Destroyed = true;
            foreach (Object component in go.Components) component.Destroyed = true;
        }
    }
    public sealed class GameObject : Object { public readonly List<Object> Components = new List<Object>(); }
    public sealed class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static float SqrMagnitude(Vector3 v) { return v.sqrMagnitude; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } }
    public static class Random
    {
        public static readonly List<string> Calls = new List<string>();
        public static int NextInt;
        public static float Fraction;
        public static int Range(int a,int b) { Calls.Add("int:"+a+":"+b); return Math.Min(b-1,Math.Max(a,NextInt)); }
        public static float Range(float a,float b) { Calls.Add("float:"+a+":"+b); return a+(b-a)*Fraction; }
        public static void Reset() { Calls.Clear(); NextInt=0; Fraction=0; }
    }
}
namespace BossRush
{
    using UnityEngine;
    public enum Teams { player, wolf, bear, scav, usec }
    public sealed class EnemyPresetInfo { public string name, displayName; public int team; public float baseHealth; }
    public sealed class CharacterMainControl : Object
    {
        public static CharacterMainControl Main;
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
        public CharacterMainControl() { gameObject.Components.Add(this); }
    }
    public sealed class CharacterRandomPreset { }
    public static class ModBehaviour
    {
        public static bool VerboseStartupDebugLogsEnabled;
        public static readonly List<string> Logs = new List<string>();
        public static void DevLog(string message) { Logs.Add(message); }
    }
    public sealed class EnemySpawnContext { public EnemyPresetInfo preset; }
    internal sealed class ModeEFSpawnPreparation
    {
        internal Dictionary<Teams,List<Vector3>> SpawnAllocation = new Dictionary<Teams,List<Vector3>>();
        internal Vector3[] Flattened = new Vector3[0];
        internal Vector3[] GetModeEFlattenedSpawnPoints() { return Flattened; }
    }
    internal sealed class SpawnRequest
    {
        internal EnemyPresetInfo Preset;
        internal Vector3 Position;
        internal bool Boss, SkipDragon, SkipKing, Deferred, Equipment, Multiplier, Normalize, SkipLoot;
        internal int Wave;
        internal Func<bool> Active;
        internal Action Failed;
        internal Func<EnemySpawnContext,bool> Commit;
    }
    internal sealed class EnemySpawnRuntime
    {
        internal readonly List<SpawnRequest> Requests = new List<SpawnRequest>();
        internal bool AutoCommit = true, Fail, Throw;
        internal EnemyPresetInfo ActualPreset;
        internal void SpawnEnemyCore(EnemyPresetInfo preset, Vector3 position, bool isBoss,
            Func<bool> isActiveCheck, Action<EnemySpawnContext> onSpawned, Action onFailed=null,
            int waveIndex=1, bool skipDragonDescendant=false, bool skipDragonKing=false,
            bool applyEquipment=true, bool applyBossMultiplier=true, CharacterRandomPreset directPreset=null,
            bool skipBossRushLootTracking=false, bool normalizeDamageMultiplier=true,
            bool deferActivationUntilNextFrame=false, Func<EnemySpawnContext,bool> onCommit=null)
        {
            if (Throw) throw new Exception("factory boundary");
            Requests.Add(new SpawnRequest { Preset=preset,Position=position,Boss=isBoss,Active=isActiveCheck,
                Failed=onFailed,Commit=onCommit,Wave=waveIndex,SkipDragon=skipDragonDescendant,SkipKing=skipDragonKing,
                Deferred=deferActivationUntilNextFrame,Equipment=applyEquipment,Multiplier=applyBossMultiplier,
                Normalize=normalizeDamageMultiplier,SkipLoot=skipBossRushLootTracking });
            if (Fail) onFailed();
            else if (AutoCommit) onCommit(new EnemySpawnContext { preset=ActualPreset ?? preset });
        }
    }
    public static class SpawnPositionHelper
    {
        public const float DefaultSafeDistance=50f;
        public static readonly List<string> Calls = new List<string>();
        public static Vector3 FindNearestSafeSpawnPoint(IList<Vector3> points,Vector3 player)
        { Calls.Add("safe:"+points.Count+":"+player.x); return points.Count>0 ? points[points.Count-1] : Vector3.zero; }
        public static Vector3 SnapToGround(Vector3 point) { Calls.Add("ground:"+point.x); return point; }
    }
}
