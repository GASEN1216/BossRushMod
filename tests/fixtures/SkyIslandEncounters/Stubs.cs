using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        internal bool Destroyed;
        internal virtual bool IsDestroyed { get { return Destroyed; } }
        internal static readonly List<Object> Delayed = new List<Object>();
        public static T Instantiate<T>(T source) where T : Object { return (T)(Object)((CharacterRandomPreset)(Object)source).Copy(); }
        public static void Destroy(Object value, float delay = 0)
        {
            if (ReferenceEquals(value, null) || value.IsDestroyed) return;
            if (delay > 0) { Delayed.Add(value); return; }
            var go = value as GameObject;
            if (go != null)
                foreach (Component component in go.Components.ToArray())
                {
                    MethodInfo method = component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null) method.Invoke(component, null);
                }
            value.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        {
            bool leftNull = ReferenceEquals(a, null) || a.IsDestroyed, rightNull = ReferenceEquals(b, null) || b.IsDestroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        internal override bool IsDestroyed { get { return Destroyed || (!ReferenceEquals(gameObject, null) && gameObject.Destroyed); } }
        public T[] GetComponentsInChildren<T>(bool include) where T : Component { return gameObject.GetComponentsInChildren<T>(include); }
        public T GetComponentInChildren<T>() where T : Component { return gameObject.GetComponentInChildren<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public bool activeSelf = true;
        public GameObject(string name = "object") { this.name = name; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var value = new T { gameObject = this }; Components.Add(value); return value; }
        public void SetActive(bool value) { activeSelf = value; }
        public T[] GetComponentsInChildren<T>(bool include) where T : Component
        {
            var result = new List<T>(); foreach (Component c in Components) if (c is T) result.Add((T)c); return result.ToArray();
        }
        public T GetComponentInChildren<T>() where T : Component { foreach (Component c in Components) if (c is T) return (T)c; return null; }
    }
    public class Transform : Component
    {
        public Vector3 position;
        private Transform parent;
        private readonly List<Transform> children = new List<Transform>();
        public void SetParent(Transform next, bool keep)
        { if (parent != null) parent.children.Remove(this); parent = next; if (parent != null) parent.children.Add(this); }
        public Transform Find(string value) { foreach (Transform child in children) if (child.gameObject.name == value) return child; return null; }
        public bool IsChildOf(Transform value) { return this == value || (parent != null && parent.IsChildOf(value)); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 down { get { return new Vector3(0,-1,0); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a-b).sqrMagnitude); }
    }
    public static class Mathf { public const float Deg2Rad = 0.0174532925f; public static float Cos(float a) { return (float)Math.Cos(a); } public static float Sin(float a) { return (float)Math.Sin(a); } }
    public struct RaycastHit { public Vector3 point; public Transform transform; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        internal static Transform Ground;
        public static bool Raycast(Vector3 p, Vector3 d, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction ignore)
        { hit = new RaycastHit { point = p + Vector3.down * 2, transform = Ground }; return true; }
        public static bool CheckCapsule(Vector3 a, Vector3 b, float radius, int mask, QueryTriggerInteraction ignore) { return false; }
    }
    public static class Time { public static float time; }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() { return (T[])(object)new[] { CharacterRandomPreset.Source }; } }
    public static class Debug { public static void LogWarning(string value) { } }
}
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask = 2; } } }
namespace Pathfinding
{
    public struct GraphMask { }
    public class Seeker : UnityEngine.Component { public GraphMask graphMask; public void CancelCurrentPathRequest() { } }
}
public enum Teams { scav, wolf }
public class AICharacterController : UnityEngine.Component { public float forceTracePlayerDistance; }
public class DamageInfo { }
public class DeathEvent
{
    private event Action<DamageInfo> handlers;
    public void AddListener(Action<DamageInfo> handler) { handlers += handler; }
    public void RemoveListener(Action<DamageInfo> handler) { handlers -= handler; }
    internal void Invoke() { if (handlers != null) handlers(new DamageInfo()); }
}
public class Health : UnityEngine.Object
{
    public bool IsDead;
    public DeathEvent OnDeadEvent = new DeathEvent();
    internal void Die() { IsDead = true; OnDeadEvent.Invoke(); }
}
public class CharacterMainControl : UnityEngine.Component
{
    public Health Health;
    public Teams Team;
    public void SetTeam(Teams value) { Team = value; }
    internal static CharacterMainControl Create(bool healthy = true)
    {
        var go = new UnityEngine.GameObject("enemy"); var character = go.AddComponent<CharacterMainControl>();
        character.Health = healthy ? new Health() : null;
        go.AddComponent<Pathfinding.Seeker>(); go.AddComponent<AICharacterController>(); return character;
    }
}
public class CharacterRandomPreset : UnityEngine.Object
{
    public bool isBoss, isZombie, dropBoxOnDead, setActiveByPlayerDistance;
    public Teams team;
    internal static CharacterRandomPreset Source = new CharacterRandomPreset { name="Scav", team=Teams.scav };
    internal static readonly List<CharacterRandomPreset> Clones = new List<CharacterRandomPreset>();
    internal static readonly List<CharacterMainControl> Created = new List<CharacterMainControl>();
    internal static TaskCompletionSource<CharacterMainControl> Block;
    internal static bool MissingHealth;
    internal CharacterRandomPreset Copy()
    { var clone = (CharacterRandomPreset)MemberwiseClone(); Clones.Add(clone); return clone; }
    public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 point, UnityEngine.Vector3 direction, int scene, object option, bool variable)
    {
        if (Block != null) return Block.Task;
        CharacterMainControl character = CharacterMainControl.Create(!MissingHealth); character.transform.position = point;
        Created.Add(character); return Task.FromResult(character);
    }
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        internal static string GetModPath() { return Environment.CurrentDirectory; }
        internal static void DevLog(string value) { }
        internal static void CriticalLog(string key, string value) { }
    }
    internal static class SkyIslandResidents
    {
        internal static readonly List<string> Faces = new List<string>();
        internal static void ApplyBattleFace(CharacterMainControl actor, string id) { Faces.Add(id); }
    }
    internal static class SpawnedEnemyActivationHelper { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl character) { } }
    // 清场播报走 L10n.T（英文玩家不该看到中文提示）。L10n 依赖官方 LocalizationManager，
    // 隔离进程里返回中文分支即可：断言只看清场记账与生成顺序，不看文案。
    internal static class L10n { internal static bool IsChinese = true; internal static string T(string zh, string en) { return IsChinese ? zh : en; } }

    /// 档次装饰的替身：只记录「谁被判成了哪一档」，让夹具能断言逐位分配。
    internal static class SkyIslandEnemyTiers
    {
        internal static readonly List<SkyIslandEnemyTier> Applied = new List<SkyIslandEnemyTier>();
        internal static readonly List<string> Champions = new List<string>();
        internal static readonly List<SkyIslandEnemyTier> AiTiers = new List<SkyIslandEnemyTier>();
        internal static void Apply(CharacterMainControl character, SkyIslandEnemyTier tier) { Applied.Add(tier); }
        internal static void ApplyAi(AICharacterController ai, SkyIslandEnemyTier tier) { AiTiers.Add(tier); }
        internal static void ApplyStoryChampion(CharacterMainControl character, string id, string cn, string en)
        { Champions.Add(id); }
        internal static void Reset() { Applied.Clear(); Champions.Clear(); AiTiers.Clear(); }
    }

    internal sealed class SkyIslandStormBoss : UnityEngine.MonoBehaviour
    {
        internal static int Bound;
        internal void Bind(CharacterMainControl character, Func<bool> valid,
            Action<string, bool> report, Action defeated) { Bound++; }
    }
}
