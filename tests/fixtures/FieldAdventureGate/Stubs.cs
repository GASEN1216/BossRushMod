using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Lerp(float a, float b, float t) { return a+(b-a)*t; }
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Sin(float a) { return (float)Math.Sin(a); }
        public static float Abs(float a) { return Math.Abs(a); }
    }
    public static class Random { public static float Range(float a, float b) { return a; } }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Vector3 point, normal; }
    public static class Physics
    {
        public static bool Ground = true, Wall;
        public static float Slope = 1, GroundOffset;
        public static int Raycasts;
        public static bool Raycast(Vector3 from, Vector3 direction, out RaycastHit hit, float length, int mask, QueryTriggerInteraction triggers)
        {
            Raycasts++;
            hit = new RaycastHit { point = new Vector3(from.x, from.y-1.5f+GroundOffset, from.z), normal = new Vector3(0,Slope,0) };
            return Ground;
        }
        public static bool CheckCapsule(Vector3 a, Vector3 b, float r, int mask, QueryTriggerInteraction triggers) { return Wall; }
    }
    public class GameObject { public UnityEngine.SceneManagement.Scene scene; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name;
        public int handle;
        public bool isLoaded;
        public bool IsValid() { return handle != 0; }
    }
}
namespace Duckov.Scenes
{
    public class MultiSceneCore
    {
        public static MultiSceneCore Instance;
        public static UnityEngine.SceneManagement.Scene? ActiveSubScene;
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public bool IsLoading;
    }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class Layers { public const int groundLayerMask = 1, wallLayerMask = 2; }
    }
}
public class LevelManager
{
    public static LevelManager Instance;
    public static bool AfterInit;
    public bool IsBaseLevel, IsRaidMap;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}
public class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Health Health;
}
public class Health { public bool IsDead; }
public static class SceneLoader { public static bool IsSceneLoading; }
namespace Duckov.UI
{
    internal static class NotificationText { public static void Push(string text) { } }
}

namespace BossRush
{
    internal class ModBehaviour
    {
        public static ModBehaviour Instance;
        public bool IsActive, IsModeDActive, IsModeEActive, IsModeFActive, IsZombieModeActive,
            IsBossRushArenaActive, IsCampaignFinalBossActive, CampaignArenaActiveForRuntime;
        public static bool ModeG, ModeH;
        public ZombieRun CampaignZombieRunForRuntime;
        public ModeHRuntime ModeHRuntime;
        public static bool IsModeGRunInProgressSafe() { return ModeG; }
        public static bool IsModeHRunInProgressSafe() { return ModeH; }
        public static void DevLog(string text) { }
        public string LastMessage;
        public void ShowMessage(string text) { LastMessage = text; }
    }
    internal class ZombieRun { public ZombieModeLifecyclePhase LifecyclePhase; }
    internal enum ZombieModeLifecyclePhase { None, Preparing, Active }
    internal class ModeHRuntime { public ModeHState RunState; }
    internal class ModeHState { public ModeHLifecycle Lifecycle; }
    internal enum ModeHLifecycle { None, Preparing, Active }
    internal static class WavesArenaRuntimeModule { public static bool ArenaPlanned, ArenaActive; }
    internal enum BossRushPendingEntryKind { None, Arena }
    internal static class BossRushMapSelectionHelper
    {
        public static BossRushPendingEntryKind Pending;
        public static BossRushPendingEntryKind GetPendingEntryKind() { return Pending; }
    }
    internal static class SceneRuntimeGate
    {
        public static bool IsBaseHubSceneName(string name) { return name == "Base"; }
        public static bool IsModResourceScene(UnityEngine.SceneManagement.Scene scene) { return scene.name == "Resource"; }
        public static bool IsGameplaySceneName(string name) { return name != "LoadingScreen"; }
        public static bool CanRunGameplayRuntimeNow(string name) { return !SceneLoader.IsSceneLoading; }
    }
    internal static class SpawnPositionHelper
    {
        public static bool Anchor = true, Reachable = true;
        public static int Queries;
        public static UnityEngine.Vector3 Offset;
        public static bool IsReachableAnchor(UnityEngine.Vector3 origin) { return Anchor; }
        public static bool TryResolveReachableFrom(UnityEngine.Vector3 candidate, UnityEngine.Vector3 anchor, float snap, float lift, out UnityEngine.Vector3 position)
        {
            Queries++;
            position = candidate + Offset;
            return Reachable;
        }
    }
    internal static class FieldAdventureRoute
    {
        public static bool Walkable = true;
        public static int Queries;
        public static bool IsWalkable(UnityEngine.Vector3 from, UnityEngine.Vector3 to) { Queries++; return Walkable; }
    }
    internal static class L10n { public static string T(string cn,string en) { return cn; } }
}
