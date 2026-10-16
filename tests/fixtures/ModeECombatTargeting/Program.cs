using System;
static class Time { internal static float time; }
namespace BossRush
{
    struct Vector3 { internal float x; }
    class Transform { internal Vector3 position; }
    class Health { public bool IsDead; public CharacterMainControl Owner; public CharacterMainControl TryGetCharacter() { return Owner; } }
    class DamageReceiver { public Health health; }
    class AICharacterController { public DamageReceiver searchedEnemy; public float forceTracePlayerDistance; public bool noticed; }
    class CharacterMainControl
    {
        public static CharacterMainControl Main;
        internal readonly Transform transform = new Transform();
        public Teams Team; public bool IsMainCharacter; public Health Health;
        public AICharacterController aiCharacterController = new AICharacterController();
        internal CoopAiPredictionReplicaMarker Marker;
        public T GetComponent<T>() where T : class { return Marker as T; }
        public DamageReceiver Receiver { get { return new DamageReceiver { health = Health }; } }
        public CharacterMainControl(Teams team, bool main = false) { Team = team; IsMainCharacter = main; Health = new Health { Owner = this }; }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return aiCharacterController as T; }
    }
    class AiOwner { public AICharacterController AI; public DamageReceiver PausedTarget; public AICharacterController GetAI() { return AI; } }
    class ModBehaviour { public static ModBehaviour Instance; public bool IsModeEActive; internal static void DevLog(string text) { } }
    static class ModeHRuntimeGates { internal static bool IsModeHRunOwnerActive; }
    static class MutatorManager { internal static bool Bloodhound; internal static bool HasActiveMutator(string key) { return Bloodhound; } }
    // Mutable host seams; HasEntityAuthority and the controller property are extracted from production.
    static class BossRushCoop
    {
        internal static bool Connected, Host;
        internal static bool IsConnected { get { return Connected; } }
        internal static readonly System.Collections.Generic.List<CharacterMainControl> Players = new System.Collections.Generic.List<CharacterMainControl>();
        internal static CharacterMainControl GetCombatTarget(Vector3 at)
        {
            CharacterMainControl best = null; float distance = float.MaxValue;
            foreach (CharacterMainControl p in Players)
            {
                if (p == null || p.Health == null || p.Health.IsDead) continue;
                float d = Math.Abs(p.transform.position.x - at.x);
                if (d < distance) { distance = d; best = p; }
            }
            return best;
        }
        internal static string RunId;
        internal static bool IsHost { get { return Connected && Host; } }
        internal static bool HasWorldAuthority { get { return !Connected || IsHost; } }
    }
    class CoopAiPredictionReplicaMarker { internal bool Replica, AuthorityBound; internal string AuthorityRun; }
    partial class DragonDescendantAbilityController
    {
        private CharacterMainControl bossCharacter, playerCharacter;
        private readonly Transform transform = new Transform();
        private AiOwner aiController;
        private bool isEnraged = true;
        private float lastCollisionTime;
        private const float COLLISION_COOLDOWN = 0.5f;
        internal int CollisionSounds, Knockbacks, CollisionDamage;
        private void PlayCollisionSound() { CollisionSounds++; }
        private void ApplyKnockback(CharacterMainControl target) { Knockbacks++; }
        private void ApplyCollisionDamage(CharacterMainControl target) { CollisionDamage++; }
        internal DragonDescendantAbilityController(CharacterMainControl boss, AiOwner controller) { bossCharacter = boss; aiController = controller; }
        internal CharacterMainControl Resolve() { RefreshPlayerReference(); return IsPlayerAlly() ? null : playerCharacter; }
    }
    static class Program
    {
        static int checks;
        static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        static void Main()
        {
            ModBehaviour.Instance = new ModBehaviour { IsModeEActive = true };
            var player = CharacterMainControl.Main = new CharacterMainControl(Teams.bear, true);
            var boss = new CharacterMainControl(Teams.bear);
            var enemy = new CharacterMainControl(Teams.wolf);
            var owner = new AiOwner { AI = boss.aiCharacterController };
            var abilities = new DragonDescendantAbilityController(boss, owner);
            owner.AI.searchedEnemy = enemy.Receiver;
            Check(abilities.Resolve() == enemy, "allied descendant casts at its actual enemy instead of disabling all skills");
            Time.time = 10;
            abilities.OnCollisionWithPlayer(player);
            Check(abilities.CollisionSounds == 0 && abilities.Knockbacks == 0 && abilities.CollisionDamage == 0,
                "allied player collision is rejected before sound, knockback and damage while AI attacks an enemy");
            owner.AI.searchedEnemy = player.Receiver;
            Check(abilities.Resolve() == null, "allied descendant rejects its player teammate");
            boss.Team = Teams.wolf;
            Check(abilities.Resolve() == player, "hostile descendant can attack a player actually selected by AI");
            abilities.OnCollisionWithPlayer(player);
            Check(abilities.Knockbacks == 1 && abilities.CollisionDamage == 1,
                "hostile collision at the same timestamp is not blocked by an allied collision consuming cooldown");
            owner.AI.searchedEnemy = null;
            Check(abilities.Resolve() == null, "Mode E cannot invent a player target when AI has none");
            owner.PausedTarget = player.Receiver;
            Check(abilities.Resolve() == player, "paused skill preserves the validated original target");
            ModeHRuntimeGates.IsModeHRunOwnerActive = true;
            Check(abilities.Resolve() == null, "Mode H always excludes the spectator even if an AI target was injected");
            Time.time = 11;
            abilities.OnCollisionWithPlayer(player);
            Check(abilities.Knockbacks == 1 && abilities.CollisionDamage == 1,
                "Mode H spectator collision never applies knockback or damage");
            owner.PausedTarget = null; boss.Team = Teams.scav; owner.AI.searchedEnemy = enemy.Receiver;
            Check(abilities.Resolve() == enemy, "Mode H still fights the opposing participant");
            enemy.Health.IsDead = true;
            Check(abilities.Resolve() == null, "dead target cannot receive a skill");
            ModeHRuntimeGates.IsModeHRunOwnerActive = false; ModBehaviour.Instance.IsModeEActive = false;
            Check(abilities.Resolve() == player, "ordinary BossRush preserves direct-player targeting");
            BossRushCoop.Connected = true; BossRushCoop.Host = true;
            BossRushCoop.Players.Add(player);
            var guest = new CharacterMainControl(Teams.bear); guest.transform.position = new Vector3 { x = 100 };
            BossRushCoop.Players.Add(guest); boss.transform.position = new Vector3 { x = 99 };
            Check(abilities.Resolve() == guest, "non-arena production refresh selects actual nearest guest");
            guest.Health.IsDead = true; Check(abilities.Resolve() == player, "dead guest does not replace actual live target");
            BossRushCoop.Connected = false;
            MutatorManager.Bloodhound = true; boss.Team = Teams.bear; boss.aiCharacterController.forceTracePlayerDistance = 99999f;
            BloodhoundSetup.Apply(boss, false, Teams.bear, Teams.bear);
            Check(boss.aiCharacterController.forceTracePlayerDistance == 0, "Mode E allied bloodhound clears shared forced-player tracking");
            BloodhoundSetup.Apply(boss, false, Teams.wolf, Teams.bear);
            Check(boss.aiCharacterController.forceTracePlayerDistance == 99999f, "hostile bloodhound keeps its intended chase");
            BloodhoundSetup.Apply(boss, true, Teams.bear, Teams.bear);
            Check(boss.aiCharacterController.forceTracePlayerDistance == 99999f, "Mode F chase behavior stays intact");
            MutatorManager.Bloodhound = false;
            BloodhoundSetup.Apply(boss, false, Teams.wolf, Teams.bear);
            Check(boss.aiCharacterController.forceTracePlayerDistance == 0, "ordinary faction AI has no forced player chase");
            boss.Team = Teams.wolf;
            foreach (string boundary in new[] { "stop", "room", "role", "replica" })
            {
                BossRushCoop.Connected = true; BossRushCoop.Host = boundary != "replica"; BossRushCoop.RunId = "original";
                boss.Marker = new CoopAiPredictionReplicaMarker { AuthorityBound = true, AuthorityRun = "original", Replica = boundary == "replica" };
                Check(abilities.HasCombatAuthority == (boundary != "replica"), "production original entity admission " + boundary);
                if (boundary == "stop" || boundary == "replica") BossRushCoop.Connected = false;
                else if (boundary == "room") BossRushCoop.RunId = "successor";
                else BossRushCoop.Host = false;
                int sounds = abilities.CollisionSounds, pushes = abilities.Knockbacks, hits = abilities.CollisionDamage;
                Time.time += 1;
                abilities.OnCollisionWithPlayer(player);
                Check(!abilities.HasCombatAuthority && abilities.CollisionSounds == sounds && abilities.Knockbacks == pushes && abilities.CollisionDamage == hits,
                    "collision refuses old entity after " + boundary);
            }
            BossRushCoop.Connected = false; BossRushCoop.Host = false; BossRushCoop.RunId = null;
            var fresh = new DragonDescendantAbilityController(new CharacterMainControl(Teams.wolf), owner);
            fresh.OnCollisionWithPlayer(player);
            Check(fresh.HasCombatAuthority && fresh.Knockbacks == 1 && fresh.CollisionDamage == 1, "fresh solo collision keeps gameplay");
            Console.WriteLine("PASS ModeECombatTargeting " + checks + " assertions");
        }
    }
}
