using System;
using System.Collections;
using System.Collections.Generic;

internal static class Trace
{
    internal static readonly List<string> Calls = new List<string>();
    internal static void Add(string value) { Calls.Add(value); }
    internal static int Count(string value) { return Calls.FindAll(entry => entry == value).Count; }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool l = ReferenceEquals(left, null) || left.Destroyed;
            bool r = ReferenceEquals(right, null) || right.Destroyed;
            return l || r ? l == r : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                Trace.Add("destroy:" + go.Name);
                foreach (var component in go.Components) component.Destroyed = true;
            }
        }
        public static void DontDestroyOnLoad(Object value) { Trace.Add("persistent:" + ((GameObject)value).Name); }
    }
    public sealed class GameObject : Object
    {
        internal readonly string Name;
        internal readonly List<Object> Components = new List<Object>();
        public GameObject(string name) { Name = name; }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            T result = new T();
            result.gameObject = this;
            Components.Add(result);
            Trace.Add("create:" + typeof(T).Name);
            return result;
        }
    }
    public sealed class Coroutine : IEnumerator
    {
        internal readonly IEnumerator Routine;
        internal bool Stopped;
        internal Coroutine(IEnumerator routine) { Routine = routine; }
        public object Current { get { return Routine.Current; } }
        public bool MoveNext() { return !Stopped && Routine.MoveNext(); }
        public void Reset() { throw new NotSupportedException(); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        internal readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
        public MonoBehaviour()
        {
            gameObject = new GameObject(GetType().Name);
            gameObject.Components.Add(this);
        }
        public Coroutine StartCoroutine(IEnumerator value) { var handle = new Coroutine(value); Coroutines.Add(handle); Trace.Add("schedule"); return handle; }
        public void StopCoroutine(Coroutine handle) { handle.Stopped = true; Trace.Add("coroutine.stop"); }
    }
    public sealed class WaitForSeconds
    {
        internal readonly float Seconds;
        public WaitForSeconds(float seconds) { Seconds = seconds; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; public Scene(string value) { name = value; } }
}
namespace ItemStatsSystem
{
    public sealed class Item : UnityEngine.Object
    {
        public string DisplayNameRaw;
        public readonly Variables Variables = new Variables();
    }
    public sealed class Variables
    {
        internal readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        internal readonly Dictionary<string, bool> Displays = new Dictionary<string, bool>();
        public void Set(string key, float value) { Values[key] = value; }
        public void SetDisplay(string key, bool value) { Displays[key] = value; }
    }
}
public sealed class CharacterMainControl : UnityEngine.Object { public static CharacterMainControl Main; }

namespace BossRush
{
    public partial class ModBehaviour : UnityEngine.MonoBehaviour
    {
        private static readonly UnityEngine.WaitForSeconds sharedWait05s = new UnityEngine.WaitForSeconds(0.5f);
        internal readonly ReverseScaleRuntimeModule reverseScaleRuntime = new ReverseScaleRuntimeModule();
        internal readonly PhantomWitchRuntimeModule phantomWitchRuntimeModule = new PhantomWitchRuntimeModule();
        internal readonly DragonKingRuntimeModule dragonKingRuntimeModule = new DragonKingRuntimeModule();
        public ModBehaviour()
        {
            reverseScaleRuntime.OnAwake(this);
            phantomWitchRuntimeModule.Bind(this);
            dragonKingRuntimeModule.Bind(this);
        }
        public static bool IsGameplaySceneName(string name) { return name == "Gameplay"; }
        public static void DevLog(string message) { }
        internal void InitReverse() { InitializeReverseScaleSystem(); }
        internal void InitScythe() { InitializePhantomWitchScytheSystem(); }
        internal void InitHalberd() { InitializeFenHuangHalberdSystem(); }
        internal void SceneReverse(string name) { SetupReverseScaleForScene(new UnityEngine.SceneManagement.Scene(name)); }
        internal void SceneScythe(string name) { SetupPhantomWitchScytheForScene(new UnityEngine.SceneManagement.Scene(name)); }
        internal void SceneHalberd(string name) { SetupFenHuangHalberdForScene(new UnityEngine.SceneManagement.Scene(name)); }
        internal void CleanupReverse() { CleanupReverseScaleSystem(); }
        internal void CleanupScythe() { CleanupPhantomWitchScytheSystem(); }
        internal void CleanupHalberd() { CleanupFenHuangHalberdSystem(); }
    }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    internal sealed partial class PhantomWitchRuntimeModule
    {
        private ModBehaviour owner;
        internal void Bind(ModBehaviour value) { owner = value; }
        private static void DevLog(string message) { }
    }
    internal sealed partial class DragonKingRuntimeModule
    {
        private ModBehaviour owner;
        internal void Bind(ModBehaviour value) { owner = value; }
        private static void DevLog(string message) { }
    }
    internal static class EquipmentFactory
    {
        internal static Action<ItemStatsSystem.Item, string> Configurator;
        internal static void RegisterConfigurator(string key, Action<ItemStatsSystem.Item, string> value) { Configurator = value; }
    }
    internal static class EquipmentHelper { internal static void AddTagToItem(ItemStatsSystem.Item item, string tag) { } }
    internal static class DragonKingConfig
    {
        internal const float PrismaticBoltScale = 1f, PrismaticBoltSpeed = 1f, PrismaticBoltDamage = 1f,
            PrismaticBoltLifetime = 1f, ProjectileHitRadius = 1f, PrismaticBoltTrackingStrength = 1f, PrismaticBoltTrackingDuration = 1f;
    }
    internal static class LocalizationHelper
    {
        internal static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        internal static void InjectLocalization(string key, string value) { Values[key] = value; Trace.Add("localization"); }
    }
    internal static class L10n
    {
        internal static bool English;
        internal static string T(string chinese, string english) { return English ? english : chinese; }
    }
    internal sealed class ReverseScaleAbilityManager : UnityEngine.MonoBehaviour
    {
        internal static ReverseScaleAbilityManager Instance;
        internal static void EnsureInstance() { Trace.Add("reverse.ability"); if (Instance == null) Instance = new ReverseScaleAbilityManager(); }
        internal void OnSceneChanged() { Trace.Add("reverse.scene"); }
        internal static void Cleanup() { Trace.Add("reverse.cleanup"); if (Instance != null) UnityEngine.Object.Destroy(Instance.gameObject); Instance = null; }
    }
    internal sealed class ReverseScaleEffectManager : UnityEngine.MonoBehaviour
    {
        internal static ReverseScaleEffectManager Instance;
        internal static void EnsureInstance() { Trace.Add("reverse.effect"); if (Instance == null) Instance = new ReverseScaleEffectManager(); }
        internal void CheckCurrentEquipment() { Trace.Add("reverse.equipment"); }
    }
    internal abstract class AbilityManager : UnityEngine.MonoBehaviour
    {
        internal bool IsAbilityEnabled;
        internal CharacterMainControl TargetCharacter;
        internal void OnSceneChanged() { Trace.Add(GetType().Name + ".scene"); }
        internal void RegisterAbility(CharacterMainControl value) { IsAbilityEnabled = true; TargetCharacter = value; Trace.Add(GetType().Name + ".register"); }
        internal void RebindToCharacter(CharacterMainControl value) { TargetCharacter = value; Trace.Add(GetType().Name + ".rebind"); }
    }
    internal sealed class PhantomWitchScytheAbilityManager : AbilityManager
    {
        internal static PhantomWitchScytheAbilityManager Instance;
        public PhantomWitchScytheAbilityManager() { Instance = this; }
        internal static void CleanupStatic() { Trace.Add("scythe.cleanup"); if (Instance != null) UnityEngine.Object.Destroy(Instance.gameObject); Instance = null; }
    }
    internal sealed class FenHuangHalberdAbilityManager : AbilityManager
    {
        internal static FenHuangHalberdAbilityManager Instance;
        public FenHuangHalberdAbilityManager() { Instance = this; }
        internal static void CleanupStatic() { Trace.Add("halberd.cleanup"); if (Instance != null) UnityEngine.Object.Destroy(Instance.gameObject); Instance = null; }
    }
    internal sealed class FenHuangComboManager : UnityEngine.MonoBehaviour
    {
        internal static FenHuangComboManager Instance;
        public FenHuangComboManager() { Instance = this; }
        internal void OnSceneChanged() { Trace.Add("combo.scene"); }
    }
    internal static class PhantomWitchAssetManager
    {
        internal static int References;
        internal static bool HasActiveReferences { get { return References > 0; } }
        internal static void AddReference() { References++; Trace.Add("asset.add"); }
        internal static void ClearCache() { References--; Trace.Add("asset.release"); }
    }
    internal static class PhantomWitchCurseSweatVfx
    {
        internal static void RegisterGlobalHook() { Trace.Add("hook.add"); }
        internal static void UnregisterGlobalHook() { Trace.Add("hook.remove"); }
    }
    internal static class PhantomWitchScytheWeaponConfig { internal static void RefreshExistingCurseBindings() { Trace.Add("curse.refresh"); } }
    internal static class PhantomWitchScytheBossDropHandler { internal static void Cleanup() { Trace.Add("drop.cleanup"); } }
    internal static class PhantomWitchCurseRealmVisual { internal static void ClearCache() { Trace.Add("visual.cleanup"); } }
    internal static class DragonFlameMarkTracker { internal static void ClearAll() { Trace.Add("mark.cleanup"); } }
}
