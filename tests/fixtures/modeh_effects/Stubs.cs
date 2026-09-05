// Host/state boundaries only. The effect system, adapters, command controller and
// two combat entry methods are compiled from production source by run.py.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } }
    public struct Vector3 { public float x, y, z; }
    public static class Time { public static float time; }
    public static class Mathf
    {
        public static float Abs(float a) { return Math.Abs(a); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
}
public class DamageReceiver { }
public class AICharacterController
{
    public float skillSuccessChance = 0.4f, itemSkillChance = 0.5f, itemSkillCoolTime = 10f;
    public float sightDistance = 100f, sightAngle = 120f, combatTurnSpeed = 10f, patrolTurnSpeed = 10f;
    public float baseReactionTime = 0.5f, nextReleaseSkillTimeMarker;
    public bool shootCanMove = true, noticed;
    public UnityEngine.Vector2 skillCoolTimeRange = new UnityEngine.Vector2(5f, 10f);
    public DamageReceiver searchedEnemy;
    public int Moves;
    public void MoveToPos(UnityEngine.Vector3 value) { Moves++; }
    public void SetNoticedToTarget(DamageReceiver target) { noticed = target != null; }
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        public static void CriticalLog(string text) { }
        public static void DevLog(string text) { }
    }
    internal static class ModeHConfig
    {
        public const int CowardCrowdEnemyThreshold = 3, MinUsableInjuriesPerKey = 3, MaxScarsPerProfile = 3;
        public const int ScarDeclineFameGain = 1, MaxFameDisplayCount = 99, SpiritInjuryEnemyThreshold = 2, BellUsesPerMatch = 1;
        public const float CommandWindowSeconds = 6f, CommandReassertIntervalSeconds = 0.1f, MatchDurationSeconds = 180f;
        public const float OldWoundTriggerHealthFraction = 0.35f, SpiritInjuryCommandScale = 0.85f;
        public const string LocalizationKeyPrefix = "BossRush_ModeH_";
    }
    internal static class ModeHContentCatalog
    {
        public static List<ModeHScarSpec> Scars;
        public static List<ModeHInjurySpec> Injuries;
        public static List<ModeHCommandSpec> Commands;
    }
    internal static class ModeHCommandCompatibilityRegistry
    {
        public static bool Verified = true;
        public static bool HasVerifiedBehavior(string key, string effect) { return Verified; }
        public static bool IsCommandSelectable(string key, string command) { return Verified; }
    }
    internal static class ModeHStableIds { public static string[] AllCommonCommands = { "center", "press" }; }
    internal sealed class ModeHSeedStream
    {
        public static class Domains { public const string Scar = "scar"; }
        public static ModeHSeedStream Create(long seed, string domain, int index) { return new ModeHSeedStream(); }
        public int NextInt(int count) { return 0; }
    }
    internal enum ModeHParticipantStatus { Available, Injured, Retired }
    internal sealed class ModeHProfileDto
    {
        public string profileId, stableKey, injuryId, archetypeId, anomalyId;
        public int status, fameDisplayCount;
        public List<string> scarIds;
    }
    internal sealed class ModeHInjuryEventDto
    {
        public string profileId, downToken, injuryId;
        public int eventSequence;
        public bool retired;
    }
    internal sealed class ModeHScarWindowStateDto { public string scarId; public float remainingSeconds; }
    internal sealed class ModeHParticipantRef { public AICharacterController Character; public bool IsRelay; }
    internal sealed class ModeHBattleSnapshotContext { }
    internal enum ModeHSnapshotTrigger { BellCommitted }
    internal sealed class ModeHFixtureTelemetry
    {
        public bool RelayConsumed;
        public void OnFighterEntered(ModeHParticipantRef fighter) { }
    }
    internal sealed class ModeHFixtureRunState { public long OwnerToken = 7; }
    internal sealed partial class ModeHCombatControl
    {
        private ModeHParticipantRef _activeFighter;
        private string _activeProfileId, _activeStableKey, _activeAnomalyId;
        private object _activeFighterArmorItem;
        private AICharacterController _activeAi;
        private int _matchIndex = 1;
        private readonly ModeHFixtureTelemetry _telemetry = new ModeHFixtureTelemetry();
        private readonly ModeHCommandFireContext _fireContext = new ModeHCommandFireContext();
        private readonly ModeHInjuryAndScarSystem _injuryAndScar = new ModeHInjuryAndScarSystem();
        private readonly ModeHCommandController _commandController = new ModeHCommandController();
        private readonly ModeHFixtureRunState _runState = new ModeHFixtureRunState();
        public string ArenaCondition;
        public int EnemyCount = 2, RefreshCount, SnapshotCount;
        public ModeHInjuryAndScarSystem Effects { get { return _injuryAndScar; } }
        public ModeHCommandController Commands { get { return _commandController; } }
        public ModeHCommandFireContext Context { get { return _fireContext; } }
        public void SetOwner(long token) { _runState.OwnerToken = token; }
        private static object ResolveArmorItem(AICharacterController character) { return null; }
        private static AICharacterController ResolveAi(AICharacterController character) { return character; }
        private void RefreshFireContext(float dt, bool force)
        {
            RefreshCount++;
            RefreshEffectConditionInputs();
        }
        private void RefreshEffectConditionInputs()
        {
            _fireContext.ArenaConditionId = ArenaCondition;
            _fireContext.ActiveFighterIsRelay = _activeFighter != null && _activeFighter.IsRelay;
            _fireContext.BellConsumed = _commandController.BellConsumed;
            _fireContext.EnemyCount = EnemyCount;
        }
        private void CaptureSnapshot(ModeHSnapshotTrigger trigger, ModeHBattleSnapshotContext context) { SnapshotCount++; }
    }
}
