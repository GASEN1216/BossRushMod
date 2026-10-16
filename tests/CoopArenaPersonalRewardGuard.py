"""Receipt-owned full solo rewards retain native private box interaction and next-wave audience."""
from pathlib import Path
from cs_source_util import clean_source
from CoopGameplaySceneGuard import body
ROOT=Path(__file__).resolve().parents[1]
def read(path):return clean_source((ROOT/path).read_text(encoding='utf-8-sig'))
def main():
    rewards=read('WavesArena/WavesArenaRuntimeModule_CoopRewards.cs')
    dispatch=body(rewards,'private void DeliverCoopArenaReward(')
    assert 'foreach (var participant in coopArenaWaveParticipants)' in dispatch and '}, participant.Value);' in dispatch,'full personal reward targets the exact frozen wave asset'
    apply=body(rewards,'private bool ApplyCoopArenaReward(')
    assert 'reward.Room != BossRushCoop.RunId || reward.Asset != BossRushCoop.LocalAssetId' in apply,'personal ledger never commits on a replacement room/asset'
    tick=body(rewards,'private void TickCoopArenaReward(')
    assert 'InfiniteHellMilestoneDelivery.TryCreditAccount(ref reward.Cash)' in tick,'physical overflow remains the original local asset account obligation'
    assert 'reward.Cash -= amount; reward.CashPiles++;' in tick and 'reward.ItemDelivered = true;' in tick,'only accepted inventory quantities settle cash and every-five items'
    assert 'fact.Milestone - 1' in tick and 'reward.Milestone.IsComplete(fact.Milestone)' in tick,'one original milestone keeps the complete tier quantity without replaying past tiers'
    assert 'reward.Shadow = shadow;' in tick and 'SpawnDifficultyRewardLootbox_LootAndRewards(fact.Difficulty, true, position)' in tick,'original full box and visual handoff are used for personal victory'
    cleanup=body(rewards,'private void CleanupCoopArenaRewards()')
    assert cleanup.index('CancelCoopArenaReward(reward);')<cleanup.index('coopArenaRewards.Clear();'),'destroy pending original visual owners before clearing references'
    cancel=body(rewards,'private static void CancelCoopArenaReward(')
    assert 'reward.Cancelled = true;' in cancel and 'UnityEngine.Object.Destroy(reward.Shadow.gameObject);' in cancel,'cancel original visual before losing its owner reference'
    assert 'if (!reward.StandardLoading && !reward.Complete && reward.Box != null)' in cancel,'pending native factory retains its original inventory until the actual await completes'
    standard=body(rewards,'private async void TickCoopArenaStandardBox(')
    assert 'await loader.Setup();' in standard and 'capacity.SetValue(loader, Math.Max(1, reward.Fact.Difficulty)' in standard,'await full native setup with enough original inventory capacity'
    assert 'loader.autoSetup = false; loader.dropOnSpawnItem = false;' in standard,'native personal setup cannot spill unaccounted items outside the original box'
    assert 'reward.Cancelled || owner == null || reward.Box == null || reward.Room != BossRushCoop.RunId' in standard,'cancelled original native continuation cannot publish into another owner'
    assert 'if (reward.Box != null) { TickCoopArenaStandardBox(reward); return; }' in tick,'existing partial standard box must await full setup and cleanup rather than count as delivered'
    finalize=body(rewards,'private IEnumerator FinalizeCoopArenaStandardBox(')
    assert 'yield return CleanupDifficultyRewardLootboxInventory_LootAndRewards(reward.Box, reward.Fact.Difficulty);' in finalize and 'if (qualityCount >= reward.Fact.Difficulty) reward.Complete = CoopArenaPersonalLoot.TryPublish(reward.Box);' in finalize,'native standard rewards retain full original high-quality quantity before publication'
    assert 'CleanupCoopArenaRewards();' in body(read('WavesArena/WavesArenaRuntimeModule.cs'),'public override void OnDestroy()'),'runtime owns personal visual cleanup'
    marker=read('LootAndRewards/CoopArenaPersonalLoot.cs')
    native=body(marker,'private static bool NativePrivateInventory(')
    assert 'if (!IsPersonal(__0)) return true;' in native and '__result = true;' in native and 'return false;' in native,'native privacy override applies only to marked personal inventory'
    assert '"IsPrivateInventory", new[] { typeof(Inventory) }' in body(marker,'private static bool EnsureNativePrivateHook()'),'hook the actual installed native privacy predicate'
    interact=body(marker,'private static bool BeforeInteract(')
    assert 'marker.ready && BossRushCoop.IsConnected' in interact,'partial personal crate cannot open before complete publication'
    assert 'marker.room == BossRushCoop.RunId && marker.asset == BossRushCoop.LocalAssetId' in interact,'physical open preserves receipt owner'
    assert 'UnityEngine.Object.DontDestroyOnLoad(value.gameObject);' in body(marker,'internal static bool Attach('),'uncompleted original personal inventory survives mandatory scene unload'
    publish=body(marker,'internal static bool TryPublish(')
    assert publish.index('MultiSceneCore.MoveToActiveWithScene(')<publish.index('marker.ready = true;'),'completed original box resumes normal scene lifetime before opening'
    assert 'marker.sceneHandle = SceneManager.GetActiveScene().handle;' in body(marker,'internal static bool Attach(') and 'if (marker.sceneHandle != scene.handle)' in publish,'same-name or same-index reload preserves actual scene instance ownership'
    assert 'manager.MainCharacter != player' in publish and '!LevelManager.AfterInit' in publish and 'SceneLoader.IsSceneLoading' in publish,'relocate only after the actual original asset player has finished loading'
    assert 'if (!SpawnPositionHelper.TrySnapToGround(player.transform.position, out landing)) return false;' in publish and 'value.transform.position = landing;' in publish,'cross-scene original box returns to its owner reachable ground before ACK'
    victory=body(read('WavesArena/WavesArenaRuntimeModule_VictoryRewards.cs'),'private InteractableLootbox SpawnDifficultyRewardLootbox_LootAndRewards(')
    assert victory.index('CoopArenaPersonalLoot.Attach(lootbox)')<victory.index('loader.StartSetup();'),'marker exists before native Loader registration'
    assert 'else BossRushCoop.RegisterWorldLoot(lootbox);' in victory,'ordinary single-player/shared boxes retain their original registration'
    assert 'if (!BossRushCoop.IsConnected) StartVictoryRewardShadowCrate_LootAndRewards(rewardHighCount);' in read('WavesArena/WavesArenaRuntimeModule_VictoryRewards.cs'),'single-player original visual is preserved without duplicate host COOP share'
    for path,cohort,sig in [('ModeD/ModeDRuntimeModule_Coop.cs','coopWaveParticipants','private void ExecuteCoopModeD('),('WavesArena/WavesArenaRuntimeModule_Coop.cs','coopArenaWaveParticipants','private void ExecuteCoopArena(')]:
        src=read(path);command=body(src,sig)
        assert cohort+'[' not in command and cohort+'.Add(' not in command,'manual mid-wave admission cannot rewrite frozen current wave: '+path
        apply=body(src,'private void ApplyCoopModeD(' if path.startswith('ModeD') else 'private void ApplyCoopArena(')
        assert 'state.WavePlayers.Length == state.WaveAssets.Length' in apply and cohort+'[state.WavePlayers[i]] = state.WaveAssets[i];' in apply,'mode snapshot transfers exact current audience to clients: '+path
    wave=body(read('WavesArena/WavesArenaRuntimeModule_InfiniteHellCompletion.cs'),'internal async void OnInfiniteHellWaveCompleted_LootAndRewards()')
    assert 'DeliverCoopArenaReward("waveReward", cashThisWave, 0, itemType, 0);' in wave and 'int copies = personalRewards ? 0 : 1;' in wave,'COOP cash/item goes only through full personal shares while solo retains original path'
    shadow=read('LootAndRewards/VictoryRewardShadowCrateController.cs').split('public sealed class ModeGRewardStrictMaterializer',1)[0]
    assert 'if (personalOwnerCurrent != null && !personalOwnerCurrent()) { CleanupAndDestroy(true); return; }' in body(shadow,'private void Update()'),'cancelled personal visual destroys its controller reference so the original receipt can retry'
    print('CoopArenaPersonalRewardGuard: PASS')
if __name__=='__main__':
    try:main()
    except AssertionError as error:print('CoopArenaPersonalRewardGuard: FAIL - '+str(error));raise SystemExit(1)
