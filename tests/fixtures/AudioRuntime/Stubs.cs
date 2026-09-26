using System;
using System.Collections.Generic;
using Duckov.ItemUsage;
using UnityEngine;

internal static class Probe
{
    internal static readonly List<string> Trace = new List<string>();
    internal static readonly List<Post> Posts = new List<Post>();
    internal static readonly List<Egg> Eggs = new List<Egg>();
    internal static bool ThrowMain, ThrowResources, ThrowInstantiate, ThrowInit, ThrowPost;
    internal static int Scans, Instantiates, Collisions, PlayerReads, PathReads;
    internal static SpawnEgg[] Resources;
    internal static CharacterMainControl Main;
    internal static SpawnEgg BeforeReset;
    internal static CharacterRandomPreset BeforeResetPreset;
    internal static bool CacheEmptyAfterReset;
    internal sealed class Post { internal string Path; internal GameObject Target; internal bool Loop; }
    internal static void RecordPost(string path, GameObject target, bool loop)
    {
        Trace.Add("audio");
        Posts.Add(new Post { Path = path, Target = target, Loop = loop });
        if (ThrowPost) throw new InvalidOperationException("audio failure");
    }
    internal static void Clear()
    {
        Trace.Clear(); Posts.Clear(); Eggs.Clear();
        ThrowMain = ThrowResources = ThrowInstantiate = ThrowInit = ThrowPost = false;
        Scans = Instantiates = Collisions = PlayerReads = PathReads = 0;
        Resources = null; Main = null; BeforeReset = null; BeforeResetPreset = null;
        CacheEmptyAfterReset = false;
    }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject gameObject = value as GameObject;
            if (gameObject != null)
                foreach (Component component in gameObject.Components) component.Destroyed = true;
            value.Destroyed = true;
        }
        public static T Instantiate<T>(T prefab, Vector3 position, Quaternion rotation) where T : Object
        {
            Probe.Trace.Add("instantiate"); Probe.Instantiates++;
            if (Probe.ThrowInstantiate) throw new InvalidOperationException("instantiate failure");
            Egg original = prefab as Egg;
            Egg egg = new GameObject().AddComponent<Egg>();
            if (original.GetComponent<Collider>() != null) egg.gameObject.AddComponent<Collider>();
            egg.transform.position = position;
            Probe.Eggs.Add(egg);
            return egg as T;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class Collider : Component { }
    public class Transform { public Vector3 position; }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf; } }
        public T AddComponent<T>() where T : Component, new()
        {
            T value = new T { gameObject = this }; Components.Add(value); return value;
        }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component value in Components) if (value is T && value != null) return (T)value;
            return null;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator *(Vector3 value, float factor) { return new Vector3(value.x * factor, value.y * factor, value.z * factor); }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public static class Resources
    {
        public static T[] FindObjectsOfTypeAll<T>()
        {
            Probe.Trace.Add("resources"); Probe.Scans++;
            if (Probe.ThrowResources) throw new InvalidOperationException("resource failure");
            return (T[])(object)Probe.Resources;
        }
    }
    public static class Physics
    {
        public static void IgnoreCollision(Collider first, Collider second, bool ignore)
        {
            if (first == null || second == null || !ignore) throw new InvalidOperationException("invalid collision mask");
            Probe.Trace.Add("collision"); Probe.Collisions++;
        }
    }
}

public class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main
    {
        get { Probe.Trace.Add("main"); if (Probe.ThrowMain) throw new InvalidOperationException("main failure"); return Probe.Main; }
    }
    public Vector3 CurrentAimDirection;
}
public class CharacterRandomPreset : UnityEngine.Object { }
public class Egg : MonoBehaviour
{
    internal Vector3 SpawnPosition, Velocity;
    internal CharacterMainControl From;
    internal CharacterRandomPreset Preset;
    internal float Delay;
    internal int InitCalls;
    public void Init(Vector3 position, Vector3 velocity, CharacterMainControl from, CharacterRandomPreset preset, float delay)
    {
        Probe.Trace.Add("init"); InitCalls++;
        if (Probe.ThrowInit) throw new InvalidOperationException("init failure");
        SpawnPosition = position; Velocity = velocity; From = from; Preset = preset; Delay = delay;
    }
}
namespace Duckov.ItemUsage
{
    public class SpawnEgg : MonoBehaviour
    {
        public Egg eggPrefab;
        public CharacterRandomPreset spawnCharacter;
        public float eggSpawnDelay;
    }
}

#if !MISSING_AUDIO
namespace Duckov
{
    public static class AudioManager
    {
#if OFFICIAL_RETURN
        public static FMOD.Studio.EventInstance? PostCustomSFX(string path, GameObject target, bool loop)
        {
            Probe.RecordPost(path, target, loop); return null;
        }
#else
        public static void PostCustomSFX(string path, GameObject target, bool loop)
        {
            Probe.RecordPost(path, target, loop);
        }
#endif
    }
}
#endif

namespace FMOD.Studio { public struct EventInstance { } }

namespace BossRush
{
    internal sealed class ModInfo
    {
        internal string RawPath;
        internal bool ThrowRead;
        internal string path { get { Probe.Trace.Add("path"); Probe.PathReads++; if (ThrowRead) throw new InvalidOperationException("path failure"); return RawPath; } }
    }
    public partial class ModBehaviour
    {
        internal MonoBehaviour RawPlayer;
        internal ModInfo info;
        private MonoBehaviour playerCharacter { get { Probe.PlayerReads++; return RawPlayer; } }
        public static void DevLog(string message) { }
        internal BossRushAudioRuntimeService AudioForFixture { get { return audioRuntime; } }
    }
    public static class DialogueActorFactory
    {
        public static void ResetStaticCaches() { Probe.Trace.Add("dialogue-reset"); }
    }
}
namespace BossRush.Patches.Compatibility
{
    public static class MagicBlendInitializationOrderPatch
    {
        public static void ResetStaticCaches()
        {
            Probe.Trace.Add("magic-reset");
            Probe.BeforeReset = BossRush.BossRushAudioRuntimeService.CachedSpawnEggBehavior;
            Probe.BeforeResetPreset = BossRush.BossRushAudioRuntimeService.EggSpawnPreset;
        }
    }
}
namespace BossRush.Utils
{
    public static class NPCUIAssetCache
    {
        public static void ResetStaticCaches()
        {
            Probe.Trace.Add("npc-reset");
            Probe.CacheEmptyAfterReset = ReferenceEquals(BossRush.BossRushAudioRuntimeService.CachedSpawnEggBehavior, null)
                && ReferenceEquals(BossRush.BossRushAudioRuntimeService.EggSpawnPreset, null);
        }
    }
}
