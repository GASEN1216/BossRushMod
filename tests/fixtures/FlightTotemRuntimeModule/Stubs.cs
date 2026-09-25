using System;
using System.Collections;
using System.Collections.Generic;

internal static class TestTrace
{
    internal static readonly List<string> Events = new List<string>();
    internal static void Reset()
    {
        Events.Clear();
        BossRush.EquipmentFactory.LoadCalls = 0;
        BossRush.EquipmentFactory.LoadResult = 1;
        BossRush.EquipmentFactory.FailLoad = false;
        ItemStatsSystem.ItemAssetsCollection.InstantiateCalls = 0;
        ItemStatsSystem.ItemAssetsCollection.Responses.Clear();
        BossRush.LocalizationHelper.Values.Clear();
        BossRush.L10n.English = false;
        BossRush.FlightAbilityManager.Instance = null;
        BossRush.FlightTotemEffectManager.Instance = null;
    }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object obj) { return this == obj as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object target)
        {
            if (ReferenceEquals(target, null)) return;
            target.Destroyed = true;
            var gameObject = target as GameObject;
            if (!ReferenceEquals(gameObject, null))
            {
                TestTrace.Events.Add("destroy:" + gameObject.Name);
                foreach (Object component in gameObject.Components) component.Destroyed = true;
            }
        }
    }
    public sealed class GameObject : Object
    {
        internal readonly string Name;
        internal readonly List<Object> Components = new List<Object>();
        internal GameObject(string name) { Name = name; }
    }
    public class MonoBehaviour : Object
    {
        public readonly GameObject gameObject;
        internal readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
        public MonoBehaviour()
        {
            gameObject = new GameObject(GetType().Name);
            gameObject.Components.Add(this);
        }
        public object StartCoroutine(IEnumerator routine)
        {
            TestTrace.Events.Add("coroutine.start");
            Coroutines.Add(routine);
            return routine;
        }
    }
    public sealed class WaitForSeconds
    {
        internal readonly float Seconds;
        public WaitForSeconds(float seconds) { Seconds = seconds; }
    }
    public static class Mathf { public static float Abs(float value) { return Math.Abs(value); } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name;
        public Scene(string value) { name = value; }
    }
}

namespace ItemStatsSystem.Stats { }
namespace ItemStatsSystem
{
    public sealed class Item : UnityEngine.Object
    {
        public readonly int TypeID;
        public readonly VariableCollection Variables = new VariableCollection();
        public Item(int typeId) { TypeID = typeId; }
    }
    public sealed class VariableCollection
    {
        internal readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        internal readonly Dictionary<string, bool> Displays = new Dictionary<string, bool>();
        public void Set(string key, float value)
        {
            TestTrace.Events.Add("set:" + key);
            Values[key] = value;
        }
        public void SetDisplay(string key, bool visible)
        {
            TestTrace.Events.Add("display:" + key);
            Displays[key] = visible;
        }
    }
    public static class ItemAssetsCollection
    {
        internal static int InstantiateCalls;
        internal static readonly Queue<Func<Item>> Responses = new Queue<Func<Item>>();
        public static Item InstantiateSync(int typeId)
        {
            InstantiateCalls++;
            TestTrace.Events.Add("item:" + typeId);
            if (Responses.Count == 0) throw new InvalidOperationException("Test did not provide an item response");
            return Responses.Dequeue()();
        }
    }
}

namespace BossRush
{
    public sealed class ModBehaviour : UnityEngine.MonoBehaviour
    {
        internal static readonly UnityEngine.WaitForSeconds FlightTotemSharedWait05sForRuntime = new UnityEngine.WaitForSeconds(0.5f);
        public static bool IsGameplaySceneName(string name) { return name == "Gameplay"; }
        public static void DevLog(string message) { }
    }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    internal static class EquipmentFactory
    {
        internal static int LoadCalls;
        internal static int LoadResult;
        internal static bool FailLoad;
        internal static int LoadBundle(string name)
        {
            LoadCalls++;
            TestTrace.Events.Add("bundle:" + name);
            if (FailLoad) throw new InvalidOperationException("bundle unavailable");
            return LoadResult;
        }
    }
    internal static class LocalizationHelper
    {
        internal static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        internal static void InjectLocalization(string key, string value)
        {
            TestTrace.Events.Add("localize:" + key);
            Values[key] = value;
        }
    }
    internal static class L10n
    {
        internal static bool English;
        internal static string T(string chinese, string english) { return English ? english : chinese; }
    }
    internal sealed class FlightAbilityManager : UnityEngine.MonoBehaviour
    {
        internal static FlightAbilityManager Instance;
        internal static void EnsureInstance()
        {
            TestTrace.Events.Add("ability.ensure");
            if (Instance == null) Instance = new FlightAbilityManager();
        }
        internal void OnSceneChanged() { TestTrace.Events.Add("ability.scene"); }
        internal static void Cleanup()
        {
            TestTrace.Events.Add("ability.cleanup");
            if (Instance != null) UnityEngine.Object.Destroy(Instance.gameObject);
            Instance = null;
        }
    }
    internal sealed class FlightTotemEffectManager : UnityEngine.MonoBehaviour
    {
        internal static FlightTotemEffectManager Instance;
        internal static void EnsureInstance()
        {
            TestTrace.Events.Add("effect.ensure");
            if (Instance == null) Instance = new FlightTotemEffectManager();
        }
        internal void CheckCurrentEquipment() { TestTrace.Events.Add("equipment.check"); }
    }
}
