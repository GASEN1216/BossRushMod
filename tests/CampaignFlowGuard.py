#!/usr/bin/env python3
"""征程玩家入口、终章隔离和清理接线；计数/事务/异步时序由执行回归验证。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    start = source.index(signature)
    start = source.index("{", start) + 1
    end, depth = start, 1
    # These selected methods have no brace literals.
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end - 1]).strip()


def check():
    errors = []

    def require(path, signature, statement):
        if statement not in body(path, signature):
            errors.append(f"{path} / {signature}: 缺少 {statement}")

    runtime = "Campaign/CampaignRuntimeModule.cs"
    require("ModeD/ModeDEquipment_StarterKit.cs", "private void GivePlayerStarterKit()",
            "if (UnityEngine.Random.value > 0.6f || (modeDActive && IsCampaignConfiguredEnabled() && CampaignObjectiveTracker.NeedsMeleeStarterKit())) { GiveRandomMeleeWeapon(main); }")
    require("Campaign/CampaignBoardInteractable.cs", "protected override void OnInteractCompleted()", "ModBehaviour.Instance.ShowMessage(L10n.T(")
    require("Campaign/CampaignBoardBuilder.cs", "private void InitCampaignBoardBuilding(bool isEarlyInit)", "if (presence == CampaignBoardPresence.Unknown)")
    require(runtime, "public override void OnSceneLoaded(", "CampaignObjectiveTracker.ResetSession();")
    require(runtime, "public override void OnSceneLoaded(", "CampaignDialoguePlayer.InvalidatePlayback();")
    require(runtime, "public override void OnDestroy()", "_owner.CleanupCampaignFinalBoss(true);")
    require(runtime, "private void ShutdownIfEnabledTurnedOff()", "_owner.CleanupCampaignFinalBoss(true);")
    require(runtime, "private void ShutdownIfEnabledTurnedOff()", "CampaignDialoguePlayer.InvalidatePlayback();")
    require(runtime, "public override void OnUpdate(", "CampaignProgressService.RetryPendingObjectives(unscaledDeltaTime);")
    require(runtime, "internal void EnsureBootstrapped()", "_questClient.RegisterAll(_owner != null && _owner.OfficialQuestRuntime != null ? _owner.OfficialQuestRuntime.Projection : null);")
    require(runtime, "public override void OnDestroy()", "if (_questClient != null) _questClient.UnregisterAll();")
    require(runtime, "private void ShutdownIfEnabledTurnedOff()", "if (_questClient != null) _questClient.UnregisterAll();")
    require(runtime, "public override void OnSceneLoaded(", "if (_questClient != null) _questClient.ClearPending();")
    client = "Campaign/CampaignOfficialQuestClient.cs"
    require(client, "public void EndTick(bool dirty)", "if (BossRushUI.IsOfficialHudHidden() || BossRushUI.IsGamePaused()) return;")
    require(client, "public void EndTick(bool dirty)", "CampaignDialoguePlayer.PlayChapterDelivered(def);")
    require(client, "public void EndTick(bool dirty)", "_pendingNotice = CampaignContentCatalog.GetDeliveredNotice(def.ChapterId);")
    require(client, "public void RefreshMarkers()", "OfficialQuestGiverLocator.RefreshMarkerFor(CampaignQuestTable.JeffGiverId);")
    require("Campaign/CampaignObjectiveTracker.cs", "internal static void EnsureArmedFor(string mode)", "if (def.Objectives[i] == null || def.Objectives[i].IsBaseScope) continue;")
    final = "Campaign/CampaignFinalBoss.cs"
    require(final, "internal bool CanStartCampaignFinalBoss()", "return ShouldCampaignFinalBossAltarExist();")
    require(final, "private async UniTask StartCampaignFinalBossAsync(", "CampaignTuning.FinalBossScale, isNonWaveSpawn: true);")
    require(final, "private void CreateCampaignAltarPart(", "renderer.sharedMaterial = material;")
    require(final, "private Vector3 ResolveCampaignFinalBossSpawnPosition()", "SpawnPositionHelper.FindNearestSafeSpawnPoint(points, main.transform.position, 8f);")
    require(final, "internal void TickCampaignFinalBossAltar()", "if (Time.unscaledTime < campaignAltarRetryAt) return;")
    require(final, "internal void CleanupCampaignFinalBoss(", "if (campaignFinalBossActive) CampaignDialoguePlayer.InvalidatePlayback();")
    require("Campaign/CampaignAssetCache.cs", "internal static Material GetAltarMaterial()", "_ownedObjects.Add(_altarMaterial);")
    require("Campaign/CampaignHud.cs", "private static bool HasProgressChanged(", "if (_shownChinese != L10n.IsChinese) return true;")
    require("Campaign/CampaignHud.cs", "internal static void Tick()", "BossRushUI.MeasureTextHeight(_bodyText, 356f, 36f);")
    require("Campaign/CampaignDialoguePlayer.cs", "private static async UniTask PlayChapterDeliveredAsync(",
            "if (generation == _playbackGeneration) CampaignNoteBridge.UnlockClue(def.ClueId);")
    require("Campaign/CampaignDialoguePlayer.cs", "private static async UniTask PlayChapterDeliveredAsync(",
            'await DialogueManager.ShowDialogueSequenceBilingual( actor, lines, "BossRush_Campaign_" + def.ChapterId, PlaybackToken());')
    require("Campaign/CampaignDialoguePlayer.cs", "internal static async UniTask PlayFinalBossPrologueAsync()",
            'await DialogueManager.ShowDialogueSequenceBilingual( actor, BuildFinalBossPrologueLines(), "BossRush_Campaign_FinalBossPrologue", PlaybackToken());')
    require("Campaign/CampaignDialoguePlayer.cs", "internal static void InvalidatePlayback()", "previous.Cancel();")
    require("Campaign/CampaignProgressService.cs", "internal static void NotifySlotChanged()", "_pendingObjectiveChapterId = null;")
    require("Campaign/CampaignProgressService.cs", "internal static void NotifySlotChanged()", "ModBehaviour.Instance?.CleanupCampaignFinalBoss(true);")
    require("Campaign/CampaignProgressService.cs", "internal static void NotifySlotChanged()", "CampaignDialoguePlayer.InvalidatePlayback();")

    for path, signature, statement in (
        ("Utilities/PlayerLifecycleRuntimeHooks.cs", "internal void RegisterPlayerLifecycleRuntimeEvents()", "Health.OnDead += CampaignObjectiveCollector.OnGlobalDead;"),
        ("Utilities/PlayerLifecycleRuntimeHooks.cs", "internal void RegisterPlayerLifecycleRuntimeEvents()", "Health.OnHurt += CampaignObjectiveCollector.OnGlobalHurt;"),
        ("LootAndRewards/LootAndRewardsVictoryRewards.cs", "private async void OnAllEnemiesDefeated_LootAndRewards()", "NotifyCampaignStandardCleared();"),
        ("ModeD/ModeDWaves.cs", "private void OnModeDWaveComplete()", "NotifyCampaignModeDWaveComplete(modeDWaveIndex);"),
        ("ModeF/ModeFExtraction.cs", "private void OnModeFExtractionSuccess()", "NotifyCampaignModeFExtracted();"),
        ("ZombieMode/ZombieModeExtractionController.cs", "private void CompleteZombieModeExtractionSuccess(", "NotifyCampaignZombieExtracted();"),
    ):
        require(path, signature, statement)
    return errors


if __name__ == "__main__":
    failures = check()
    for failure in failures:
        print(failure)
    print("CampaignFlowGuard: " + ("FAIL" if failures else "PASS"))
    raise SystemExit(bool(failures))
