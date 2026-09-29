using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        internal bool Destroyed;
        internal static readonly List<Object> All = new List<Object>();
        public Object() { All.Add(this); }
        public int GetInstanceID() { return _id; }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return _id; }
        public static T[] FindObjectsOfType<T>() where T : Component
        {
            List<T> result = new List<T>();
            foreach (Object value in All)
            {
                T component = value as T;
                if (component != null && component.gameObject.activeInHierarchy) result.Add(component);
            }
            return result.ToArray();
        }
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Component
        {
            List<T> result = new List<T>();
            foreach (Object value in All)
            {
                T component = value as T;
                if (component == null || component.Destroyed) continue;
                if (includeInactive || component.gameObject.activeInHierarchy) result.Add(component);
            }
            return result.ToArray();
        }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
                foreach (Component component in go.Components) component.Destroyed = true;
        }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return !Destroyed && activeSelf; } }
        internal bool FailNextDeactivate;
        public GameObject(string name = "") { }
        public SceneManagement.Scene scene = new SceneManagement.Scene { name = "Level" };
        public void SetActive(bool active)
        {
            activeSelf = active;
            if (!active && FailNextDeactivate)
            { FailNextDeactivate = false; throw new InvalidOperationException("injected deactivate callback"); }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform = new Transform();
        public Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class { return null; }
    }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static float Distance(Vector3 a, Vector3 b)
        { float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return (float)Math.Sqrt(x*x + y*y + z*z); }
    }
    public enum CursorLockMode { None, Locked }
    namespace SceneManagement
    {
        public struct Scene
        {
            public string name;
            public bool IsValid() { return name != null; }
            public bool isLoaded { get { return name != null; } }
        }
    }
    public static class Cursor { public static bool visible; public static CursorLockMode lockState; }
}

