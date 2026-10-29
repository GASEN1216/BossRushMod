using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(VoidBuilder))]
    public struct UniTaskVoid { public void Forget() { } }
    public struct VoidBuilder
    {
        private AsyncVoidMethodBuilder builder;
        public static VoidBuilder Create() { return new VoidBuilder { builder = AsyncVoidMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid(); } }
        public void Start<T>(ref T state) where T : IAsyncStateMachine { builder.Start(ref state); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception error) { throw error; }
        public void AwaitOnCompleted<T, S>(ref T awaiter, ref S state) where T : INotifyCompletion where S : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<T, S>(ref T awaiter, ref S state) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name = "object";
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void DontDestroyOnLoad(Object target) { }
        public static void Destroy(Object target)
        {
            if (ReferenceEquals(target, null) || target.Destroyed) return;
            target.Destroyed = true;
            GameObject root = target as GameObject;
            if (!ReferenceEquals(root, null)) foreach (Component child in root.Components.ToArray()) Destroy(child);
            target.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public GameObject(string name = "object", params Type[] types)
        {
            this.name = name;
            bool rect = Array.IndexOf(types, typeof(RectTransform)) >= 0;
            transform = (Transform)Add(rect ? typeof(RectTransform) : typeof(Transform));
            foreach (Type type in types) if (type != typeof(RectTransform) && type != typeof(Transform)) Add(type);
        }
        private Component Add(Type type) { Component item = (Component)Activator.CreateInstance(type); item.gameObject = this; Components.Add(item); return item; }
        public T AddComponent<T>() where T : Component, new() { return (T)Add(typeof(T)); }
        public T GetComponent<T>() where T : Component { foreach (Component item in Components) if (item is T && !item.Destroyed) return (T)item; return null; }
    }
    public class Transform : Component { public Vector3 position; public void SetParent(Transform parent, bool world) { } }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; public static Vector3 forward = new Vector3(); }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a) { this.r=r; this.g=g; this.b=b; this.a=a; } }
    public class RectOffset { public RectOffset(int a, int b, int c, int d) { } }
    public enum RenderMode { ScreenSpaceOverlay }
    public class Canvas : Component { public RenderMode renderMode; public int sortingOrder; }
    public static class ColorUtility { public static string ToHtmlStringRGB(Color value) { return "ffffff"; } }
    public static class Mathf { public static int CeilToInt(float f) { return (int)Math.Ceiling(f); } public static int Max(int a, int b) { return Math.Max(a,b); } public static int Clamp(int v, int a, int b) { return Math.Max(a, Math.Min(b,v)); } }
    public static class Time { public static float unscaledDeltaTime; }
    public enum KeyCode { PageDown, Keypad3, PageUp, Keypad9, Home, Keypad7, Insert, Keypad0, End, Keypad1 }
    public static class Input { public static bool GetKeyDown(KeyCode key) { return false; } }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() where T : class { return new[] { CharacterRandomPreset.Preset as T }; } }
}
namespace UnityEngine.UI
{
    public class CanvasScaler : UnityEngine.Component { }
    public class Image : UnityEngine.Component { public UnityEngine.Color color; public bool raycastTarget; }
    public class VerticalLayoutGroup : UnityEngine.Component { public UnityEngine.RectOffset padding; public bool childControlWidth, childControlHeight, childForceExpandHeight; }
    public class ContentSizeFitter : UnityEngine.Component { public enum FitMode { PreferredSize } public FitMode verticalFit; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public int buildIndex { get { return handle; } }
        public bool isLoaded { get { return Loaded.Contains(handle); } }
        public bool IsValid() { return handle > 0; }
        public static readonly HashSet<int> Loaded = new HashSet<int>();
    }
}
namespace TMPro
{
    public class TextMeshProUGUI : UnityEngine.Component
    { public string text; public float fontSize, lineSpacing; public UnityEngine.Color color; public bool enableWordWrapping, raycastTarget; }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Component
    { public int TypeID, StackCount, MaxStackCount = 99; public bool Stackable, CanBeSold; public float Value, Durability, MaxDurability; public HashSet<string> Tags = new HashSet<string>(); }
}
namespace Duckov.Utilities
{
    public class ItemFilter { public object requireTags; public int minQuality, maxQuality; public string caliber; }
    public static class ItemAssetsCollection
    {
        public static int[] GetAllTypeIds(ItemFilter filter) { return new int[0]; }
        public static ItemStatsSystem.Item GetPrefab(int id) { return null; }
        public static ItemStatsSystem.Item InstantiateSync(int id) { return null; }
    }
}
public class Health { public bool IsDead; public float MaxHealth = 100f, CurrentHealth = 100f; public void SetHealth(float health) { CurrentHealth = health; } }
public class CharacterMainControl : UnityEngine.MonoBehaviour
{
    public static CharacterMainControl Main;
    public Health Health = new Health();
    public Teams Team;
    public int TeamChanges;
    public void SetTeam(Teams team) { Team = team; TeamChanges++; }
}
public enum Teams { player, wolf, neutral }
public static class Team { public static bool IsEnemy(Teams a, Teams b) { return b == Teams.wolf; } }
public static class SceneLoader { public static bool IsSceneLoading; }
public class CharacterRandomPreset : UnityEngine.Object
{
    public string nameKey = "Cname_Scav";
    public static readonly CharacterRandomPreset Preset = new CharacterRandomPreset();
    public static readonly List<TaskCompletionSource<CharacterMainControl>> Requests = new List<TaskCompletionSource<CharacterMainControl>>();
    public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 p, UnityEngine.Vector3 d, int scene, object group, bool leader)
    {
        var task = new TaskCompletionSource<CharacterMainControl>(); Requests.Add(task); return task.Task;
    }
}
public static class ItemUtilities { public static void SendToPlayer(ItemStatsSystem.Item item) { } }
namespace BossRush
{
    public class ModBehaviour : UnityEngine.MonoBehaviour { public static void DevLog(string message) { } }
    public static class L10n { public static bool IsChinese = true; public static string T(string cn, string en) { return IsChinese ? cn : en; } }
    public static class BossRushUI
    { public static bool IsGamePaused() { return false; } public static void ApplyPanelSkin(UnityEngine.UI.Image image, int radius, BossRushUISkinPart part) { } public static void ApplyGameFont(TMPro.TextMeshProUGUI text) { } }
    public enum BossRushUISkinPart { Card }
    public static class BossRushUILayers { public const int Hud = 1; }
    public static class BossRushUIColors { public static UnityEngine.Color Accent, TextSecondary, WarningText, DangerText, Surface, TextPrimary; }
    public static class BossRushUIKit { public static void ApplyWorldTextOutline(TMPro.TextMeshProUGUI text) { } }
    public static class ZombieModeUIHelper { public static void ConfigureCanvasScaler(UnityEngine.UI.CanvasScaler scaler) { } }
    public static class FieldAdventureSupport
    {
        public static bool RaidAllowed = true;
        public static UnityEngine.SceneManagement.Scene Scene;
        public static bool TryGetRaid(ModBehaviour host, out CharacterMainControl player, out UnityEngine.SceneManagement.Scene scene)
        {
            player = CharacterMainControl.Main; scene = Scene;
            return host != null && RaidAllowed && !SceneLoader.IsSceneLoading && scene.isLoaded && player != null && player.Health != null && !player.Health.IsDead;
        }
        public static bool TryFindGroundBands(UnityEngine.Vector3 origin, float[] bands, out UnityEngine.Vector3 point) { point = origin; return true; }
    }
    public static class SpawnedEnemyActivationHelper
    { public static int Activations; public static void ReleaseFromPlayerDistanceSleep(CharacterMainControl enemy) { Activations++; } }
    public static class EclipseBloomBoss
    {
        public static readonly List<TaskCompletionSource<CharacterMainControl>> Requests = new List<TaskCompletionSource<CharacterMainControl>>();
        public static readonly List<Func<bool>> Validity = new List<Func<bool>>();
        public static Task<CharacterMainControl> SpawnAsync(ModBehaviour owner, UnityEngine.Vector3 point, Func<bool> valid, UnityEngine.SceneManagement.Scene scene)
        { var task = new TaskCompletionSource<CharacterMainControl>(); Requests.Add(task); Validity.Add(valid); return task.Task; }
    }
    public enum GreedyMimicFood { Ammunition, Explosive, Medicine }
    public static class GreedyMimicInventory { public static bool MatchesFood(ItemStatsSystem.Item item, GreedyMimicFood food) { return false; } }
    public static class BossRushItemIds { public const int BossRushTicket = 500001, ResidualSunShield = 500110; }
    public static class BossRushDynamicItemRegistry { public static void EnsureRegistered(int id) { } }
    public static class ResidualSunShieldWeaponConfig { public static bool TryConfigure(ItemStatsSystem.Item item) { return true; } }
    public class GreedyMimicRuntimeModule
    { public static GreedyMimicRuntimeModule DevCurrent; public bool DevForceSpawn(out string reason) { reason = null; return true; } }
    public class OddParcelRuntimeModule { public static OddParcelRuntimeModule DevCurrent; public string DevResetOrders() { return null; } }
    public enum DangerousPrototypeKind { JetCannon, RicochetSun, QuackRail, Thunderbill, GrandFinale }
    public class DangerousPrototypeRuntimeModule
    { public static DangerousPrototypeRuntimeModule DevCurrent; public bool DevForceBench(DangerousPrototypeKind kind, out string reason) { reason = null; return true; } }
    public static class DangerousPrototypeCatalog { public static string Name(DangerousPrototypeKind kind) { return kind.ToString(); } }
}
