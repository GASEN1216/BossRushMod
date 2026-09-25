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
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            go.Destroyed = true;
            foreach (Component component in go.Components) component.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public T GetComponent<T>() where T : Component
        { foreach (Component c in Components) if (c is T && c != null) return (T)c; return null; }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components.Add(c); return c; }
    }
    public class Transform : Object { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public static class Time { public static float realtimeSinceStartup, unscaledTime; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { buildIndex=7 }; } }
}
namespace Duckov.Modding { public class ModBehaviour { } }
namespace Duckov.UI.DialogueBubbles
{
    public static class DialogueBubblesManager
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Show(string message, UnityEngine.Transform target, float y, bool a, bool b, float c, float duration)
        { Messages.Add(message); }
    }
}
namespace BossRush
{
    using UnityEngine;
    public sealed class CharacterMainControl : Component { public static CharacterMainControl Main; }
    public sealed class CourierNPCController : Component { }
    public sealed class InteractableLootbox : Component { public bool Marked=true; public int Session; public BossRushTrackedLootboxMode Mode; }
    internal sealed class AwenLootSweepRunner : Component
    {
        internal bool IsRunning, AllowBegin=true;
        internal readonly List<AwenLootSweepTarget> Snapshot = new List<AwenLootSweepTarget>();
        internal bool BeginSweep(List<AwenLootSweepTarget> targets, BossRushTrackedLootboxMode mode, int token, int scene)
        {
            Program.Events.Add("begin");
            Snapshot.Clear(); Snapshot.AddRange(targets);
            IsRunning=AllowBegin;
            return AllowBegin;
        }
        internal void CancelSweep(bool restore) { Program.Events.Add("cancel:"+restore); IsRunning=false; }
    }
    internal static class CourierPaidLootSweepService
    { internal static void ReleasePendingSweepResultToPlayer(bool close, bool message) { Program.Events.Add("release:"+close+":"+message); } }
    internal static class BossRushLootboxUtility
    {
        internal static readonly List<InteractableLootbox> Boxes = new List<InteractableLootbox>();
        internal static int Scans;
        internal static bool IsMarkedLootbox(InteractableLootbox box, int scene=int.MinValue) { return box != null && box.Marked; }
        internal static int CollectMarkedLootboxes(List<InteractableLootbox> output, int scene)
        { Scans++; foreach (var box in Boxes) if (IsMarkedLootbox(box)) output.Add(box); return output.Count; }
        internal static void StampLootboxMarker(InteractableLootbox box, BossRushTrackedLootboxMode mode, int token)
        { box.Mode=mode; box.Session=token; }
    }
    internal static class AwenLootSweepTokenConfig
    {
        internal const int TYPE_ID=500048;
        internal static bool Registration=true;
        internal static bool EnsureRuntimeRegistration() { Program.Events.Add("register"); return Registration; }
        internal static string GetDisplayName() { return "sweep-token"; }
    }
    internal static class L10n { internal static string T(string zh, string en) { return en; } }
    internal sealed class ModeFState { internal bool IsActive=true; }
    internal sealed class ModeFRuntimeModule
    {
        internal bool Delivery=true;
        internal int Grants;
        internal bool TryGiveItemToPlayerOrDrop(int typeId, string name, bool bubble, bool drop)
        {
            Program.Events.Add("give:"+bubble+":"+drop);
            if (Delivery) Grants++;
            return Delivery;
        }
    }
    public partial class ModBehaviour
    {
        internal bool modeFActive, modeEActive, IsActive, IsModeDActive, IsBossRushArenaActive;
        internal int CurrentModeFSessionToken=2, CurrentModeESessionToken=1;
        internal bool FSessionValid=true, ESessionValid=true;
        internal readonly ModeFState modeFState=new ModeFState();
        internal readonly ModeFRuntimeModule modeFRuntime=new ModeFRuntimeModule();
        internal GameObject courierNPCInstance;
        internal CourierNPCController courierController;
        internal readonly List<string> Banners=new List<string>(), Messages=new List<string>();
        internal bool IsModeFSessionStillValid(int token,int scene) { return FSessionValid && token==CurrentModeFSessionToken && scene==7; }
        internal bool IsModeESessionStillValid(int token,int scene) { return ESessionValid && token==CurrentModeESessionToken && scene==7; }
        internal static void DevLog(string message) { }
        internal void ShowBigBanner(string message) { Banners.Add(message); Program.Events.Add("banner"); }
        internal void ShowMessage(string message) { Messages.Add(message); }
        internal void Setup()
        {
            courierNPCInstance=new GameObject();
            courierController=courierNPCInstance.AddComponent<CourierNPCController>();
            BindAwenLootSweepRuntime();
        }
    }
}
