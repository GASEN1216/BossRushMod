using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Destroyed = true; }
    }
    public class GameObject : Object { public GameObject(string name) { } }
    public class Transform { public GameObject gameObject = new GameObject("resident"); }
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string message) { } }
    public class DuckovDialogueActor : UnityEngine.Object, NodeCanvas.DialogueTrees.IDialogueActor { }
    public static class DialogueActorFactory
    {
        public static bool Fail;
        public static DuckovDialogueActor Get(UnityEngine.GameObject host) { return null; }
        public static void RefreshPresentation(DuckovDialogueActor actor, string cn, string en, object portrait = null) { }
        public static DuckovDialogueActor CreateBilingual(UnityEngine.GameObject host, string id, string cn, string en, UnityEngine.Vector3 offset, object portrait)
        { if (Fail) throw new InvalidOperationException("actor failure"); return new DuckovDialogueActor(); }
    }
    public static class SkyIslandWorldStory { public static string ResidentName(string id) { return id; } }
    public static class SkyIslandUiArt { public static object GetPortrait(string id) { return null; } }
}
