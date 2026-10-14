using System;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopCampaignFact
    {
        public string Kind, Mode, RunId;
        public int SceneGeneration;
        public bool Melee, Boss, Bounty;
        public int Wave;
    }

    [Serializable]
    internal sealed class CoopCampaignWorld
    {
        public bool FinalActive, SpawnResolved;
        public int FinalRun;
        public string Boss;
    }

    internal sealed partial class CampaignRuntimeModule
    {
        private static CampaignRuntimeModule coopOwner;
        private CoopPersonalDelivery<CoopCampaignFact> coopFacts;
        private CoopGameplayChannel<CoopCampaignWorld> coopWorld;
        private bool coopRemoteFinalAdmission;

        private void InitializeCoopCampaign()
        {
            coopOwner = this;
            coopFacts = new CoopPersonalDelivery<CoopCampaignFact>("Campaign", ApplyCoopCampaignFact);
            coopWorld = new CoopGameplayChannel<CoopCampaignWorld>("Campaign", CaptureCoopCampaignWorld, ApplyCoopCampaignWorld, ExecuteCoopCampaignCommand);
        }

        private CoopCampaignWorld CaptureCoopCampaignWorld()
        {
            return new CoopCampaignWorld
            {
                FinalActive = campaignFinalBossActive, SpawnResolved = campaignFinalBossSpawnResolved,
                FinalRun = campaignFinalBossRunId, Boss = BossRushCoop.GetEntityId(campaignFinalBossInstance)
            };
        }

        private void ApplyCoopCampaignWorld(CoopCampaignWorld state)
        {
            if (state.FinalActive && !campaignFinalScopeBound) CaptureCampaignFinalScope();
            if (!IsCampaignFinalScopeValid()) return;
            campaignFinalBossActive = state.FinalActive;
            campaignFinalBossSpawnResolved = state.SpawnResolved;
            campaignFinalBossRunId = state.FinalRun;
            campaignFinalBossInstance = BossRushCoop.FindEntity(state.Boss);
            CoopAiPredictionHealthProtection.MarkReplica(campaignFinalBossInstance);
            CharacterMainControl local = CharacterMainControl.Main;
            if (state.FinalActive && local != null && local.Health != null && !local.Health.IsDead)
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
        }

        private void ExecuteCoopCampaignCommand(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (command.Action != "final-start" || player == null || !player.IsAlive || !IsCampaignArenaSceneCached()
                || _owner == null || !_owner.CampaignArenaActiveForRuntime || campaignFinalBossActive || IsAnyGameplayModeActiveForCampaign()) return;
            // 章节资格在请求者自己的本地入口判定；服务器只接管已通过资格入口的共享召唤。
            coopRemoteFinalAdmission = true;
            try { StartCampaignFinalBoss(); }
            finally { coopRemoteFinalAdmission = false; }
        }

        internal static bool ReportCoopCampaignKill(CharacterMainControl victim, bool melee, bool boss, bool bounty)
        {
            if (!BossRushCoop.IsConnected) return false;
            if (!BossRushCoop.IsHost || coopOwner == null || victim == null) return true;
            coopOwner.DeliverCoopCampaignFact("kill:" + victim.GetInstanceID(), new CoopCampaignFact
            { Kind = "kill", Melee = melee, Boss = boss, Bounty = bounty });
            return true;
        }

        private bool DeliverCoopCampaignFact(string id, CoopCampaignFact fact)
        {
            if (!BossRushCoop.IsHost) return false;
            fact.RunId = BossRushCoop.RunId; fact.SceneGeneration = BossRushCoop.SceneGeneration;
            if (string.IsNullOrEmpty(fact.Mode)) fact.Mode = ResolveCampaignWorldMode();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsConnected) coopFacts.Deliver(player.Id, id, fact);
            return true;
        }

        private bool ApplyCoopCampaignFact(CoopCampaignFact fact)
        {
            CharacterMainControl local = CharacterMainControl.Main;
            if (fact == null || fact.RunId != BossRushCoop.RunId || fact.SceneGeneration != BossRushCoop.SceneGeneration
                || local == null || local.Health == null || local.Health.IsDead) return true;
            // 只提交自己已经接受的章节；未武装者确认收到事实即可，不复制房主任务/存档。
            if (!IsEnabled || !CampaignObjectiveTracker.IsArmed && fact.Kind == "kill") return true;
            if (!string.IsNullOrEmpty(fact.Mode)) CampaignObjectiveTracker.EnsureArmedFor(fact.Mode);
            switch (fact.Kind)
            {
                case "kill": CampaignObjectiveTracker.ReportPlayerKill(fact.Melee, fact.Boss, fact.Bounty); break;
                case "standard": CampaignObjectiveTracker.ReportStandardClear(); break;
                case "wave": CampaignObjectiveTracker.ReportWaveReached(fact.Wave); break;
                case "extract": CampaignObjectiveTracker.ReportExtract(); break;
                case "final": CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal); CampaignObjectiveTracker.ReportFinalBossKill(); break;
            }
            return true;
        }
    }
}
