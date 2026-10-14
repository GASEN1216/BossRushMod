using System;
using BossRush;

internal enum Teams { player, wolf }
internal static class Team { internal static bool IsEnemy(Teams a, Teams b) { return a != b; } }
internal struct DamageInfo
{
    internal CharacterMainControl fromCharacter;
    internal float damageValue;
    internal bool ignoreArmor, isFromBuffOrEffect;
    internal int fromWeaponItemID;
    internal object toDamageReceiver, damagePoint;
    internal DamageInfo(CharacterMainControl attacker)
    { this = default(DamageInfo); fromCharacter = attacker; }
}
namespace BossRush
{
    internal sealed class DragonKingAbilityController
    {
        private CharacterMainControl spawnedDescendant;
        internal bool IsInChildProtection;
        internal DragonKingAbilityController(CharacterMainControl child) { spawnedDescendant = child; }
    }
}
internal static class GChildProtection
{
    internal static void Run(Action<bool, string> check)
    {
        var original = new CharacterMainControl(); original.gameObject.activeInHierarchy = true;
        var child = new CharacterMainControl(); child.gameObject.activeInHierarchy = true; child.Health.ReviveOnce = true;
        var controller = new DragonKingAbilityController(child);
        original.Controller = controller;
        var assist = new ProductionGProtectionCase();
        assist.Run(original);
        check(child.Health.HurtCalls == 0, "G helper cannot damage a descendant before the actual owner enters child protection");
        controller.IsInChildProtection = true; child.Team = Teams.player;
        assist.Run(original);
        check(child.Health.HurtCalls == 0, "G helper never attacks a player-friendly protection reference");
        child.Team = Teams.wolf; child.Health.Invincible = true;
        assist.Run(original);
        check(child.Health.HurtCalls == 0 && child.Health.Invincible, "G helper respects original descendant invincibility rather than disabling it");
        child.Health.Invincible = false;
        assist.Run(original);
        check(child.Health.HurtCalls == 1 && !child.Health.IsDead && child.Health.CurrentHealth == 1,
            "G helper uses real Hurt and leaves the descendant's first revival outcome alive");
        assist.Run(original);
        check(child.Health.HurtCalls == 2 && child.Health.IsDead && original.Health.HurtCalls == 0,
            "G helper retries normal Hurt only on the exact live child reference and never edits the parent's phase or health");
        DamageInfo damage = child.Health.LastDamage;
        check(damage.fromCharacter == CharacterMainControl.Main && damage.toDamageReceiver == child.mainDamageReceiver
            && damage.damagePoint == child.transform.position && damage.ignoreArmor && damage.damageValue == child.Health.MaxHealth * 10,
            "G helper executes the production official DamageInfo constructor, attacker, receiver and settled hit path");
        assist.Run(original);
        check(child.Health.HurtCalls == 2 && assist.Logs.Count == 2,
            "G helper does not hit a dead protector again and logs each actual assisted hit");
    }
}
