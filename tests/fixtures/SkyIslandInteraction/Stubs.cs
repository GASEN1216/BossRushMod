// 仅宿主替身：对象销毁模拟 Unity 的 fake-null 和组件级联；不宣称模拟真实渲染、物理、音频。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null) || obj.Destroyed) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (ReferenceEquals(go, null)) return;
            foreach (Component c in go.Components) c.Destroyed = true;
            foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
        }
        public static void DestroyImmediate(Object obj) { Destroy(obj); }
    }
    internal class Component : Object
    {
        internal GameObject gameObject;
        internal Transform transform { get { return gameObject.transform; } }
    }
    internal class GameObject : Object
    {
        internal string name;
        internal Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        internal GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            Components.Add(transform);
        }
        internal T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components.Add(c); return c; }
        internal T GetComponent<T>() where T : Component
        {
            if (Destroyed) return null;
            foreach (var c in Components) if (c is T && !c.Destroyed) return (T)c;
            return null;
        }
    }
    internal class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        internal Transform parent;
        internal Vector3 position, localPosition, forward = Vector3.forward;
        internal void SetParent(Transform value, bool worldPositionStays)
        { if (parent != null) parent.Children.Remove(this); parent = value; if (value != null) value.Children.Add(this); }
    }
    internal class RectTransform : Transform
    {
        internal Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
    }
    internal class Canvas : Component { }
    internal class Sprite : Object { internal Rect rect = new Rect { width = 1024, height = 288 }; }
    internal struct Rect { internal float width, height; }
    internal struct Vector2
    {
        internal float x, y;
        internal Vector2(float x, float y) { this.x = x; this.y = y; }
        internal static Vector2 zero { get { return new Vector2(); } }
    }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal float sqrMagnitude { get { return x * x + y * y + z * z; } }
        internal static Vector3 zero { get { return new Vector3(); } }
        internal static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 operator *(Vector3 a, float f) { return new Vector3(a.x * f, a.y * f, a.z * f); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    }
    internal struct Color
    {
        internal float r, g, b, a;
        internal Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        internal static Color white { get { return new Color(1, 1, 1); } }
        internal static Color Lerp(Color x, Color y, float t)
        { return new Color(Mathf.Lerp(x.r, y.r, t), Mathf.Lerp(x.g, y.g, t), Mathf.Lerp(x.b, y.b, t), Mathf.Lerp(x.a, y.a, t)); }
    }
    internal static class Mathf
    {
        internal const float PI = (float)Math.PI;
        internal static float Cos(float a) { return (float)Math.Cos(a); }
        internal static float Sin(float a) { return (float)Math.Sin(a); }
        internal static float Min(float a, float b) { return Math.Min(a, b); }
        internal static float Max(float a, float b) { return Math.Max(a, b); }
        internal static int Max(int a, int b) { return Math.Max(a, b); }
        internal static float Clamp(float x, float a, float b) { return Max(a, Min(b, x)); }
        internal static int Clamp(int x, int a, int b) { return Math.Max(a, Math.Min(b, x)); }
        internal static float Clamp01(float x) { return Clamp(x, 0, 1); }
        internal static float Floor(float x) { return (float)Math.Floor(x); }
        internal static int CeilToInt(float x) { return (int)Math.Ceiling(x); }
        internal static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }
    internal static class Time { internal static float time; }
    internal static class Debug { internal static void LogWarning(string value) { } }
    internal class LineRenderer : Component
    {
        private Vector3[] vertices = new Vector3[0];
        private float width;
        private Color start, end;
        internal int VertexWrites, CountWrites, WidthWrites, ColorWrites;
        internal int positionCount { get { return vertices.Length; } set { Array.Resize(ref vertices, value); CountWrites++; } }
        internal void SetPosition(int index, Vector3 value) { vertices[index] = value; VertexWrites++; }
        internal Vector3 GetPosition(int index) { return vertices[index]; }
        internal float widthMultiplier { get { return width; } set { width = value; WidthWrites++; } }
        internal Color startColor { get { return start; } set { start = value; ColorWrites++; } }
        internal Color endColor { get { return end; } set { end = value; ColorWrites++; } }
    }
}

namespace UnityEngine.UI
{
    internal class Image : UnityEngine.Component
    {
        internal UnityEngine.Color color;
        internal bool raycastTarget;
        internal void CrossFadeColor(UnityEngine.Color value, float duration, bool ignoreTimeScale, bool useAlpha) { color = value; }
    }
    internal struct ColorBlock { internal UnityEngine.Color normalColor; }
    internal struct Navigation { internal enum Mode { None } internal Mode mode; }
    internal sealed class ButtonEvent
    {
        private Action handler;
        internal void AddListener(Action callback) { handler += callback; }
        internal void Invoke() { if (handler != null) handler(); }
    }
    internal class Button : UnityEngine.Component
    {
        internal Image targetGraphic;
        internal ColorBlock colors;
        internal Navigation navigation;
        internal bool interactable = true;
        internal readonly ButtonEvent onClick = new ButtonEvent();
    }
    internal class ScrollRect : UnityEngine.Component { }
}
namespace UnityEngine.EventSystems
{
    internal enum EventTriggerType { PointerEnter }
    internal class EventTrigger : UnityEngine.Component
    {
        internal sealed class Callback { internal void AddListener(Action<object> callback) { } }
        internal sealed class Entry { internal EventTriggerType eventID; internal Callback callback = new Callback(); }
        internal List<Entry> triggers = new List<Entry>();
    }
}
namespace TMPro
{
    internal enum TextAlignmentOptions { Left, TopLeft }
    internal enum TextOverflowModes { Ellipsis }
    internal class TextMeshProUGUI : UnityEngine.Component
    {
        internal string text;
        internal float fontSize;
        internal bool enableAutoSizing, enableWordWrapping, raycastTarget;
        internal UnityEngine.Color color;
        internal TextAlignmentOptions alignment;
        internal TextOverflowModes overflowMode;
        internal UnityEngine.RectTransform rectTransform { get { return (UnityEngine.RectTransform)transform; } }
        internal UnityEngine.Vector2 GetPreferredValues(string value, float width, float height)
        { return new UnityEngine.Vector2((value ?? "").Length * fontSize * 0.6f, fontSize * 1.25f); }
    }
}

