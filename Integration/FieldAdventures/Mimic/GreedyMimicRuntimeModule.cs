using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed class GreedyMimicRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private GreedyMimicEncounter encounter;
        private LevelManager raidOwner;
        private int spawnAttempts;
        private bool spawned;
        private float nextCheck, readyTime;
        public override string ModuleName { get { return "GreedyMimic"; } }
        public override void OnAwake(ModBehaviour host) { owner = host; }
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            if (encounter != null && (!encounter.Scene.IsValid() || !encounter.Scene.isLoaded || context.LoadMode == LoadSceneMode.Single)) Cleanup();
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            try
            {
                CharacterMainControl player;
                Scene scene;
                if (encounter != null)
                {
                    if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene) || scene.handle != encounter.Scene.handle)
                    { Cleanup(); return; }
                    encounter.SyncHudVisibility();
                    if (BossRushUI.IsGamePaused()) return;
                    encounter.Tick(Mathf.Clamp(deltaTime, 0f, 0.1f), player);
                    if (encounter.Finished) Cleanup();
                    return;
                }
                if (BossRushUI.IsGamePaused() || SceneLoader.IsSceneLoading) return;
                nextCheck -= deltaTime;
                if (nextCheck > 0f) return;
                nextCheck = 1f;
                if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene)) return;
                if (!ReferenceEquals(raidOwner, LevelManager.Instance))
                { raidOwner = LevelManager.Instance; spawned = false; spawnAttempts = 0; readyTime = 6f; }
                if (spawned) return;
                readyTime -= 1f;
                if (readyTime > 0f) return;
                Vector3 position;
                if (++spawnAttempts > 12) { spawned = true; return; }
                if (!FieldAdventureSupport.TryFindGround(player.transform.position, 16f, 24f, out position)) return;
                // 成长后的箱体和药瓶远大于普通落点胶囊，额外预留完整模型净空。
                if (Physics.CheckBox(position + Vector3.up * 3f, new Vector3(2.5f, 2.9f, 2.3f), Quaternion.identity,
                    Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return;
                spawned = true;
                encounter = new GreedyMimicEncounter(owner, scene, position, player);
                FieldAdventureSupport.Notify("附近有一只肚子咕咕叫的巨大宝箱。靠近选择投喂，或直接开席。", "A giant hungry chest is rumbling nearby. Approach to feed it, or start its feast.");
            }
            catch (Exception e) { Debug.LogWarning("[GreedyMimic] 遭遇运行失败，已清理: " + e.Message); Cleanup(); }
        }
        private void Cleanup() { if (encounter != null) encounter.Dispose(); encounter = null; }
        public override void OnDestroy() { Cleanup(); owner = null; raidOwner = null; spawned = false; }
    }
}
