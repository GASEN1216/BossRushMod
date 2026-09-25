"""征程终章、模式观察与兼容入口归同一运行时；行为由 CampaignPlayability 执行。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
FINAL = "Campaign/CampaignFinalBoss.cs"
MODE = "Campaign/CampaignModeBridge.cs"
RUNTIME = "Campaign/CampaignRuntimeModule.cs"
HOST = "Campaign/CampaignRuntimeModuleHostBridge.cs"
ENTRIES = {
    "internal bool CanStartCampaignFinalBoss()": "return campaignRuntime.CanStartCampaignFinalBoss();",
    "internal void TickCampaignFinalBossAltar()": "campaignRuntime.TickCampaignFinalBossAltar();",
    "internal void StartCampaignFinalBoss()": "campaignRuntime.StartCampaignFinalBoss();",
    "internal bool DebugStartCampaignFinalBossForValidation()": "return campaignRuntime.DebugStartCampaignFinalBossForValidation();",
    "internal void TickCampaignFinalBossYield()": "campaignRuntime.TickCampaignFinalBossYield();",
    "internal void CleanupCampaignFinalBoss(bool destroyBoss)": "campaignRuntime.CleanupCampaignFinalBoss(destroyBoss);",
    "internal void TickCampaignModeBridge(float deltaTime)": "campaignRuntime.TickCampaignModeBridge(deltaTime);",
    "internal string ResolveCampaignCurrentMode()": "return campaignRuntime.ResolveCampaignCurrentMode();",
    "internal int GetCampaignCurrentWave()": "return campaignRuntime.GetCampaignCurrentWave();",
    "internal bool HasCampaignBountyMark(CharacterMainControl boss)": "return campaignRuntime.HasCampaignBountyMark(boss);",
    "internal bool ConsumeCampaignBountyMark(CharacterMainControl boss)": "return campaignRuntime.ConsumeCampaignBountyMark(boss);",
    "internal void NotifyCampaignStandardCleared()": "campaignRuntime.NotifyCampaignStandardCleared();",
    "internal void NotifyCampaignModeDWaveComplete(int waveIndex)": "campaignRuntime.NotifyCampaignModeDWaveComplete(waveIndex);",
    "internal void NotifyCampaignModeFExtracted()": "campaignRuntime.NotifyCampaignModeFExtracted();",
    "internal void NotifyCampaignZombieExtracted()": "campaignRuntime.NotifyCampaignZombieExtracted();",
}
STATE = (
    "CharacterMainControl campaignFinalBossInstance", "bool campaignFinalBossActive",
    "int campaignFinalBossRunId", "bool campaignFinalBossSpawnResolved",
    "int campaignArenaSceneGeneration = -1", "bool campaignArenaSceneIsValid",
    "int campaignFinalBossDeathPresentationCount", "GameObject campaignFinalBossAltar",
    "float campaignAltarRetryAt", "int campaignLastObservedWave", "string campaignLastObservedMode",
)


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def compact(value):
    return re.sub(r"\s+", " ", value).strip()


def body(value, signature):
    start = value.index(signature)
    start = value.index("{", start) + 1
    end, depth = start, 1
    while depth:
        depth += (value[end] == "{") - (value[end] == "}")
        end += 1
    return compact(value[start:end - 1])


def main():
    checks = 0

    def check(condition, label):
        nonlocal checks
        assert condition, label
        checks += 1

    def ordered(value, label, *tokens):
        previous = -1
        for token in tokens:
            current = value.find(token, previous + 1)
            check(current >= 0, label + ": " + token)
            previous = current

    final, mode, runtime, host = map(source, (FINAL, MODE, RUNTIME, HOST))
    for path, value in ((FINAL, final), (MODE, mode)):
        check("internal sealed partial class CampaignRuntimeModule" in value, path + " lost runtime owner")
        check("partial class ModBehaviour" not in value, path + " retained host business")
    for field in STATE:
        declaration = "private " + field + ";"
        check(declaration in final + mode, "missing module state: " + field)
        check(declaration not in host, "host retained state: " + field)
    for signature, statement in ENTRIES.items():
        check(body(host, signature) == statement, "host bridge disconnected: " + signature)
        check(signature in final + mode, "runtime entry missing: " + signature)
    for name, target in (("campaignFinalBossActive", "IsCampaignFinalBossActive"),
                         ("IsCampaignFinalBossActive", "IsCampaignFinalBossActive"),
                         ("CampaignFinalBossDeathPresentationCount", "CampaignFinalBossDeathPresentationCount"),
                         ("CampaignFinalBossInstanceForValidation", "CampaignFinalBossInstanceForValidation")):
        check(re.search(r"\b" + name + r"\s*\{\s*get\s*\{\s*return campaignRuntime\." + target + r";\s*\}\s*\}", host), "getter disconnected: " + name)
    for name, target in (("CampaignArenaActiveForRuntime", "bossRushArenaActive"),
                         ("CampaignModeDActiveForRuntime", "modeDActive"),
                         ("CampaignModeEActiveForRuntime", "modeEActive"),
                         ("CampaignModeFActiveForRuntime", "modeFActive"),
                         ("CampaignModeGActiveForRuntime", "modeGActive"),
                         ("CampaignInfiniteHellForRuntime", "infiniteHellMode"),
                         ("CampaignInfiniteHellWaveForRuntime", "infiniteHellWaveIndex"),
                         ("CampaignEnemyIndexForRuntime", "currentEnemyIndex"),
                         ("CampaignZombieRunForRuntime", "zombieModeRunState"),
                         ("CampaignModeFStateForRuntime", "modeFState")):
        check(re.search(r"\b" + name + r"\s*\{\s*get\s*\{\s*return " + target + r";\s*\}\s*\}", host), "host query drifted: " + name)
    check(body(host, "internal bool ConsumeCampaignBountyKillLatchForRuntime(") == "return ConsumeModeFPlayerBountyKillLatch(victimId);", "bounty consumption no longer synchronous")
    check(body(host, "internal bool HasCampaignBountyKillLatchForRuntime(") == "return HasModeFPlayerBountyKillLatch(victimId);", "bounty query no longer pure")
    ordered(body(runtime, "public override void OnSceneLoaded("), "scene lifecycle",
            "_sceneGeneration++;", "CampaignObjectiveTracker.ResetSession();", "CampaignDialoguePlayer.InvalidatePlayback();",
            "_questClient.ClearPending();", "if (_owner != null) { CleanupCampaignFinalBoss(false); }")
    ordered(body(runtime, "public override void OnUpdate("), "update lifecycle",
            "if (_owner != null) { TickCampaignModeBridge(deltaTime); }", "CampaignHud.Tick();",
            "CampaignProgressService.RetryPendingObjectives(unscaledDeltaTime);", "CampaignSaveCoordinator.Tick();")
    ordered(body(runtime, "public override void OnDestroy()"), "destroy lifecycle",
            "if (_owner != null) CleanupCampaignFinalBoss(true);", "_questClient.UnregisterAll();", "_owner = null;")
    ordered(body(runtime, "private void ShutdownIfEnabledTurnedOff()"), "disabled lifecycle",
            "CampaignSaveCoordinator.TryFlushOnHostDestroy();", "CampaignSaveCoordinator.ShutdownSubscription();",
            "if (_owner != null) CleanupCampaignFinalBoss(true);", "_questClient.UnregisterAll();")
    ordered(body(final, "internal void CleanupCampaignFinalBoss("), "final cleanup",
            "OnDeadEvent.RemoveListener(OnCampaignFinalBossDead);", "if (destroyBoss)",
            "_owner.ClearCampaignBossRandomLootTrackingForRuntime(campaignFinalBossInstance);",
            "UnityEngine.Object.Destroy(campaignFinalBossInstance.gameObject);", "StopBossBGM(",
            "if (campaignFinalBossActive) CampaignDialoguePlayer.InvalidatePlayback();",
            "campaignFinalBossInstance = null;", "campaignFinalBossActive = false;",
            "campaignFinalBossRunId++;", "ResetCampaignFinalBossTracking();")
    ordered(body(final, "private async UniTask StartCampaignFinalBossAsync("), "spawn ownership",
            "ModBehaviour campaignOwner = _owner;", "await campaignOwner.SpawnPhantomWitch(", "if (runId != campaignFinalBossRunId)",
            "campaignOwner.ClearCampaignBossRandomLootTrackingForRuntime(boss);", "UnityEngine.Object.Destroy(boss.gameObject);",
            "campaignFinalBossSpawnResolved = true;", "ApplyCampaignFinalBossVariant(boss);",
            "OnDeadEvent.AddListener(OnCampaignFinalBossDead);")
    ordered(body(mode, "internal void TickCampaignModeBridge("), "tick sequence",
            "if (!_owner.IsCampaignConfiguredEnabled()) return;", "TickCampaignFinalBossAltar();", "TickCampaignFinalBossYield();",
            "if (campaignFinalBossActive)", "CampaignObjectiveTracker.Tick(deltaTime);", "ResolveCampaignCurrentMode();")
    print("CampaignRuntimeOwnershipGuard: PASS (" + str(checks) + " assertions)")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, IndexError) as error:
        print("CampaignRuntimeOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
