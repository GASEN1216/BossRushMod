using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null) && !value.Destroyed; }
        public static bool operator !(Object value) { return !(bool)value; }
        public static bool operator ==(Object a, Object b)
        { return (!(bool)a && !(bool)b) || ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T Instantiate<T>(T prefab, Vector3 point, Quaternion rotation) where T : Object
        { LevelManager.FxCount++; return prefab; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public int layer = 0;
        public GameObject(string value = "object") { name = value; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this, name = name }; components.Add(typeof(T), component); return component; }
        public T GetComponent<T>() where T : Component
        { Component value; return components.TryGetValue(typeof(T), out value) ? value as T : null; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Transform : Component { public Vector3 position; }
    public class Collider : Component { public float radius = 0.25f; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude > 0f ? this / magnitude : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1f / b); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static float Angle(Vector3 a, Vector3 b)
        { float n = a.magnitude * b.magnitude; return n <= 0f ? 0f : (float)(Math.Acos(Math.Max(-1, Math.Min(1, (a.x*b.x+a.y*b.y+a.z*b.z)/n))) * 180 / Math.PI); }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct LayerMask
    {
        public int value;
        public static implicit operator int(LayerMask mask) { return mask.value; }
        public static implicit operator LayerMask(int value) { return new LayerMask { value = value }; }
    }
    public struct Ray { public Vector3 origin, direction; public Ray(Vector3 o, Vector3 d) { origin=o; direction=d; } }
    public struct RaycastHit { }
    public static class Physics
    {
        public static readonly List<Collider> Contacts = new List<Collider>();
        public static readonly List<int> QueryCapacities = new List<int>();
        public static Func<Ray, float, bool> Blocked;
        public static bool ThrowQuery;
        public static int OverlapSphereNonAlloc(Vector3 center, float radius, Collider[] results, int layerMask)
        { return OverlapSphereNonAlloc(center, radius, results, layerMask, 0); }
        public static int OverlapSphereNonAlloc(Vector3 center, float radius, Collider[] results, int layerMask, int trigger)
        {
            if (ThrowQuery) throw new InvalidOperationException("physics query failed");
            QueryCapacities.Add(results.Length);
            int n = 0;
            foreach (Collider value in Contacts)
            {
                if (!value || !value.gameObject.activeSelf || (layerMask & (1 << value.gameObject.layer)) == 0 ||
                    Vector3.Distance(center, value.transform.position) > radius + value.radius) continue;
                if (n == results.Length) break;
                results[n++] = value;
            }
            return n;
        }
        public static bool Raycast(Ray ray, float distance, int mask) { return Blocked != null && Blocked(ray, distance); }
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] hits, float distance, int mask)
        { return Raycast(ray, distance, mask) ? 1 : 0; }
    }
    public static class Time { public static float deltaTime = 0.02f, time; }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Abs(float a) { return Math.Abs(a); }
    }
    public static class Debug { public static void LogWarning(string text) { Program.Warnings.Add(text); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; public bool IsValid() { return handle != 0; } }
    public static class SceneManager { public static Scene Active; public static Scene GetActiveScene() { return Active; } }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class Layers
        { public static LayerMask damageReceiverLayerMask = 1, wallLayerMask = 2, groundLayerMask = 4; }
    }
}
namespace Duckov { public static class HardwareSyncingManager { public static void SetEvent(string name) { } } }
public static class ObjectUtils
{ public static bool IsInLayerMask(GameObject value, int mask) { return ((1 << value.layer) & mask) != 0; } }
public enum Teams { middle, all, player, scav, wolf, bear }
public static class Team
{ public static bool IsEnemy(Teams selfTeam, Teams targetTeam) { return selfTeam != Teams.middle && (selfTeam == Teams.all || (targetTeam != Teams.middle && selfTeam != targetTeam)); } }
public enum ExplosionFxTypes { normal, flash, custom }
public enum AimTypes { normalAim }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float damageValue;
    public bool isExplosion;
    public DamageReceiver toDamageReceiver;
    public Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl source) : this() { fromCharacter = source; }
}
public class Health : UnityEngine.Object
{
    public CharacterMainControl Character;
    public CharacterMainControl TryGetCharacter() { return Character; }
}
public class DamageReceiver : Component
{
    public Health health;
    public Teams Team;
    public int Hits;
    public DamageInfo LastHit;
    public Action OnHit;
    public void Hurt(DamageInfo damage) { Hits++; LastHit = damage; if (OnHit != null) OnHit(); }
}
public class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public DamageReceiver mainDamageReceiver;
    public Teams Team;
    public bool Dashing, Running;
    public float VisableDistanceFactor = 1f;
    public Vector3 LastAim;
    public void SetAimPoint(Vector3 point) { LastAim = point; }
}
public class LevelManager
{
    public static bool LevelInited = true;
    public static LevelManager Instance;
    public CharacterMainControl MainCharacter;
    public static int FxCount;
}
public static class CameraShaker
{
    public enum CameraShakeTypes { explosion }
    public static Action OnShake;
    public static void Shake(Vector3 delta, CameraShakeTypes type) { if (OnShake != null) OnShake(); }
}
public partial class ExplosionManager : MonoBehaviour
{
    private LayerMask damageReceiverLayers, obsticleLayers;
    private List<Health> damagedHealth;
    private Collider[] colliders;
    private RaycastHit[] ObsHits = new RaycastHit[3];
    public GameObject normalFxPfb = new GameObject("normalFx"), flashFxPfb = new GameObject("flashFx");
    public Collider[] Buffer { get { return colliders; } }
    public List<Health> HealthBuffer { get { return damagedHealth; } }
    public LayerMask ReceiverLayers { get { return damageReceiverLayers; } }
}
public class GameCamera : UnityEngine.Object
{ public static GameCamera Instance; public bool IsOffScreen(Vector3 point) { return false; } }
public class MultiSceneCore : UnityEngine.Object
{
    public static MultiSceneCore Instance = new MultiSceneCore();
    public class Info { public bool IsInDoor; }
    public Info GetSubSceneInfo() { return new Info(); }
}
public class TimeOfDayController { public static TimeOfDayController Instance = new TimeOfDayController(); public bool AtNight; }
public class FakeStat { public float BaseValue; }
public class FakeModifier { public float Value; }
public class FakeGroup { public bool hasLeader; public AICharacterController LeaderAI; }
public partial class AICharacterController : MonoBehaviour
{
    public DamageReceiver searchedEnemy, cachedSearchedEnemy;
    private CharacterMainControl characterMainControl;
    public CharacterMainControl CharacterMainControl { get { return characterMainControl; } }
    public Transform aimTarget;
    public GameCamera gameCamera;
    public float updateValueTimer = 100f, reactionTime = 0.5f, baseReactionTime = 0.5f, nightReactionTimeFactor = 1f;
    public float combatTurnSpeed, patrolTurnSpeed, scatterMultiIfTargetRunning = 4f, scatterMultiIfOffScreen = 4f;
    public float scatterModifierMultiplier = 1f, forceTracePlayerDistance, combatWithTargetTimer;
    public FakeStat rotateSpeedStat;
    public FakeModifier scatterMultiplierModifier = new FakeModifier();
    public FakeGroup group;
    public AICharacterController leaderAI;
    public CharacterMainControl leader;
    public Vector3 patrolPosition;
    public bool defaultWeaponOut, weaponOut;
    public GameObject hideIfFoundEnemy;
    public void Bind(CharacterMainControl character) { characterMainControl = character; }
    public void Tick() { Update(); }
    public void TakeOutWeapon() { weaponOut = true; }
    public void SetTarget(Transform target) { aimTarget = target; }
    public void SetAimInput(Vector3 input, AimTypes type) { }
}
public class FakeItem : UnityEngine.Object { public int TypeID; }
public class FakeItemAgent : UnityEngine.Object { public FakeItem Item; }
public class InteractablePickup : Component { public FakeItemAgent ItemAgent; }
public partial class AIMainBrain
{
    private Collider[] cols = new Collider[15];
    private int dmgReceiverLayers = 1, interactLayers = 8;
    private DamageReceiver dmgReceiverTemp;
    public struct SearchTaskContext
    {
        public Vector3 searchCenter, searchDirection;
        public float searchDistance, searchAngle;
        public Teams selfTeam;
        public bool checkObsticle, thermalOn, ignoreFowBlockLayer;
        public int searchPickupID;
        public Action<DamageReceiver, InteractablePickup> onSearchFinishedCallback;
    }
    private bool CheckObsticle(Vector3 a, Vector3 b, bool thermal, bool ignore)
    { return Physics.Raycast(new Ray(a, (b-a).normalized), (b-a).magnitude, 6); }
    public void Search(SearchTaskContext context) { DoSearch(context); }
}
namespace NodeCanvas.Framework
{
    public class ActionTask<T>
    { public T agent; public bool isRunning = true; public void EndAction(bool ok) { } protected virtual void OnExecute() { } }
    public class BBParameter<T>
    {
        public Func<T> Get;
        public Action<T> Set;
        public T value { get { return Get == null ? default(T) : Get(); } set { if (Set != null) Set(value); } }
    }
}
namespace NodeCanvas.Tasks.Actions
{
    public partial class SearchEnemyAround : NodeCanvas.Framework.ActionTask<AICharacterController>
    {
        private float searchStartTimeMarker;
        private bool waitingSearchResult;
        public bool setNullIfNotFound, alwaysSuccess;
        public NodeCanvas.Framework.BBParameter<DamageReceiver> result;
        public NodeCanvas.Framework.BBParameter<InteractablePickup> pickupResult = new NodeCanvas.Framework.BBParameter<InteractablePickup>();
        public void Deliver(DamageReceiver receiver, InteractablePickup pickup) { OnSearchFinished(receiver, pickup); }
    }
    public partial class SetAim : NodeCanvas.Framework.ActionTask<AICharacterController>
    {
        public bool useTransfom = true;
        public NodeCanvas.Framework.BBParameter<Transform> aimTarget;
        public NodeCanvas.Framework.BBParameter<Vector3> aimPos;
        public void Execute() { OnExecute(); }
    }
}
namespace BossRush
{ internal sealed class SkyIslandEnemyAiMark : MonoBehaviour { } internal static partial class SkyIslandEnemyTiers { } }
