// CombatControl methods are extracted byte-for-byte at build time; telemetry and seed streams are production classes.
// Unity actions, presentation and command effects below are host boundaries, not substitutes for terminal decisions.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal enum Teams { player, scav, middle }
    internal enum ModeHSnapshotTrigger { DownOrRelay, Interval }
    internal sealed class ModeHBattleSnapshotContext { }
    internal sealed class AuditSnapshot { internal bool TickInterval(float delta) { return false; } }
    internal sealed class AuditCommand
    {
        internal string ActiveCommandId;
        internal void Tick(float delta, Vector3 center, object nearest, object lowest, int enemies) { }
        internal void RestoreAll() { ActiveCommandId = null; }
    }
    internal sealed class AuditInjury
    {
        internal void Tick(float delta, AuditFireContext context) { }
        internal void RestoreAll() { }
    }
    internal sealed class AuditFireContext
    { internal Vector3 ArenaCenter; internal object NearestEnemy, LowestHealthEnemy; internal int EnemyCount; }
    internal sealed class AuditPerformer { internal void Stop() { } }
    internal static class ModeHRuntimeGates { internal static void SetStandInActive(bool value, int id) { } }
    internal static class ModeHEventRouter { internal static void SetErrorSwapControlledParticipant(ModeHParticipantRef value) { } }
    internal static partial class ModeHCommandCompatibilityRegistry
    { internal static bool HasVerifiedAnomalyBehavior(string key, string anomaly) { return true; } }
    internal sealed partial class Health
    {
        public bool Invincible;
        public void SetInvincible(bool value) { Invincible = value; }
    }
    internal sealed partial class CharacterMainControl
    {
        public CA_ControlOtherCharacter ControlOtherCharacterAction;
        public Teams Team;
        public int HeldWeapon = -1, SavedWeapon = -1;
        public void SetTeam(Teams value) { Team = value; }
        public void SetPosition(Vector3 value) { transform.position = value; }
        public void SwitchToWeaponBeforeUse()
        {
            // Official SwitchToWeaponBeforeUse unconditionally clears the saved slot, even if action gating rejected the swap.
            if (ControlOtherCharacterAction == null || !ControlOtherCharacterAction.Running) HeldWeapon = SavedWeapon;
            SavedWeapon = -1;
        }
    }
    internal sealed class CA_ControlOtherCharacter
    {
        public bool Running;
        public CharacterMainControl targetCharacter, Owner;
        public int StopCount;
        public bool StopAction()
        {
            if (!Running) return true;
            Running = false; StopCount++;
            LevelManager.Instance.SetControllingCharacter(Owner);
            return true;
        }
    }
    internal sealed class LevelManager
    {
        public static LevelManager Instance;
        public CharacterMainControl Main, ControllingCharacter;
        public void SetControllingCharacter(CharacterMainControl character)
        {
            // Official LevelManager restores the weapon before publishing the new controlling character.
            if (ReferenceEquals(character, Main)) character.SwitchToWeaponBeforeUse();
            ControllingCharacter = character;
        }
    }
    internal sealed partial class ModeHCombatControl
    {
        private readonly AuditCommand _commandController = new AuditCommand();
        private readonly AuditInjury _injuryAndScar = new AuditInjury();
        private readonly AuditFireContext _fireContext = new AuditFireContext();
        private readonly AuditSnapshot _snapshot = new AuditSnapshot();
        private readonly ModeHMatchRules _matchRules = new ModeHMatchRules();
        private readonly HashSet<string> _cowardChecksDone = new HashSet<string>(StringComparer.Ordinal);
        private readonly AuditPerformer _standInPerformer = new AuditPerformer();
        private ModeHCombatTelemetry _telemetry;
        private ModeHParticipantRef _activeFighter, _relayFighter, _controlledParticipant;
        private string _activeProfileId, _activeStableKey, _activeAnomalyId;
        private int _matchIndex, _entryBatchIndex, _lastEntryBatchIndex;
        private long _runSeed;
        private bool _relayWindowOpen, _enemySpawningPending;
        private ModeHErrorSwapPhase _swapPhase;
        private CharacterMainControl _playerBody, _controlledFighter;
        private bool _playerStateCaptured, _playerOriginalInvincible;
        private Teams _playerOriginalTeam;
        private Vector3 _playerOriginalPosition;
        private float _swapDeadlineRemaining;
        private void RefreshFireContext(float delta, bool force) { }
        private void TickErrorSwap(float delta) { }
        private void EvaluateTriggeredInjuries() { }
        private void TryEvaluateErrorTrigger() { }
        private void CaptureSnapshot(ModeHSnapshotTrigger trigger, ModeHBattleSnapshotContext context) { }
        private void RestoreAll() { RestoreErrorSwap(); _relayWindowOpen = false; }
        internal void Configure(ModeHCombatTelemetry telemetry, ModeHParticipantRef fighter,
            ModeHParticipantRef relay, long seed)
        {
            _telemetry = telemetry; _activeFighter = fighter; _relayFighter = relay;
            _activeProfileId = fighter.ProfileId; _activeStableKey = fighter.StableKey;
            _activeAnomalyId = ModeHStableIds.AnomalyCowardBlood; _matchIndex = 1; _runSeed = seed;
        }
        internal bool RelayOpen { get { return _relayWindowOpen; } }
        internal int CowardChecks { get { return _cowardChecksDone.Count; } }
        internal void ConfigureSwap(CharacterMainControl body, CharacterMainControl fighter)
        {
            _playerBody = body; _controlledFighter = fighter; _swapPhase = ModeHErrorSwapPhase.Active;
            _playerStateCaptured = true; _playerOriginalTeam = Teams.middle; _playerOriginalInvincible = true;
        }
    }

    internal static class CombatControlAudit
    {
        private static int _checks;
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); _checks++; }
        internal static void Run()
        {
            long seed = 0;
            while (!ModeHSeedStream.Create(seed, ModeHSeedStream.Domains.Coward,
                10 + ModeHStableIds.AnomalyCowardBlood.Length).NextChance(ModeHConfig.CowardBloodBaseChance)) seed++;
            foreach (string death in new[] { "event", "sweep", "destroyed", "alive" })
            {
                var telemetry = new ModeHCombatTelemetry(); telemetry.BeginMatch(1, seed, null);
                var starter = new ModeHParticipantRef { ProfileId = "starter", StableKey = "blood", Character = new CharacterMainControl() };
                var relay = new ModeHParticipantRef { ProfileId = "relay", IsRelay = true };
                telemetry.OnFighterEntered(starter);
                telemetry.OnEnemyEntered(new ModeHParticipantRef { IsEnemy = true, StableKey = "enemy", Character = new CharacterMainControl() });
                var control = new ModeHCombatControl(); control.Configure(telemetry, starter, relay, seed);
                starter.Character.Health.CurrentHealth = death == "alive" ? 20f : 0f;
                if (death == "event") { starter.Character.Health.IsDead = true; telemetry.OnParticipantDead(starter, null); }
                if (death == "sweep") starter.Character.Health.IsDead = true;
                if (death == "destroyed") UnityEngine.Object.Destroy(starter.Character.gameObject);
                bool terminal = control.Tick(0.01f, null);
                if (death == "alive")
                {
                    Check(terminal && telemetry.HasResult && telemetry.Result.CowardiceType == ModeHStableIds.AnomalyCowardBlood,
                        "living blood coward still rolls the original deterministic chance");
                    continue;
                }
                Check(!terminal && !telemetry.HasResult && control.RelayOpen,
                    "down starter must open relay instead of forfeiting by cowardice: " + death);
                Check(telemetry.IsDown(starter.ProfileId) && control.CowardChecks == 0,
                    "confirmed down must neither consume nor produce a coward check: " + death);
            }
            var body = new CharacterMainControl { SavedWeapon = 0, HeldWeapon = -1 };
            var fighter = new CharacterMainControl();
            body.ControlOtherCharacterAction = new CA_ControlOtherCharacter { Owner = body, targetCharacter = fighter, Running = true };
            LevelManager.Instance = new LevelManager { Main = body, ControllingCharacter = fighter };
            var swap = new ModeHCombatControl(); swap.ConfigureSwap(body, fighter);
            swap.RestoreErrorSwap(); swap.RestoreErrorSwap();
            Check(!body.ControlOtherCharacterAction.Running && body.ControlOtherCharacterAction.StopCount == 1,
                "ERROR restore must stop its official action exactly once before recycling its live target");
            Check(ReferenceEquals(LevelManager.Instance.ControllingCharacter, body) && body.HeldWeapon == 0 && body.SavedWeapon == -1,
                "ERROR restore must restore the original weapon while inventory editing is allowed");
            Console.WriteLine("Combat control release audit: PASS (" + _checks + " assertions)");
        }
    }
}
