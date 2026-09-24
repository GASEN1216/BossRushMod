using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2 { }
    public struct Vector3 { }
    public sealed class GameObject { public bool activeInHierarchy; }
}

public enum Teams { player, middle, scav, wolf }
public static class Team
{
    public static bool IsEnemy(Teams a, Teams b) { return a != Teams.middle && b != Teams.middle && a != b; }
}
public sealed class Health { public bool IsDead; }
public sealed class DamageReceiver { public Health health; public Teams Team; }
public sealed class CharacterMainControl
{
    public Health Health;
    public Teams Team;
    public UnityEngine.GameObject gameObject;
    public AICharacterController Ai;
}
public sealed class LevelManager
{
    public static LevelManager Instance;
    public CharacterMainControl ControllingCharacter;
}
public sealed class CharacterRandomPreset
{
    public bool isBoss, isVehicle, showName;
    public Teams team;
    public string nameKey;
    public List<object> specialAttachmentBases;
}
public sealed class AICharacterController
{
    public float skillSuccessChance, itemSkillChance, itemSkillCoolTime, sightDistance, sightAngle,
        combatTurnSpeed, patrolTurnSpeed, baseReactionTime, nextReleaseSkillTimeMarker;
    public bool shootCanMove, noticed;
    public UnityEngine.Vector2 skillCoolTimeRange;
    public DamageReceiver searchedEnemy, LastNotice;
    public void SetNoticedToTarget(DamageReceiver target) { LastNotice = target; }
    public void MoveToPos(UnityEngine.Vector3 pos) { }
}
namespace BossRush
{
    public partial class ModBehaviour
    {
        public static string Root;
        public static string GetModPath() { return Root; }
        public static void DevLog(string text) { }
        public static void CriticalLog(string key, string text) { }
    }
    public static class ModeHPresetRegistry
    {
        public static ModeHProductionCertificationDto Installed;
        public static void MaterializeFromReport(ModeHProductionCertificationDto report) { Installed = report; }
    }
    partial class ReleaseCatalog
    {
        private readonly Dictionary<string, ModeHPresetCertificationRecordDto> _records = new Dictionary<string, ModeHPresetCertificationRecordDto>();
        private ModeHProductionCertificationDto _report;
        private string _lastError;
        public ModeHProductionCertificationDto Report { get { return _report; } }
        private static CharacterRandomPreset ResolveAuditedPreset(string key)
        {
            return new CharacterRandomPreset { isBoss = true, showName = true, team = Teams.scav, nameKey = key };
        }
        public static bool Supports(string key) { return IsReleaseControlPointAvailable(key); }
    }
    partial class ArenaWake
    {
        private static AICharacterController ResolveAi(CharacterMainControl character) { return character.Ai; }
        public static void Wake(CharacterMainControl character, DamageReceiver target) { WakeArenaOpponent(character, target); }
    }
    partial class BellLease
    {
        public bool IsActive;
        private bool _bellAccepting;
        public bool Accepting { get { return IsActive && _bellAccepting; } }
    }
    partial class CommandDescriptions
    {
        internal static string Describe(ModeHCommandSpec spec, ModeHProfileDto profile)
        { return DescribeCommand(spec, profile, null); }
    }
    internal static class Program
    {
        internal static int _assertions;
        internal static void Check(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new Exception("FAIL: " + message);
        }
        public static void Main(string[] args)
        {
            ModBehaviour.Root = args[0];
            Check(ModeHProfileRegistry.EnsureValidated(), "production profile data loads");
            var catalog = new ReleaseCatalog();
            Check(catalog.TryUseReleaseCatalog(), "fresh entry builds complete release report synchronously");
            Check(catalog.Report.overallPassed && ModeHPresetRegistry.Installed == catalog.Report, "release pool materialized");
            Check(catalog.Report.passedStableKeys.Count >= ModeHConfig.MinProductionCandidateCount, "minimum pool preserved");
            foreach (var record in catalog.Report.records)
            {
                Check(record.durationMs == 0 && record.spawnTimelineDigest == "release_contract_v1", "static record cannot masquerade as live measurement");
                Check(ModeHCommandCompatibilityRegistry.MeetsCommandGate(record.stableKey), "each fighter has playable commands");
                Check(ModeHCommandCompatibilityRegistry.GetCommandStatus(record.stableKey, "finish") == ModeHCommandCompatibilityStatus.ReleaseSupported, "action support retains evidence tier");
            }
            string key = catalog.Report.passedStableKeys[0];
            ModeHCommandSpec hold = ModeHContentCatalog.Commands.Find(x => x.CommandId == "hold");
            Check(hold != null, "hold command exists in shipped data");
            var profile = new ModeHProfileDto { stableKey = key };
            string description = CommandDescriptions.Describe(hold, profile);
            Check(description.Contains("技能施放概率") && description.Contains("物品技能概率"),
                "ordinary release commands show their supported effect details");
            Check(ModeHCommandCompatibilityRegistry.GetVerifiedEffectIds(key, "hold").Count == hold.Effects.Count,
                "release effects remain visible to candidate descriptions");
            Check(ModeHCommandCompatibilityRegistry.GetBehaviorEntryStatus(key, "leg") == ModeHCommandCompatibilityStatus.ReleaseSupported, "injury retains release support tier");
            Check(ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(key, "leg"), "release injury remains usable");
            foreach (var injury in ModeHContentCatalog.Injuries)
                Check(ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(key, injury.InjuryId), "all shipped injuries supported: " + injury.InjuryId);
            foreach (var scar in ModeHContentCatalog.Scars)
                Check(ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(key, scar.ScarId), "all shipped scars supported: " + scar.ScarId);
            ModeHCommandCompatibilityRegistry.ResetStaticCaches();
            ModeHCommandCompatibilityRegistry.RestoreCertificationEffects(catalog.Report.records);
            Check(ModeHCommandCompatibilityRegistry.IsCommandSelectable(key, "finish"), "release status roundtrip selectable");
            Check(ModeHStateModel.ToCompatibilityStatus(6) == ModeHCommandCompatibilityStatus.ReleaseSupported, "appended status accepted");
            Check(ModeHStateModel.ToCompatibilityStatus(7) == ModeHCommandCompatibilityStatus.Unknown, "unknown future status rejected");
            Check(!ReleaseCatalog.Supports("inventedControlPoint"), "unknown API refused");

            var actor = new CharacterMainControl { Health = new Health(), Team = Teams.scav,
                gameObject = new UnityEngine.GameObject { activeInHierarchy = true }, Ai = new AICharacterController() };
            var enemy = new DamageReceiver { health = new Health(), Team = Teams.wolf };
            ArenaWake.Wake(actor, enemy);
            Check(actor.Ai.searchedEnemy == enemy && actor.Ai.noticed && actor.Ai.LastNotice == enemy, "idle fighter learns opponent and notice");
            var second = new DamageReceiver { health = new Health(), Team = Teams.wolf };
            ArenaWake.Wake(actor, second);
            Check(actor.Ai.searchedEnemy == enemy, "valid command target preserved");
            enemy.health.IsDead = true;
            ArenaWake.Wake(actor, second);
            Check(actor.Ai.searchedEnemy == second, "dead target replaced");
            actor.Ai.searchedEnemy = null;
            LevelManager.Instance = new LevelManager { ControllingCharacter = actor };
            ArenaWake.Wake(actor, second);
            Check(actor.Ai.searchedEnemy == null, "ERROR controlled fighter not given AI target");
            LevelManager.Instance.ControllingCharacter = null;
            second.Team = Teams.middle;
            ArenaWake.Wake(actor, second);
            Check(actor.Ai.searchedEnemy == null, "neutral spectator excluded");
            second.Team = Teams.wolf;
            actor.gameObject.activeInHierarchy = false;
            ArenaWake.Wake(actor, second);
            Check(actor.Ai.searchedEnemy == null, "inactive staging actor excluded");

            var bell = new BellLease { IsActive = true };
            for (int match = 0; match < 6; match++)
            {
                bell.StartAcceptingBell();
                Check(bell.Accepting, "bell opens each match");
                bell.StopAcceptingBell();
                Check(!bell.Accepting, "bell closes at settlement");
            }
            bell.IsActive = false;
            bell.StartAcceptingBell();
            Check(!bell.Accepting, "released lease cannot reopen");
            FlowRegression.Run();
            Console.WriteLine("PASS ModeHPlayerFlow: " + _assertions + " assertions");
        }
    }
}
