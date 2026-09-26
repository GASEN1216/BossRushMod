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
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            go.Destroyed = true;
            foreach (var component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public readonly Transform transform = new Transform();
        internal readonly List<MonoBehaviour> Components = new List<MonoBehaviour>();
        public void Attach(MonoBehaviour component) { component.gameObject = this; Components.Add(component); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
    }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object value) { return value is Vector3 && this == (Vector3)value; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public override string ToString() { return x + "," + y + "," + z; }
    }
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
namespace BossRush
{
    using UnityEngine;
    internal static class Probe
    {
        internal static readonly List<string> Events = new List<string>();
        internal static void Record(string value) { Events.Add(value); }
    }
    public class ModBehaviour
    {
        public readonly List<string> Messages = new List<string>();
        public static void DevLog(string message) { }
        public static void CriticalLog(string id, string message) { throw new Exception(id + ": " + message); }
        public void ShowMessage(string message) { Messages.Add(message); Probe.Record("message"); }
    }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public class CharacterMainControl : MonoBehaviour
    {
        public static CharacterMainControl Current;
        public static bool ThrowMain;
        public static CharacterMainControl Main
        {
            get { if (ThrowMain) throw new InvalidOperationException("main unavailable"); return Current; }
        }
        public bool ThrowSetPosition;
        public int SetPositionCalls;
        public CharacterMainControl() { new GameObject().Attach(this); }
        public void SetPosition(Vector3 position)
        {
            SetPositionCalls++;
            Probe.Record("position");
            if (ThrowSetPosition) throw new InvalidOperationException("set position failed");
            transform.position = position;
        }
    }
    public class BossRushSignInteractable : MonoBehaviour
    {
        public int AmmoOptions;
        public bool ThrowOnRefill;
        public void AddAmmoRefillOption()
        {
            AmmoOptions++;
            Probe.Record("ammo");
            if (ThrowOnRefill) throw new InvalidOperationException("sign unavailable");
        }
    }
    internal static class DragonBreathBuffHandler { internal static void Subscribe() { Probe.Record("dragon"); } }
    internal static class MutatorManager { internal static void RemoveAll() { Probe.Record("mutators"); } }
    internal static class MutatorUI { internal static void HideAll() { Probe.Record("ui"); } }
    internal sealed partial class WavesArenaRuntimeModule
    {
        // Other production partials supply these slots. Values are set by each test,
        // while HostState owns all tested decisions and private bound delegates.
        private ModBehaviour owner;
        internal int BossesPerWave { get; set; }
        internal bool InfiniteHellMode { get; set; }
        internal int InfiniteHellWaveIndex { get; set; }
        internal long InfiniteHellCashPool { get; set; }
        internal int InfiniteHellMilestoneRewardTier { get; set; }
        internal Vector3 DemoChallengeStartPosition { get; set; }
        internal WavesArenaRuntimeModule(ModBehaviour value) { owner = value; }
        private void ClearCashMagnetState() { Probe.Record("cash"); }
    }
    internal sealed partial class CommonNpcRuntimeModule { }
    internal static class NPCSpawnConfig
    {
        internal static readonly Dictionary<string, Vector3[]> Points = new Dictionary<string, Vector3[]>();
        internal static readonly List<string> Calls = new List<string>();
        internal static Vector3[] GetCourierNormalModeSpawnPoints(string sceneName)
        {
            Calls.Add(sceneName);
            Vector3[] result;
            return sceneName != null && Points.TryGetValue(sceneName, out result) ? result : null;
        }
    }
}