public enum Teams { player, middle, scav, wolf }
public static class Team { public static bool IsEnemy(Teams a, Teams b) { return b == Teams.wolf; } }
public interface INPCController { }
public class Health : UnityEngine.Component
{
    public bool Invincible;
    public CharacterMainControl Character;
    public CharacterMainControl TryGetCharacter() { if (Destroyed) throw new InvalidOperationException("destroyed health"); return Character; }
    public void SetInvincible(bool value) { Invincible = value; }
}
public class CharacterMainControl : UnityEngine.Component
{
    public static CharacterMainControl Main;
    public bool IsMainCharacter { get { return ReferenceEquals(Main, this); } }
    public Teams Team = Teams.player;
    public Health Health;
    public CharacterRandomPreset characterPreset;
    internal bool FailTeamCallback, FailPositionCallback;
    public CharacterMainControl()
    {
        Health = new Health();
        Health.Character = this;
        Health.gameObject.Components.Remove(Health);
        Health.gameObject = gameObject;
        gameObject.Components.Add(Health);
    }
    public void SetTeam(Teams team)
    {
        Team = team;
        if (FailTeamCallback) { FailTeamCallback = false; throw new InvalidOperationException("injected team callback"); }
    }
    public void SetPosition(UnityEngine.Vector3 position)
    {
        transform.position = position;
        if (FailPositionCallback) { FailPositionCallback = false; throw new InvalidOperationException("injected position callback"); }
    }
}
public class CharacterRandomPreset : UnityEngine.Object { }
public class CharacterSpawnerRoot : UnityEngine.Component
{
    private bool created;
    public bool Created { get { return created; } }
    public CharacterSpawnerRoot(bool alreadyCreated) { created = alreadyCreated; }
}
public class FogOfWarManager : UnityEngine.Object { private bool allVision; }
public class InputManager
{
    internal static readonly HashSet<UnityEngine.GameObject> Tokens = new HashSet<UnityEngine.GameObject>();
    public static void DisableInput(UnityEngine.GameObject source) { Tokens.Add(source); }
    public static void ActiveInput(UnityEngine.GameObject source) { Tokens.Remove(source); }
}
public class LevelManager
{
    public static LevelManager Instance;
    public InputManager InputManager = new InputManager();
    public FogOfWarManager FogOfWarManager;
    public CharacterMainControl ControllingCharacter;
}
public class GameCamera
{
    public static GameCamera Instance;
    public CharacterMainControl target;
    public void SetTarget(CharacterMainControl value) { target = value; }
}
namespace BossRush
{
    internal static class ModBehaviour { public static void DevLog(string message) { } }
    internal static class PetNestCompanionAgent { public static bool IsCompanionCharacter(CharacterMainControl value) { return false; } }
    internal static class ObjectCache
    {
        internal static CharacterSpawnerRoot[] Roots = new CharacterSpawnerRoot[0];
        internal static CharacterSpawnerRoot[] GetCharacterSpawnerRoots() { return Roots; }
    }
    internal class ModeHSupportedMap
    {
        public UnityEngine.Vector3[] ArenaSpawnPoints = new[] { new UnityEngine.Vector3(0, 0, 0) };
        public UnityEngine.Vector3 ArenaCenter, StagingPos = new UnityEngine.Vector3(200, 0, 0), SpectatorPos;
    }
    internal static class ModeHMapSupportRegistry
    {
        public const float MinStagingIsolationDistance = 100;
        public static bool TryGetMap(string name, out ModeHSupportedMap map) { map = new ModeHSupportedMap(); return true; }
    }
    internal static class Program
    {
        private static int _checks;
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); _checks++; }
        private static void Reset()
        {
            UnityEngine.Object.All.Clear(); InputManager.Tokens.Clear();
            CharacterMainControl.Main = new CharacterMainControl();
            CharacterMainControl.Main.SetPosition(new UnityEngine.Vector3(7, 0, 8));
            LevelManager.Instance = new LevelManager { ControllingCharacter = CharacterMainControl.Main };
            ObjectCache.Roots = new CharacterSpawnerRoot[0];
            UnityEngine.Cursor.visible = false; UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
        }
        private static void ArenaPartialRollback()
        {
            Reset();
            var first = new CharacterSpawnerRoot(false);
            var second = new CharacterSpawnerRoot(true);
            second.gameObject.FailNextDeactivate = true;
            ObjectCache.Roots = new[] { first, second };
            var lease = new ModeHArenaIsolationLease(); string reason;
            Check(!lease.TryAcquire("arena", 1, 17, out reason), "partial freeze must reject acquisition");
            Check(first.gameObject.activeSelf && !first.Created, "completed prior spawner must restore original active and created");
            Check(second.gameObject.activeSelf && second.Created, "throwing spawner must restore its original active and created");
            Check(lease.FrozenSpawnerCount == 0 && !lease.IsActive, "failed lease must release registrations");
            lease.Release(1);
        }
        private static void LateSpawnerRollback()
        {
            Reset(); var lease = new ModeHArenaIsolationLease(); string reason;
            Check(lease.TryAcquire("arena", 1, 18, out reason), "empty arena can acquire");
            var created = new CharacterSpawnerRoot(true);
            var notCreated = new CharacterSpawnerRoot(false);
            notCreated.gameObject.SetActive(false);
            ObjectCache.Roots = new[] { created, notCreated };
            Check(!lease.CheckLateSpawners(out reason) && reason == "isolation_late_spawner", "late spawners must reject isolation");
            Check(!created.gameObject.activeSelf && created.Created && notCreated.Created, "late spawners freeze immediately");
            lease.Release(1); lease.Release(1);
            Check(created.Created && created.gameObject.activeSelf, "already created late spawner must remain created after release");
            Check(!notCreated.Created && !notCreated.gameObject.activeSelf, "uncreated inactive late spawner must restore exact state");
        }
        private static void SpectatorCallbackRollback(bool teamFailure)
        {
            Reset(); CharacterMainControl player = CharacterMainControl.Main;
            player.FailTeamCallback = teamFailure; player.FailPositionCallback = !teamFailure;
            var lease = new ModeHSpectatorLease(); string reason;
            Check(!lease.TryAcquire(new UnityEngine.Vector3(100, 0, 100), 1, 19, out reason), "host callback failure must reject spectator lease");
            Check(player.Team == Teams.player && !player.Health.Invincible, "failed acquisition restores player team and invincibility");
            Check(UnityEngine.Vector3.Distance(player.transform.position, new UnityEngine.Vector3(7, 0, 8)) == 0,
                "write then callback failure must restore original position");
            Check(InputManager.Tokens.Count == 0 && !lease.IsActive, "failed spectator lease must release input token");
            lease.Release(1);
        }
        private static void SceneDestructionRelease()
        {
            Reset(); var spectator = new ModeHSpectatorLease(); string reason;
            Check(spectator.TryAcquire(new UnityEngine.Vector3(100, 0, 100), 1, 20, out reason), "spectator acquires");
            CharacterMainControl oldPlayer = CharacterMainControl.Main;
            UnityEngine.Object.Destroy(oldPlayer.gameObject);
            Check(oldPlayer == null && oldPlayer.Health == null, "scene destroy must cascade to components and emulate Unity null");
            CharacterMainControl.Main = new CharacterMainControl();
            spectator.Release(2); spectator.Release(2);
            Check(CharacterMainControl.Main.Team == Teams.player && !CharacterMainControl.Main.Health.Invincible,
                "new scene player must not inherit old lease writes");
            Check(InputManager.Tokens.Count == 0 && !UnityEngine.Cursor.visible, "scene release clears surviving input token and cursor");
        }
        private static void DormantNativeEnemyCleared()
        {
            // 官方原生角色离玩家 100 m 外是休眠（inactive）的；清场必须连它们一起清，否则上看台后会就近苏醒闯进擂台
            Reset();
            var dormant = new CharacterMainControl { Team = Teams.wolf };
            dormant.gameObject.SetActive(false);
            var awake = new CharacterMainControl { Team = Teams.wolf };
            var resident = new CharacterMainControl { Team = Teams.wolf };
            resident.gameObject.scene = new UnityEngine.SceneManagement.Scene { name = "DontDestroyOnLoad" };
            var lease = new ModeHArenaIsolationLease(); string reason;
            Check(lease.TryAcquire("arena", 1, 21, out reason), "arena with native enemies acquires");
            Check(awake == null, "active native enemy is cleared");
            Check(dormant == null, "dormant (inactive) native enemy must be cleared too");
            Check(resident != null, "DontDestroyOnLoad characters are not part of the level and must be kept");
            Check(CharacterMainControl.Main != null, "player is never cleared");
            lease.Release(1);
        }
        private static void GenerationChangeSurvivingPlayer()
        {
            // 附加场景加载会推进代次但玩家身体仍在：阵营与无敌必须还原，位置不还原（旧坐标属于旧场景）
            Reset(); var spectator = new ModeHSpectatorLease(); string reason;
            CharacterMainControl player = CharacterMainControl.Main;
            Check(spectator.TryAcquire(new UnityEngine.Vector3(100, 0, 100), 1, 22, out reason), "spectator acquires");
            Check(player.Team != Teams.player || player.Health.Invincible, "lease wrote spectator protection");
            spectator.Release(2);
            Check(player.Team == Teams.player && !player.Health.Invincible,
                "surviving player must get team and invincibility back even when generation changed");
            Check(UnityEngine.Vector3.Distance(player.transform.position, new UnityEngine.Vector3(100, 0, 100)) == 0,
                "position must not be restored across generations");
        }
        private static int RegistryCount(string name)
        {
            return ((HashSet<int>)typeof(ModeHDeathSuppressionRegistry).GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null)).Count;
        }
        private static void DestroyedSuppressionRelease()
        {
            Reset(); ModeHDeathSuppressionRegistry.ResetStaticCaches();
            var character = new CharacterMainControl { characterPreset = new CharacterRandomPreset() };
            Health health = character.Health;
            ModeHDeathSuppressionRegistry.RegisterPreset(character.characterPreset);
            ModeHDeathSuppressionRegistry.RegisterCharacter(health, character);
            Check(ModeHDeathSuppressionRegistry.IsModeHOnDeadSuppressionActive(health), "owned live character is suppressed");
            UnityEngine.Object.Destroy(character.gameObject);
            Check(character == null && health == null, "scene unload destroys both registered Unity references");
            ModeHDeathSuppressionRegistry.UnregisterCharacter(health, character);
            Check(RegistryCount("_healthIds") == 0 && RegistryCount("_characterIds") == 0,
                "destroyed Unity references must still release both instance IDs");
            ModeHDeathSuppressionRegistry.UnregisterPreset(character.characterPreset);
            Check(!ModeHDeathSuppressionRegistry.IsSuppressionArmed, "last preset releases suppression fast gate");
            var partial = new CharacterMainControl();
            ModeHDeathSuppressionRegistry.RegisterCharacter(null, partial);
            ModeHDeathSuppressionRegistry.UnregisterCharacter(null, partial);
            Check(RegistryCount("_characterIds") == 0, "failed spawn without health releases character registration");
        }
        public static void Main()
        {
            ArenaPartialRollback(); LateSpawnerRollback(); SpectatorCallbackRollback(true);
            SpectatorCallbackRollback(false); SceneDestructionRelease(); DestroyedSuppressionRelease();
            DormantNativeEnemyCleared(); GenerationChangeSurvivingPlayer();
            Console.WriteLine("PASS ModeHCombatRelease: " + _checks + " assertions");
        }
    }
}
