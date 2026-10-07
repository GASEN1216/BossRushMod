using System;
static class Time { internal static float time; }
namespace BossRush
{
    class Health { public bool IsDead; public CharacterMainControl Owner; public CharacterMainControl TryGetCharacter() { return Owner; } }
    class DamageReceiver { public Health health; }
    class AICharacterController { public DamageReceiver searchedEnemy; public float forceTracePlayerDistance; public bool noticed; }
    class CharacterMainControl
    {
        public static CharacterMainControl Main;
        public Teams Team; public bool IsMainCharacter; public Health Health;
        public AICharacterController aiCharacterController = new AICharacterController();
        public DamageReceiver Receiver { get { return new DamageReceiver { health = Health }; } }
        public CharacterMainControl(Teams team, bool main = false) { Team = team; IsMainCharacter = main; Health = new Health { Owner = this }; }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return aiCharacterController as T; }
    }
    class AiOwner { public AICharacterController AI; public DamageReceiver PausedTarget; public AICharacterController GetAI() { return AI; } }
    class ModBehaviour { public static ModBehaviour Instance; public bool IsModeEActive; internal static void DevLog(string text) { } }
    static class ModeHRuntimeGates { internal static bool IsModeHRunOwnerActive; }
    static class MutatorManager { internal static bool Bloodhound; internal static bool HasActiveMutator(string key) { return Bloodhound; } }
    partial class DragonDescendantAbilities
    {
        private CharacterMainControl bossCharacter, playerCharacter;
        private AiOwner aiController;
        private bool isEnraged = true;
        private float lastCollisionTime;
        private const float COLLISION_COOLDOWN = 0.5f;
        internal int CollisionSounds, Knockbacks, CollisionDamage;
        private void PlayCollisionSound() { CollisionSounds++; }
        private void ApplyKnockback(CharacterMainControl target) { Knockbacks++; }
        private void ApplyCollisionDamage(CharacterMainControl target) { CollisionDamage++; }
        internal DragonDescendantAbilities(CharacterMainControl boss, AiOwner controller) { bossCharacter = boss; aiController = controller; }
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
            var abilities = new DragonDescendantAbilities(boss, owner);
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
            Console.WriteLine("PASS ModeECombatTargeting " + checks + " assertions");
        }
    }
}