internal static class HUDManager
{
    internal static readonly List<UnityEngine.GameObject> Tokens = new List<UnityEngine.GameObject>();
    internal static void RegisterHideToken(UnityEngine.GameObject token) { Tokens.Add(token); }
    internal static void UnregisterHideToken(UnityEngine.GameObject token) { Tokens.Remove(token); }
}
internal sealed class TestHealth
{
    internal float CurrentHealth = 50, MaxHealth = 100;
    internal void SetHealth(float health) { CurrentHealth = health; }
}
internal sealed class CharacterMainControl
{
    internal TestHealth Health = new TestHealth();
    internal UnityEngine.Transform transform = new UnityEngine.GameObject("player").transform;
}
namespace Duckov.Economy
{
    internal struct Cost { internal long Amount; internal Cost(long amount) { Amount = amount; } }
    internal static class EconomyManager
    {
        internal static bool Enough = true, Payment = true;
        internal static int Payments;
        internal static bool IsEnough(Cost cost, bool account, bool cash) { return Enough; }
        internal static bool Pay(Cost cost, bool account, bool cash) { if (!Payment || !Enough) return false; Payments++; return true; }
    }
}
namespace Duckov
{
    internal enum StopMode { Immediate = 1 }
    internal class AudioObject : UnityEngine.Component
    {
        internal int Stops;
        internal bool StoppedBeforeDestroy;
        private void StopAll(StopMode mode) { Stops++; StoppedBeforeDestroy = !Destroyed && mode == StopMode.Immediate; }
    }
}

namespace BossRush
{
    internal static class L10n
    { internal static bool IsChinese = true; internal static string T(string cn, string en) { return IsChinese ? cn : en; } }
    internal enum BossRushUISkinPart { Panel, Card, Rule }
    internal static class BossRushUILayers { internal const int Modal = 10; }
    internal static class BossRushUIColors
    {
        internal static Color TextPrimary = Color.white, SurfaceRaised = new Color(.1f, .1f, .1f), Surface = new Color(.05f, .05f, .05f),
            Stroke = Color.white, Accent = Color.white, Divider = Color.white;
    }
    internal static class BossRushUI
    {
        internal static int Canvases, Opens;
        internal static Canvas CreateCanvasRoot(string name, int layer, bool events) { Canvases++; return new GameObject(name).AddComponent<Canvas>(); }
        internal static void CreateBackdrop(Transform t) { }
        internal static void ApplyGameFont(TMPro.TextMeshProUGUI t) { }
        internal static float MeasureTextHeight(TMPro.TextMeshProUGUI text, float width, float minimum)
        { return Math.Max(minimum, ((text.text ?? "").Length / 35 + 1) * 30); }
        internal static void ApplyPanelSkin(UnityEngine.UI.Image i, float radius, BossRushUISkinPart p) { }
        internal static UnityEngine.UI.Image ApplyPanelStroke(UnityEngine.UI.Image i, float radius, BossRushUISkinPart part, Color color)
        { return i.gameObject.AddComponent<UnityEngine.UI.Image>(); }
        internal static Color GetDisabledColor(Color c) { return c; }
        internal static Color GetButtonTextColor(Color c) { return Color.white; }
        internal static void PlayOpenAnimation(GameObject go) { Opens++; }
    }
    internal static class ZombieModeUIHelper
    {
        internal sealed class ModalInputLease
        {
            private bool released;
            internal void Release() { if (released) return; released = true; Leases--; }
        }
        internal static int Leases;
        internal static Vector2 GetReferenceViewportSize() { return new Vector2(1920, 1080); }
        internal static ModalInputLease ClaimModalInput(GameObject go, string owner) { Leases++; return new ModalInputLease(); }
        internal static void ApplyButtonColors(UnityEngine.UI.Button b, Color normal, Color hover, Color disabled)
        { b.colors = new UnityEngine.UI.ColorBlock { normalColor = normal }; }
    }
    internal static class BossRushUIEntranceAnimation { internal static void Play(GameObject go, float delay, float seconds, float rise) { } }
    internal static class SkyIslandUiArt { internal static Sprite GetPanelBackground(Sprite banner) { return banner; } }
    internal static class SkyIslandNoteBridge { internal static int Unlocks; internal static void Unlock(string key) { Unlocks++; } }
    internal static class ModBehaviour { internal static void DevLog(string text) { } }
    internal enum SkyIslandLootTier { Supply, Voyage, Starworks }
    internal static class SkyIslandItemRules
    {
        internal static string BadgeDiscountNote { get { return " badge"; } }
        internal static int ServicePrice(int price, bool badge) { return badge ? Math.Max(1, price / 2) : price; }
    }
    internal static class SkyIslandRewardCrate
    {
        internal static bool PlacementAvailable = true, Created = true;
        internal static int CreateCalls;
        internal static bool TryFindCratePosition(Transform root, Vector3 anchor, float bearing, float distance, int mask, out Vector3 result)
        { result = anchor + Vector3.forward * distance; return PlacementAvailable; }
        internal static bool Create(Transform root, Vector3 position, SkyIslandLootTier tier, string name, string stream, int seed, int count, bool guarantee)
        { CreateCalls++; return Created; }
    }
}
